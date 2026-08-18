namespace Schematic.DependencyInjection;

/// <summary>
/// Sends one tenant's trait payload to Schematic. The default implementation calls the Companies
/// upsert API directly (awaitable, immediate errors) rather than the buffered fire-and-forget Identify,
/// so report runs can count failures and retry on the next run.
/// </summary>
internal interface ISchematicTraitPusher
{
    Task PushAsync(CompanyTraitReport report, CancellationToken cancellationToken);
}
