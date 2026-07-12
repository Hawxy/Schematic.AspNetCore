using SchematicHQ.Client;
using SchematicHQ.Client.RulesEngine;

namespace Schematic.AspNetCore.Internal;

/// <summary>
/// Seam over the subset of <see cref="SchematicHQ.Client.Schematic"/> methods used by the AspNetCore
/// filters. Exists primarily so the filters can be tested without a live Schematic backend; advanced
/// callers can register their own implementation to add caching, batching, or per-tenant fan-out.
/// Most consumers should not need to interact with this interface directly.
/// </summary>
public interface ISchematicGateClient
{
    Task<CheckFlagWithEntitlementResponse> CheckFlagWithEntitlementAsync(
        string flagKey,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        CancellationToken cancellationToken);

    void Track(
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        int? quantity);

    void Identify(
        Dictionary<string, string> keys,
        EventBodyIdentifyCompany? company,
        string? name,
        Dictionary<string, object?>? traits);
}
