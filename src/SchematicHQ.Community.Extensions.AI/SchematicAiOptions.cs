using Microsoft.Extensions.AI;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.AI;

/// <summary>
/// Options for the Schematic chat-client middlewares. Captured per pipeline at
/// <c>UseSchematicUsageTracking</c>/<c>UseSchematicRequireFeature</c> time.
/// </summary>
public sealed class SchematicAiOptions
{
    /// <summary>
    /// Maps a response's <see cref="UsageDetails"/> (plus model id) to the Track events to emit.
    /// Defaults to <see cref="DefaultUsageMapping"/>: one <c>ai.input-tokens</c> and one
    /// <c>ai.output-tokens</c> event with the model id as a trait.
    /// </summary>
    public Func<UsageDetails, string?, IEnumerable<SchematicAiUsageEvent>> MapUsage { get; set; } = DefaultUsageMapping;

    /// <summary>
    /// Identity to attribute usage to when no HTTP request context is available (background jobs,
    /// hosted services). When <c>null</c> and no context can be resolved, tracking is skipped and
    /// gating denies the call.
    /// </summary>
    public SchematicFlagContext? FallbackContext { get; set; }

    /// <summary>
    /// How <c>UseSchematicRequireFeature</c> behaves when the entitlement check throws.
    /// <see cref="SchematicFailurePolicy.FailClosed"/> (the default) denies the call.
    /// </summary>
    public SchematicFailurePolicy FailurePolicy { get; set; } = SchematicFailurePolicy.FailClosed;

    public static IEnumerable<SchematicAiUsageEvent> DefaultUsageMapping(UsageDetails usage, string? modelId)
    {
        var traits = modelId is null ? null : new Dictionary<string, object?> { ["model"] = modelId };

        if (usage.InputTokenCount is { } input && input > 0)
            yield return new SchematicAiUsageEvent("ai.input-tokens", input, traits);

        if (usage.OutputTokenCount is { } output && output > 0)
            yield return new SchematicAiUsageEvent("ai.output-tokens", output, traits);
    }
}
