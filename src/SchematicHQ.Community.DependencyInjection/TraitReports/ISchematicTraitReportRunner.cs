namespace SchematicHQ.Community.DependencyInjection;

/// <summary>
/// Runs a registered trait report by name. Scheduler adapters call this on their cadence; it can also be
/// invoked directly (e.g. from an admin endpoint) to re-run a report on demand.
/// </summary>
public interface ISchematicTraitReportRunner
{
    /// <exception cref="InvalidOperationException">No report with <paramref name="reportName"/> is registered.</exception>
    Task<TraitReportResult> RunReportAsync(string reportName, CancellationToken cancellationToken = default);
}
