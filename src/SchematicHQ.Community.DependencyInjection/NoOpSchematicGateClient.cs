using SchematicHQ.Client;
using SchematicHQ.Client.RulesEngine;

namespace SchematicHQ.Community.DependencyInjection;

/// <summary>
/// <see cref="ISchematicGateClient"/> that talks to nothing: Track and Identify are discarded, credit
/// leases are granted in full without being recorded, and entitlement checks answer from
/// <see cref="_allowAll"/> without a network call. Registered by <c>AddSchematicNoOp</c> for
/// environments that have no API key.
/// </summary>
internal sealed class NoOpSchematicGateClient : ISchematicGateClient
{
    private readonly bool _allowAll;

    public NoOpSchematicGateClient(bool allowAll)
    {
        _allowAll = allowAll;
    }

    public Task<CheckFlagWithEntitlementResponse> CheckFlagWithEntitlementAsync(
        string flagKey,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        CancellationToken cancellationToken)
        => Task.FromResult(new CheckFlagWithEntitlementResponse
        {
            FlagKey = flagKey,
            Value = _allowAll,
            Reason = "Schematic is not configured",
        });

    public void Track(
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        int? quantity,
        TrackOptions? options = null)
    {
    }

    public void Identify(
        Dictionary<string, string> keys,
        EventBodyIdentifyCompany? company,
        string? name,
        Dictionary<string, object?>? traits,
        IdentifyOptions? options = null)
    {
    }

    public Task<SchematicCreditLease> AcquireCreditLeaseAsync(
        string companyId,
        string creditTypeId,
        double requestedAmount,
        DateTime? expiresAt,
        CancellationToken cancellationToken)
        => Task.FromResult(new SchematicCreditLease(
            "noop", companyId, creditTypeId, requestedAmount, 0, expiresAt ?? DateTime.UtcNow.AddMinutes(10)));

    public Task<SchematicCreditLease> ExtendCreditLeaseAsync(
        string leaseId,
        double additionalAmount,
        CancellationToken cancellationToken)
        => Task.FromResult(new SchematicCreditLease(
            leaseId, string.Empty, string.Empty, additionalAmount, 0, DateTime.UtcNow.AddMinutes(10)));

    public Task ReleaseCreditLeaseAsync(string leaseId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task TrackAgainstLeaseAsync(
        string leaseId,
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        long quantity,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
