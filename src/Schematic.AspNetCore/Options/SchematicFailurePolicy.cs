namespace Schematic.AspNetCore.Options;

/// <summary>
/// Controls how the gate filter behaves when the Schematic entitlement check itself fails
/// (network error, SDK exception) — as opposed to a successful check that denies access.
/// </summary>
public enum SchematicFailurePolicy
{
    /// <summary>
    /// Treat a failed check as a blocked request: respond 503 with ProblemDetails. The default.
    /// </summary>
    FailClosed,

    /// <summary>
    /// Treat a failed check as allowed: continue the pipeline. No auto-track event is emitted
    /// because no check result exists.
    /// </summary>
    FailOpen,
}
