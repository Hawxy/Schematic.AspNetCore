using Microsoft.Extensions.Logging;
using SchematicHQ.Client;
using SchematicHQ.Client.RulesEngine;

namespace SchematicHQ.Community.DependencyInjection;

/// <summary>
/// Decorates an <see cref="ISchematicGateClient"/> so every denied entitlement check is logged as a warning
/// and answered as allowed. Track, Identify and credit leases pass straight through. Registered by
/// <c>AddSchematicShadowMode</c> for the rollout window in which gating is wired but not yet enforced.
/// </summary>
public sealed class ShadowModeSchematicGateClient : ISchematicGateClient
{
    private readonly ISchematicGateClient _inner;
    private readonly ILogger<ShadowModeSchematicGateClient> _logger;

    public ShadowModeSchematicGateClient(ISchematicGateClient inner, ILogger<ShadowModeSchematicGateClient> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(logger);
        _inner = inner;
        _logger = logger;
    }

    /// <summary>The client whose denials are being shadowed.</summary>
    public ISchematicGateClient Inner => _inner;

    public async Task<CheckFlagWithEntitlementResponse> CheckFlagWithEntitlementAsync(
        string flagKey,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        CancellationToken cancellationToken)
    {
        var response = await _inner.CheckFlagWithEntitlementAsync(flagKey, company, user, cancellationToken);
        if (response.Value)
            return response;

        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                "Schematic shadow mode: would deny flag '{FlagKey}' for company {Company} / user {User}: {Reason}",
                flagKey,
                SchematicKeyString.Format(company, ','),
                SchematicKeyString.Format(user, ','),
                response.Reason);
        }

        // Returned as a copy: the SDK may return the same instance from its cache, and mutating it would make
        // later hits skip the log.
        return new CheckFlagWithEntitlementResponse
        {
            CompanyId = response.CompanyId,
            Entitlement = response.Entitlement,
            FlagId = response.FlagId,
            FlagKey = response.FlagKey,
            Reason = response.Reason,
            RuleId = response.RuleId,
            RuleType = response.RuleType,
            UserId = response.UserId,
            Value = true,
        };
    }

    public void Track(
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        int? quantity,
        TrackOptions? options = null)
        => _inner.Track(eventName, company, user, traits, quantity, options);

    public void Identify(
        Dictionary<string, string> keys,
        EventBodyIdentifyCompany? company,
        string? name,
        Dictionary<string, object?>? traits,
        IdentifyOptions? options = null)
        => _inner.Identify(keys, company, name, traits, options);

    public Task<SchematicCreditLease?> AcquireCreditLeaseAsync(
        string companyId,
        string creditTypeId,
        double requestedAmount,
        DateTime? expiresAt,
        CancellationToken cancellationToken)
        => _inner.AcquireCreditLeaseAsync(companyId, creditTypeId, requestedAmount, expiresAt, cancellationToken);

    public Task ExtendCreditLeaseAsync(string leaseId, double additionalAmount, CancellationToken cancellationToken)
        => _inner.ExtendCreditLeaseAsync(leaseId, additionalAmount, cancellationToken);

    public Task ReleaseCreditLeaseAsync(string leaseId, CancellationToken cancellationToken)
        => _inner.ReleaseCreditLeaseAsync(leaseId, cancellationToken);

    public Task TrackAgainstLeaseAsync(
        string leaseId,
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        long quantity,
        CancellationToken cancellationToken)
        => _inner.TrackAgainstLeaseAsync(leaseId, eventName, company, user, traits, quantity, cancellationToken);
}
