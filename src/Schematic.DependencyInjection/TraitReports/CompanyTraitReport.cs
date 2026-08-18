namespace Schematic.DependencyInjection;

/// <summary>
/// One company's payload within a trait report: the keys identifying the company (e.g. <c>id</c>) and the
/// trait values to upsert. Traits are last-write-wins, so re-sending a report is safe.
/// </summary>
public sealed record CompanyTraitReport(
    Dictionary<string, string> Keys,
    Dictionary<string, object?> Traits,
    string? Name = null);
