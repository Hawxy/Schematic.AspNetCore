namespace SchematicHQ.Community.DependencyInjection;

/// <summary>
/// Controls how a gating integration behaves when the Schematic entitlement check itself fails
/// (network error, SDK exception) — as opposed to a successful check that denies access.
/// </summary>
public enum SchematicFailurePolicy
{
    /// <summary>
    /// Treat a failed check as a blocked request (e.g. the AspNetCore gate responds 503, a Quartz
    /// job execution is vetoed). The default.
    /// </summary>
    FailClosed,

    /// <summary>
    /// Treat a failed check as allowed: continue as if the check passed. No auto-track event is
    /// emitted because no check result exists.
    /// </summary>
    FailOpen,
}
