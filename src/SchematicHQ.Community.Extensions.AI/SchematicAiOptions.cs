using System.Text;
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
    /// Defaults to <see cref="DefaultUsageMapping"/>: <c>ai.input-tokens</c>, <c>ai.output-tokens</c>, and
    /// one event per entry in <see cref="UsageDetails.AdditionalCounts"/>, each with the model id as a trait.
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

    /// <summary>
    /// Emits <c>ai.input-tokens</c> and <c>ai.output-tokens</c>, plus one event per entry in
    /// <see cref="UsageDetails.AdditionalCounts"/> named <c>ai.{key}</c> with the key normalised to
    /// kebab-case — so a Bedrock response reporting <c>cache_read_input_tokens</c> also emits
    /// <c>ai.cache-read-input-tokens</c>. Zero and absent counts emit nothing.
    /// <para>
    /// Those extra counts are passed through as the provider reported them, with no attempt to price or
    /// reconcile them: create a Schematic feature only for the ones you want to meter, and read the
    /// provider's own docs before adding them to the input count. Whether they are already part of
    /// <see cref="UsageDetails.InputTokenCount"/> differs by provider — Anthropic reports cache reads and
    /// writes as buckets alongside the input count, while OpenAI counts cached tokens within it — so
    /// summing blindly double-counts on some providers and not others.
    /// </para>
    /// </summary>
    public static IEnumerable<SchematicAiUsageEvent> DefaultUsageMapping(UsageDetails usage, string? modelId)
    {
        var traits = modelId is null ? null : new Dictionary<string, object?> { ["model"] = modelId };

        if (usage.InputTokenCount is { } input && input > 0)
            yield return new SchematicAiUsageEvent("ai.input-tokens", input, traits);

        if (usage.OutputTokenCount is { } output && output > 0)
            yield return new SchematicAiUsageEvent("ai.output-tokens", output, traits);

        if (usage.AdditionalCounts is null)
            yield break;

        foreach (var count in usage.AdditionalCounts)
        {
            if (count.Value <= 0)
                continue;

            var name = ToEventNameSuffix(count.Key);
            if (name.Length == 0)
                continue;

            yield return new SchematicAiUsageEvent($"ai.{name}", count.Value, traits);
        }
    }

    /// <summary>
    /// Normalises a provider's count key to the kebab-case the built-in event names use, so
    /// <c>cache_read_input_tokens</c> and <c>CacheReadInputTokens</c> both land on
    /// <c>cache-read-input-tokens</c> and an event name does not leak whichever casing the provider
    /// happened to pick.
    /// </summary>
    private static string ToEventNameSuffix(string key)
    {
        var builder = new StringBuilder(key.Length + 4);
        var previous = '\0';

        foreach (var character in key)
        {
            if (character is '_' or '-' or ' ' or '.')
            {
                AppendSeparator(builder);
            }
            else
            {
                // Break on the end of a lower-case run only, so an acronym such as "TTFTMs" does not
                // come out as "t-t-f-t-ms".
                if (char.IsUpper(character) && (char.IsLower(previous) || char.IsDigit(previous)))
                    AppendSeparator(builder);

                builder.Append(char.ToLowerInvariant(character));
            }

            previous = character;
        }

        return builder.ToString().Trim('-');

        static void AppendSeparator(StringBuilder builder)
        {
            if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                builder.Append('-');
        }
    }
}
