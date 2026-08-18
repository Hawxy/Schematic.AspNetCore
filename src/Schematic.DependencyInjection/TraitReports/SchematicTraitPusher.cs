using SchematicHQ.Client;

namespace Schematic.DependencyInjection;

internal sealed class SchematicTraitPusher : ISchematicTraitPusher
{
    private readonly SchematicHQ.Client.Schematic _client;

    public SchematicTraitPusher(SchematicHQ.Client.Schematic client)
    {
        _client = client;
    }

    public async Task PushAsync(CompanyTraitReport report, CancellationToken cancellationToken)
    {
        await _client.Companies.UpsertCompanyAsync(
            new UpsertCompanyRequestBody
            {
                Keys = report.Keys,
                Traits = report.Traits,
                Name = report.Name,
            },
            cancellationToken: cancellationToken);
    }
}
