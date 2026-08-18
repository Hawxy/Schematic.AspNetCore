using Microsoft.AspNetCore.Http;
using SchematicHQ.Community.AspNetCore.Denial;
using SchematicHQ.Community.AspNetCore.Resolvers;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.AspNetCore.Options;

public sealed class SchematicAspNetCoreOptions
{
    /// <summary>
    /// Simple delegate-based shortcut for resolving the Schematic flag context from an incoming request.
    /// Ignored when an <see cref="ISchematicFlagContextResolver"/> is registered in DI.
    /// </summary>
    public Func<HttpContext, ValueTask<SchematicFlagContext?>>? ResolveContext { get; set; }

    /// <summary>
    /// Custom denial response writer. When set, the filter invokes this delegate instead of the default
    /// ProblemDetails implementation. The delegate is responsible for writing a complete response.
    /// </summary>
    public Func<HttpContext, SchematicDenialContext, Task>? OnDenied { get; set; }

    /// <summary>
    /// How the gate filter responds when the entitlement check throws (network/SDK error).
    /// Defaults to <see cref="SchematicFailurePolicy.FailClosed"/> (503).
    /// </summary>
    public SchematicFailurePolicy FailurePolicy { get; set; } = SchematicFailurePolicy.FailClosed;

    /// <summary>
    /// When set, <c>UseSchematicIdentify</c> sends at most one Identify per identity within this window
    /// instead of one per request. The dedup key covers the user/company keys and names — not traits, so
    /// trait changes within the window are not re-sent. <c>null</c> (the default) identifies on every request.
    /// </summary>
    public TimeSpan? IdentifyDeduplicationWindow { get; set; }

    /// <summary>
    /// Signing secret used by <c>RequireSchematicWebhookSignature</c> to verify inbound Schematic
    /// webhooks. Found in the Schematic dashboard's webhook settings. Required only when webhook
    /// verification is used.
    /// </summary>
    public string? WebhookSecret { get; set; }
}
