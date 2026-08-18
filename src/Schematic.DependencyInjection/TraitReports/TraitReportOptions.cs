namespace Schematic.DependencyInjection;

public sealed class TraitReportOptions
{
    /// <summary>
    /// Quartz cron expression used by scheduler adapters (e.g. Schematic.Extensions.Quartz) to run the
    /// report automatically. <c>null</c> (the default) means the report is only run when triggered
    /// manually via <see cref="ISchematicTraitReportRunner"/>.
    /// </summary>
    public string? Cron { get; set; }

    /// <summary>
    /// Maximum number of tenants processed concurrently per run (source call + upsert). Defaults to 4.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;
}
