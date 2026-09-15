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
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var call = await BeginAsync(messages, options, cancellationToken);

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
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
        var call = await BeginAsync(messages, options, cancellationToken);

        UsageDetails? usage = null;
        string? modelId = null;
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                modelId ??= update.ModelId;
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                        (usage ??= new UsageDetails()).Add(usageContent.Details);
                }

                yield return update;
            }
        }
        finally
        {
            await SettleAsync(call, usage, modelId);
        }
    }

    /// <summary>Gates the call and, for a credit-backed entitlement, takes the hold.</summary>
    private async ValueTask<CallState> BeginAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var context = await AiFlagContextResolution.ResolveAsync(_httpContextAccessor, _options)
            ?? throw new SchematicFeatureDeniedException(_flagKey, "no_schematic_context");

        CheckFlagWithEntitlementResponse response;
        try
        {
            response = await _schematic.CheckFlagWithEntitlementAsync(_flagKey, context.Company, context.User, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Schematic entitlement check for flag '{FlagKey}' failed; applying {FailurePolicy}.",
                _flagKey, _options.FailurePolicy);

            if (_options.FailurePolicy == SchematicFailurePolicy.FailOpen)
                return new CallState(context, null, null);

            throw new SchematicFeatureDeniedException(_flagKey, "entitlement_check_failed", ex);
        }

        if (!response.Value)
            throw new SchematicFeatureDeniedException(_flagKey, response.Reason);

        var entitlement = response.Entitlement;
        if (entitlement?.CreditId is null || entitlement.ConsumptionRate is null || response.CompanyId is null)
        {
            _logger.LogDebug(
                "Flag '{FlagKey}' is not backed by a credit entitlement; usage is tracked without a lease.",
                _flagKey);
            return new CallState(context, null, null);
        }

        var estimate = MapEvents(_options.EstimateUsage(messages, options), options?.ModelId);
        var hold = _options.CreditCost(estimate, entitlement);
        if (hold <= 0)
            return new CallState(context, entitlement, null);

        SchematicCreditLease lease;
        try
        {
            lease = await _schematic.AcquireCreditLeaseAsync(
                response.CompanyId,
                entitlement.CreditId,
                hold,
                DateTime.UtcNow.Add(_options.LeaseDuration),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SchematicApiException ex) when (ex.StatusCode is 400 or 402 or 409 or 422)
        {
            // The API rejected the hold itself: not enough credit to cover the estimate.
            _logger.LogInformation(
                "Schematic refused a {Hold} credit hold for flag '{FlagKey}' (HTTP {StatusCode}); call denied.",
                hold, _flagKey, ex.StatusCode);
            throw new SchematicFeatureDeniedException(_flagKey, "insufficient_credits", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Acquiring a Schematic credit lease for flag '{FlagKey}' failed; applying {FailurePolicy}.",
                _flagKey, _options.FailurePolicy);

            if (_options.FailurePolicy == SchematicFailurePolicy.FailOpen)
                return new CallState(context, entitlement, null);

            throw new SchematicFeatureDeniedException(_flagKey, "credit_lease_failed", ex);
        }

        if (lease.GrantedAmount <= 0)
        {
            await ReleaseQuietlyAsync(lease.Id);
            throw new SchematicFeatureDeniedException(_flagKey, "insufficient_credits");
        }

        return new CallState(context, entitlement, lease);
    }

    /// <summary>
    /// Tracks the actual usage (against the lease when one was taken, extending it first if the estimate
    /// fell short) and releases the hold. Never throws: the response has already been produced.
    /// </summary>
    private async ValueTask SettleAsync(CallState call, UsageDetails? usage, string? modelId)
    {
        try
        {
            var events = usage is null ? [] : MapEvents(usage, modelId);

            if (call.Lease is null)
            {
                foreach (var usageEvent in events)
                    TrackBuffered(call.Context, usageEvent);
                return;
            }

            if (events.Count == 0)
                return;

            var cost = _options.CreditCost(events, call.Entitlement!);
            if (cost > call.Lease.GrantedAmount)
                await ExtendQuietlyAsync(call.Lease.Id, cost - call.Lease.GrantedAmount);

            foreach (var usageEvent in events)
                await TrackAgainstLeaseQuietlyAsync(call.Lease.Id, call.Context, usageEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schematic AI usage tracking failed; the response is unaffected.");
        }
        finally
        {
            if (call.Lease is not null)
                await ReleaseQuietlyAsync(call.Lease.Id);
        }
    }

    private IReadOnlyList<SchematicAiUsageEvent> MapEvents(UsageDetails usage, string? modelId)
        => _options.MapUsage(usage, modelId).Where(static e => e.Quantity > 0).ToArray();

    private void TrackBuffered(SchematicFlagContext context, SchematicAiUsageEvent usageEvent)
    {
        var quantity = (int)Math.Min(usageEvent.Quantity, int.MaxValue);
        _schematic.Track(usageEvent.EventName, context.Company, context.User, usageEvent.Traits ?? new(), quantity);
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
            TrackBuffered(context, usageEvent);
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

    private sealed record CallState(
        SchematicFlagContext Context,
        RulesengineFeatureEntitlement? Entitlement,
        SchematicCreditLease? Lease);
}
