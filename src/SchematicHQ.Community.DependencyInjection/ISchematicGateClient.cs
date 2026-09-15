using SchematicHQ.Client;
using SchematicHQ.Client.RulesEngine;

namespace SchematicHQ.Community.DependencyInjection;

/// <summary>
/// Seam over the subset of <see cref="SchematicHQ.Client.Schematic"/> methods used by the Schematic
/// integration packages (AspNetCore filters, AI middleware, Quartz listeners). Exists primarily so those
/// components can be tested without a live Schematic backend; advanced callers can register their own
/// implementation to add caching, batching, or per-tenant fan-out.
/// Most consumers should not need to interact with this interface directly.
/// </summary>
public interface ISchematicGateClient
{
    Task<CheckFlagWithEntitlementResponse> CheckFlagWithEntitlementAsync(
        string flagKey,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        CancellationToken cancellationToken);

    /// <summary>
    /// Buffered, fire-and-forget event send. <paramref name="options"/> carries SDK send options
    /// (idempotency key, sent-at, backfill).
    /// </summary>
    void Track(
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        int? quantity,
        TrackOptions? options = null);

    /// <summary>
    /// Buffered, fire-and-forget identify. <paramref name="options"/> carries SDK send options
    /// (idempotency key).
    /// </summary>
    void Identify(
        Dictionary<string, string> keys,
        EventBodyIdentifyCompany? company,
        string? name,
        Dictionary<string, object?>? traits,
        IdentifyOptions? options = null);

    /// <summary>
    /// Reserves <paramref name="requestedAmount"/> credits of <paramref name="creditTypeId"/> for
    /// <paramref name="companyId"/> (Schematic's company id, not the company keys). Events sent with
    /// <see cref="TrackAgainstLeaseAsync"/> settle from the hold; <see cref="ReleaseCreditLeaseAsync"/>
    /// returns what was not tracked. Custom implementations that do not support leases can leave the
    /// default, which throws <see cref="NotSupportedException"/>.
    /// </summary>
    Task<SchematicCreditLease> AcquireCreditLeaseAsync(
        string companyId,
        string creditTypeId,
        double requestedAmount,
        DateTime? expiresAt,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{GetType().Name} does not support credit leases.");

    /// <summary>Adds <paramref name="additionalAmount"/> credits to an open lease.</summary>
    Task<SchematicCreditLease> ExtendCreditLeaseAsync(
        string leaseId,
        double additionalAmount,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{GetType().Name} does not support credit leases.");

    /// <summary>Closes a lease, returning its untracked remainder to the company's balance.</summary>
    Task ReleaseCreditLeaseAsync(string leaseId, CancellationToken cancellationToken)
        => throw new NotSupportedException($"{GetType().Name} does not support credit leases.");

    /// <summary>
    /// Sends a Track event redeemed against a lease. Unlike <see cref="Track"/> this is an immediate API
    /// call: the SDK's buffered path cannot carry a lease id.
    /// </summary>
    Task TrackAgainstLeaseAsync(
        string leaseId,
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        long quantity,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{GetType().Name} does not support credit leases.");
}
