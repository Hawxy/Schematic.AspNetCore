namespace SchematicHQ.Community.AspNetCore.Denial;

/// <summary>
/// The reason and context for a denied entitlement check. Passed to the denial response writer (default
/// ProblemDetails or a user-supplied <c>OnDenied</c> delegate).
/// </summary>
/// <param name="FeatureId">The feature/flag the route required.</param>
/// <param name="Reason">Schematic-style denial reason surfaced to clients (raw SDK string).</param>
public sealed record SchematicDenialContext(
    string FeatureId,
    string? Reason);
