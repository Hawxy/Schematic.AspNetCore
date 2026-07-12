using SchematicHQ.Client.RulesEngine;
using SchematicClient = SchematicHQ.Client.Schematic;

namespace Schematic.AspNetCore.Internal;

internal sealed class SchematicGateClient : ISchematicGateClient
{
    private readonly SchematicClient _client;

    public SchematicGateClient(SchematicClient client)
    {
        _client = client;
    }

    // The SDK method does not accept a CancellationToken; WaitAsync at least stops awaiting
    // (e.g. when the request is aborted) even though the underlying call runs to completion.
    public Task<CheckFlagWithEntitlementResponse> CheckFlagWithEntitlementAsync(
        string flagKey,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        CancellationToken cancellationToken)
        => _client.CheckFlagWithEntitlement(flagKey, company, user).WaitAsync(cancellationToken);

    public void Track(
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        int? quantity)
        => _client.Track(eventName, company, user, traits, quantity);

    public void Identify(
        Dictionary<string, string> keys,
        SchematicHQ.Client.EventBodyIdentifyCompany? company,
        string? name,
        Dictionary<string, object?>? traits)
        => _client.Identify(keys, company, name, traits);
}
