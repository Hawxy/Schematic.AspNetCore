namespace Schematic.DependencyInjection;

/// <summary>Outcome of one trait report run. Failed companies are logged individually.</summary>
public sealed record TraitReportResult(int Succeeded, int Failed);
