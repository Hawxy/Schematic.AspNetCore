using Microsoft.Extensions.AI;
using SchematicHQ.Client;

namespace SchematicHQ.Community.Extensions.AI;

/// <summary>
/// Sends each model call as one Schematic inference event instead of Track events. Set on
/// <see cref="SchematicAiOptions.Inference"/>. Schematic then burns credits at the per-token rates on the
/// feature's plan entitlement, so the token counts go to Schematic unweighted.
/// </summary>
public sealed class SchematicInferenceOptions
{
    private static readonly string[] CacheReadKeys = ["cache-read-input-tokens"];
    private static readonly string[] CacheCreationKeys = ["cache-creation-input-tokens", "cache-write-input-tokens"];

    /// <summary>
    /// The inference feature's event name. Required by <c>UseSchematicUsageTracking</c>. <c>UseSchematicCreditLease</c>
    /// defaults it to the gated entitlement's event subtype.
    /// </summary>
    public string? EventName { get; set; }

    /// <summary>
    /// The provider sent with each event, e.g. <c>anthropic</c> or <c>openai</c>. Defaults to the inner client's
    /// <see cref="ChatClientMetadata.ProviderName"/>. Schematic looks up list prices by provider and model, so set
    /// this when the client's name differs from the model vendor's, as with <c>aws.bedrock</c> serving Anthropic models.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Maps a response's <see cref="UsageDetails"/> (plus the model id) to the counts to send. Return <c>null</c>
    /// to send nothing. Defaults to <see cref="DefaultUsageMapping"/>; use <see cref="BucketedCacheUsageMapping"/>
    /// for adapters that report cache tokens beside the input count rather than inside it.
    /// </summary>
    public Func<UsageDetails, string?, SchematicInferenceUsage?> MapUsage { get; set; } = DefaultUsageMapping;

    /// <summary>
    /// The credits a call costs, used by <c>UseSchematicCreditLease</c> to size and top up its hold. Defaults to
    /// <see cref="DefaultCreditCost"/>. Schematic computes the actual burn itself, including any per-model rate
    /// overrides this cannot see.
    /// </summary>
    public Func<SchematicInferenceUsage, RulesengineFeatureEntitlement, double> CreditCost { get; set; } = DefaultCreditCost;

    /// <summary>
    /// Follows the Microsoft.Extensions.AI convention: <see cref="UsageDetails.InputTokenCount"/> includes
    /// <see cref="UsageDetails.CachedInputTokenCount"/>, and <see cref="UsageDetails.OutputTokenCount"/> includes
    /// <see cref="UsageDetails.ReasoningTokenCount"/>. Schematic counts tokens the same way. Returns <c>null</c>
    /// when the response reported no tokens.
    /// </summary>
    public static SchematicInferenceUsage? DefaultUsageMapping(UsageDetails usage, string? modelId)
    {
        var input = usage.InputTokenCount ?? 0;
        var output = usage.OutputTokenCount ?? 0;
        if (input <= 0 && output <= 0)
            return null;

        return new SchematicInferenceUsage(input, output)
        {
            CachedInputTokens = usage.CachedInputTokenCount,
            ReasoningTokens = usage.ReasoningTokenCount,
        };
    }

    /// <summary>
    /// For adapters that pass through the Anthropic Messages API counts, where
    /// <see cref="UsageDetails.InputTokenCount"/> excludes cache reads and writes and those are reported in
    /// <see cref="UsageDetails.CachedInputTokenCount"/> or <see cref="UsageDetails.AdditionalCounts"/>
    /// (<c>cache_read_input_tokens</c>, <c>cache_creation_input_tokens</c> or <c>cache_write_input_tokens</c>,
    /// in any casing). Adds both to the input count, since Schematic expects it to include them.
    /// </summary>
    public static SchematicInferenceUsage? BucketedCacheUsageMapping(UsageDetails usage, string? modelId)
    {
        var cached = usage.CachedInputTokenCount ?? FindCount(usage, CacheReadKeys);
        var creation = FindCount(usage, CacheCreationKeys);
        var input = (usage.InputTokenCount ?? 0) + (cached ?? 0) + (creation ?? 0);
        var output = usage.OutputTokenCount ?? 0;
        if (input <= 0 && output <= 0)
            return null;

        return new SchematicInferenceUsage(input, output)
        {
            CachedInputTokens = cached,
            CacheCreationInputTokens = creation,
            ReasoningTokens = usage.ReasoningTokenCount,
        };
    }

    /// <summary>
    /// The entitlement's flat <see cref="RulesengineFeatureEntitlement.ConsumptionRate"/> per call, plus each
    /// token count times its rate in <see cref="RulesengineFeatureEntitlement.QuantityRates"/>, with uncached
    /// input priced at <c>input_tokens</c>.
    /// </summary>
    public static double DefaultCreditCost(SchematicInferenceUsage usage, RulesengineFeatureEntitlement entitlement)
    {
        var rates = entitlement.QuantityRates;
        double Rate(string key) => rates is not null && rates.TryGetValue(key, out var rate) ? rate : 0;

        var cached = usage.CachedInputTokens ?? 0;
        var creation = usage.CacheCreationInputTokens ?? 0;
        var uncached = Math.Max(0, usage.InputTokens - cached - creation);

        return (entitlement.ConsumptionRate ?? 0)
            + uncached * Rate("input_tokens")
            + cached * Rate("cached_input_tokens")
            + creation * Rate("cache_creation_input_tokens")
            + usage.OutputTokens * Rate("output_tokens")
            + (usage.ReasoningTokens ?? 0) * Rate("reasoning_tokens");
    }

    private static long? FindCount(UsageDetails usage, string[] keys)
    {
        if (usage.AdditionalCounts is null)
            return null;

        foreach (var count in usage.AdditionalCounts)
        {
            if (Array.IndexOf(keys, SchematicAiOptions.ToEventNameSuffix(count.Key)) >= 0)
                return count.Value;
        }

        return null;
    }
}
