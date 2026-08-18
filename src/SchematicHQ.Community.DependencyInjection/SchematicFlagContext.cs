namespace SchematicHQ.Community.DependencyInjection;

/// <summary>
/// The keys identifying a company and user for a flag/entitlement check. Each dictionary maps a key name
/// (e.g. <c>id</c>, <c>email</c>) to its value. Either dictionary may be empty when the dimension is not
/// known, but at least one identifying pair is required for Schematic to evaluate.
/// </summary>
public sealed record SchematicFlagContext(
    Dictionary<string, string> Company,
    Dictionary<string, string> User);
