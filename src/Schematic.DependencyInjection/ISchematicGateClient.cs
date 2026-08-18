using SchematicHQ.Client;
using SchematicHQ.Client.RulesEngine;

namespace Schematic.DependencyInjection;

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
}
