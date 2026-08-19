namespace SchematicHQ.Community.DependencyInjection;

public sealed class TraitReportOptions
{
    /// <summary>
    /// Quartz cron expression used by scheduler adapters (e.g. SchematicHQ.Community.Extensions.Quartz) to run the
    /// report automatically. <c>null</c> (the default) means the report is only run when triggered
    /// manually via <see cref="ISchematicTraitReportRunner"/>.
    /// </summary>
    public string? Cron { get; set; }

    /// <summary>
    /// Whether scheduler adapters may act on <see cref="Cron"/>. Defaults to <c>true</c>.
    /// <para>
    /// Set it to <c>false</c> to keep the report registered — still runnable through
    /// <see cref="ISchematicTraitReportRunner"/> — without anything firing it on a schedule. That is what an
    /// integration test host booting the real application needs: leaving the cron in place would have a
    /// suite fan out over every tenant and write to Schematic partway through a test. It also covers
    /// nominating one instance to own reporting when several run the same configuration.
    /// </para>
    /// </summary>
    public bool ScheduleEnabled { get; set; } = true;

    /// <summary>
    /// Maximum number of tenants processed concurrently per run (source call + upsert). Defaults to 4.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;
}
