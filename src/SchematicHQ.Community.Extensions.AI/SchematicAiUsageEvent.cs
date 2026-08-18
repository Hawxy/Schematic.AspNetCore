namespace SchematicHQ.Community.Extensions.AI;

/// <summary>
/// A Schematic Track event produced from AI usage. Quantities above <see cref="int.MaxValue"/> are
/// clamped when sent (the SDK's quantity is an <see cref="int"/>).
/// </summary>
public sealed record SchematicAiUsageEvent(
    string EventName,
    long Quantity,
    Dictionary<string, object?>? Traits = null);
