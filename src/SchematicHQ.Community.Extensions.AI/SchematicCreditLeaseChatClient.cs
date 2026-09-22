using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SchematicHQ.Client;
using SchematicHQ.Client.RulesEngine;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.AI;

/// <summary>
/// Chat pipeline middleware for credit-burndown entitlements. Before the model is invoked it checks the
/// flag, estimates the call's usage and reserves that many credits with a lease; afterwards it tracks
/// the actual usage against the lease and releases the remainder, so concurrent or long calls cannot
/// overspend a balance. Denied calls throw <see cref="SchematicFeatureDeniedException"/>. When the
/// entitlement is not credit-based the call is gated and tracked without a lease.
/// </summary>
public sealed class SchematicCreditLeaseChatClient : DelegatingChatClient
{
    private readonly ISchematicGateClient _schematic;
    private readonly string _flagKey;
    private readonly SchematicCreditLeaseOptions _options;
    private readonly ILogger<SchematicCreditLeaseChatClient> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly Func<CheckFlagWithEntitlementResponse, bool> _canOverdraw;

    public SchematicCreditLeaseChatClient(
        IChatClient innerClient,
        ISchematicGateClient schematic,
        string flagKey,
        SchematicCreditLeaseOptions options,
        ILogger<SchematicCreditLeaseChatClient> logger,
        IHttpContextAccessor? httpContextAccessor = null)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(schematic);
        ArgumentException.ThrowIfNullOrWhiteSpace(flagKey);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _schematic = schematic;
        _flagKey = flagKey;
        _options = options;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        // Overdraft only excuses a denial about credit: a company with no entitlement at all is still denied.
        _canOverdraw = response => _options.AllowOverdraft && response.Entitlement?.CreditId is not null;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Estimation enumerates the messages once already; do not hand a lazy sequence downstream.
        var history = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var call = await BeginAsync(history, options, cancellationToken);

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(history, options, cancellationToken);
        }
        catch
        {
            await SettleAsync(call, usage: null, modelId: null);
            throw;
        }

        await SettleAsync(call, response.Usage, response.ModelId);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var history = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var call = await BeginAsync(history, options, cancellationToken);

        var usage = new AiUsageAccumulator();
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(history, options, cancellationToken))
            {
                usage.Add(update);
                yield return update;
            }
        }
        finally
        {
            await SettleAsync(call, usage.Usage, usage.ModelId);
        }
    }

    /// <summary>Gates the call and, for a credit-backed entitlement, takes the hold.</summary>
    private async ValueTask<CallState?> BeginAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var gate = await AiEntitlementGate.CheckAsync(
            _schematic, _flagKey, _options, _httpContextAccessor, _logger, cancellationToken, tolerateDenial: _canOverdraw);

        // No identity: nothing to reserve against or attribute usage to.
        if (gate.Context is null)
            return null;

        // The check said no and the call was allowed anyway. The balance cannot fund a hold, so the model runs
        // without one and its usage debits the grant directly through the buffered path.
        if (gate.Denied)
            return new CallState(gate.Context, null);

        var entitlement = gate.Response?.Entitlement;
        var companyId = gate.Response?.CompanyId;
        if (entitlement?.CreditId is null || entitlement.ConsumptionRate is null || companyId is null)
        {
            if (gate.Response is not null)
            {
                _logger.LogDebug(
                    "Flag '{FlagKey}' is not backed by a credit entitlement; usage is tracked without a lease.",
                    _flagKey);
            }

            return new CallState(gate.Context, null);
        }

        var estimate = AiUsageTracking.MapEvents(_options, _options.EstimateUsage(messages, options), options?.ModelId);
        var hold = _options.CreditCost(estimate, entitlement);
        if (hold <= 0)
            return new CallState(gate.Context, null);

        SchematicCreditLease? lease;
        try
        {
            lease = await _schematic.AcquireCreditLeaseAsync(
                companyId,
                entitlement.CreditId,
                hold,
                DateTime.UtcNow.Add(_options.LeaseDuration),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Acquiring a Schematic credit lease for flag '{FlagKey}' failed; applying {FailurePolicy}.",
                _flagKey, _options.FailurePolicy);

            if (_options.FailurePolicy == SchematicFailurePolicy.FailOpen)
                return new CallState(gate.Context, null);

            throw new SchematicFeatureDeniedException(_flagKey, "credit_lease_failed", ex);
        }

        if (lease is null)
        {
            _logger.LogInformation("Schematic refused a {Hold} credit hold for flag '{FlagKey}'.", hold, _flagKey);
            await AiEntitlementGate.DenyAsync(
                _options, _logger, _flagKey, "insufficient_credits", gate.Context, gate.Response, tolerated: _canOverdraw(gate.Response!));
            return new CallState(gate.Context, null);
        }

        return new CallState(gate.Context, new Hold(lease, entitlement));
    }

    /// <summary>
    /// Tracks the actual usage (against the lease when one was taken, extending it first if the estimate
    /// fell short) and releases the hold. Never throws: the response has already been produced.
    /// </summary>
    private async ValueTask SettleAsync(CallState? call, UsageDetails? usage, string? modelId)
    {
        if (call is null)
            return;

        try
        {
            IReadOnlyList<SchematicAiUsageEvent> events = usage is null ? [] : AiUsageTracking.MapEvents(_options, usage, modelId);

            if (call.Hold is null)
            {
                foreach (var usageEvent in events)
                    AiUsageTracking.TrackBuffered(_schematic, call.Context, usageEvent);
                return;
            }

            if (events.Count == 0)
                return;

            var cost = _options.CreditCost(events, call.Hold.Entitlement);
            if (cost > call.Hold.Lease.GrantedAmount)
                await ExtendQuietlyAsync(call.Hold.Lease.Id, cost - call.Hold.Lease.GrantedAmount);

            foreach (var usageEvent in events)
                await TrackAgainstLeaseQuietlyAsync(call.Hold.Lease.Id, call.Context, usageEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schematic AI usage tracking failed; the response is unaffected.");
        }
        finally
        {
            if (call.Hold is not null)
                await ReleaseQuietlyAsync(call.Hold.Lease.Id);
        }
    }

    private async ValueTask TrackAgainstLeaseQuietlyAsync(string leaseId, SchematicFlagContext context, SchematicAiUsageEvent usageEvent)
    {
        try
        {
            await _schematic.TrackAgainstLeaseAsync(
                leaseId, usageEvent.EventName, context.Company, context.User,
                usageEvent.Traits ?? new(), usageEvent.Quantity, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Fall back to the buffered path so the usage still burns down once the hold is released.
            _logger.LogError(ex,
                "Tracking '{EventName}' against Schematic lease {LeaseId} failed; sending without the lease.",
                usageEvent.EventName, leaseId);
            AiUsageTracking.TrackBuffered(_schematic, context, usageEvent);
        }
    }

    private async ValueTask ExtendQuietlyAsync(string leaseId, double additionalAmount)
    {
        try
        {
            await _schematic.ExtendCreditLeaseAsync(leaseId, additionalAmount, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Extending Schematic lease {LeaseId} by {Amount} credits failed; usage is tracked against the original hold.",
                leaseId, additionalAmount);
        }
    }

    private async ValueTask ReleaseQuietlyAsync(string leaseId)
    {
        try
        {
            await _schematic.ReleaseCreditLeaseAsync(leaseId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Releasing Schematic lease {LeaseId} failed; it expires on its own.", leaseId);
        }
    }

    private sealed record Hold(SchematicCreditLease Lease, RulesengineFeatureEntitlement Entitlement);

    private sealed record CallState(SchematicFlagContext Context, Hold? Hold);
}
