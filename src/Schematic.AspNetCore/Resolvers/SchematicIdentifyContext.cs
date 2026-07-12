using SchematicHQ.Client;

namespace Schematic.AspNetCore.Resolvers;

/// <summary>
/// Result of identifying the caller for a request, used by <c>UseSchematicIdentify</c> to call
/// <c>Schematic.Identify</c> per request.
/// </summary>
public sealed record SchematicIdentifyContext(
    Dictionary<string, string> Keys,
    EventBodyIdentifyCompany? Company = null,
    string? Name = null,
    Dictionary<string, object?>? Traits = null);
