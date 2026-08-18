namespace Schematic.DependencyInjection;

/// <summary>
/// DI registration record for one named trait report. Consumed by scheduler adapters to wire recurring
/// runs; created via <c>AddSchematicTraitReport</c>.
/// </summary>
public sealed record SchematicTraitReportRegistration(string Name, Type CatalogType, Type SourceType, TraitReportOptions Options);
