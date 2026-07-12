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

    public Task<CheckFlagWithEntitlementResponse> CheckFlagWithEntitlementAsync(
        string flagKey,
        Dictionary<string, string> company,
        Dictionary<string, string> user)
        => _client.CheckFlagWithEntitlement(flagKey, company, user);

    public void Track(
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        int? quantity)
        => _client.Track(eventName, company, user, traits, quantity);
}
