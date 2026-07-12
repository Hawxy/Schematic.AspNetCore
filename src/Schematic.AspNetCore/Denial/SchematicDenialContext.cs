namespace Schematic.AspNetCore.Denial;

/// <summary>
/// The reason and context for a denied entitlement check. Passed to the denial response writer (default
/// ProblemDetails or a user-supplied <c>OnDenied</c> delegate).
/// </summary>
/// <param name="FeatureId">The feature/flag the route required.</param>
/// <param name="RequestedUsage">The requested usage (if any) declared on the route.</param>
/// <param name="RequestedValue">The requested enum value (if any) declared on the route.</param>
/// <param name="Reason">Schematic-style denial reason surfaced to clients (raw SDK string).</param>
public sealed record SchematicDenialContext(
    string FeatureId,
    double? RequestedUsage,
    string? RequestedValue,
    string? Reason);
