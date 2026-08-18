namespace Schematic.DependencyInjection;

/// <summary>
/// Produces one tenant's trait payload for a report. Called once per tenant id from the report's
/// <see cref="ISchematicTenantCatalog"/> — concurrently, up to
/// <see cref="TraitReportOptions.MaxDegreeOfParallelism"/> — so resolve per-tenant resources inside the
/// call (e.g. via an <c>IDbContextFactory</c> or your own scope) rather than holding shared mutable state.
/// </summary>
public interface ISchematicTraitReportSource
{
    /// <summary>Return <c>null</c> to skip the tenant for this run.</summary>
    Task<CompanyTraitReport?> GetReportAsync(string tenantId, TraitReportContext context, CancellationToken cancellationToken);
}
