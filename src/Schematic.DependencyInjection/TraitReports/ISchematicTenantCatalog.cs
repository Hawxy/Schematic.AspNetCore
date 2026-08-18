namespace Schematic.DependencyInjection;

/// <summary>
/// Enumerates the tenant ids a trait report should cover, typically from a tenant/company table.
/// Each id is passed to the report's <see cref="ISchematicTraitReportSource"/>.
/// </summary>
public interface ISchematicTenantCatalog
{
    IAsyncEnumerable<string> GetTenantIdsAsync(TraitReportContext context, CancellationToken cancellationToken);
}
