namespace SchematicHQ.Community.DependencyInjection;

/// <summary>
/// Passed to <see cref="ISchematicTraitReportSource"/> for each run. Sources that aggregate over a time
/// window derive it from <see cref="RunTimeUtc"/> and their own cadence.
/// </summary>
public sealed record TraitReportContext(string ReportName, DateTimeOffset RunTimeUtc);
