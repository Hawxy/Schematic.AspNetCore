using Microsoft.Extensions.AI;
using SchematicHQ.Client;

namespace SchematicHQ.Community.Extensions.AI;

/// <summary>
/// Options for <c>UseSchematicCreditLease</c>. Adds to <see cref="SchematicAiOptions"/> the pieces that
/// size the credit hold taken before the model is invoked.
/// </summary>
public sealed class SchematicCreditLeaseOptions : SchematicAiOptions
{
    private const int DefaultOutputTokens = 1024;

    /// <summary>
    /// Estimates the usage a call will consume, before the model is invoked, so the hold can be sized.
    /// Defaults to <see cref="DefaultUsageEstimate"/>: roughly four characters per input token, and
    /// <see cref="ChatOptions.MaxOutputTokens"/> (or 1024 when unset) output tokens.
    /// </summary>
    public Func<IEnumerable<ChatMessage>, ChatOptions?, UsageDetails> EstimateUsage { get; set; } = DefaultUsageEstimate;

    /// <summary>
    /// Converts the events <see cref="SchematicAiOptions.MapUsage"/> produced into a credit amount for the
    /// gated entitlement. Defaults to the sum of every event's quantity times the entitlement's
    /// <see cref="RulesengineFeatureEntitlement.ConsumptionRate"/>. Override when input and output tokens
    /// burn at different rates, keying off <see cref="SchematicAiUsageEvent.EventName"/>.
    /// </summary>
    public Func<IReadOnlyList<SchematicAiUsageEvent>, RulesengineFeatureEntitlement, double> CreditCost { get; set; }
        = DefaultCreditCost;

    /// <summary>
    /// How long a hold stays open before Schematic releases it on its own. Should comfortably exceed the
    /// longest expected model call, since the hold is released explicitly once usage is tracked.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(10);

    public static UsageDetails DefaultUsageEstimate(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        long characters = 0;
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is TextContent text)
                    characters += text.Text.Length;
            }
        }

        return new UsageDetails
        {
            InputTokenCount = (characters + 3) / 4,
            OutputTokenCount = options?.MaxOutputTokens ?? DefaultOutputTokens,
        };
    }

    public static double DefaultCreditCost(IReadOnlyList<SchematicAiUsageEvent> events, RulesengineFeatureEntitlement entitlement)
    {
        var rate = entitlement.ConsumptionRate ?? 0;
        double quantity = 0;
        foreach (var usageEvent in events)
            quantity += usageEvent.Quantity;

        return quantity * rate;
    }
}
