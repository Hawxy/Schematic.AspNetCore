using Microsoft.Extensions.AI;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.AI;

/// <summary>Collects the usage and model id a streamed response reports across its updates.</summary>
internal sealed class AiUsageAccumulator
{
    public UsageDetails? Usage { get; private set; }

    public string? ModelId { get; private set; }

    public void Add(ChatResponseUpdate update)
    {
        ModelId ??= update.ModelId;
        foreach (var content in update.Contents)
        {
            if (content is UsageContent usageContent)
                (Usage ??= new UsageDetails()).Add(usageContent.Details);
        }
    }
}

/// <summary>Turns usage into Track events and sends them, shared by the metering middlewares.</summary>
internal static class AiUsageTracking
{
    /// <summary>Maps usage through <see cref="SchematicAiOptions.MapUsage"/>, dropping non-positive quantities.</summary>
    public static IReadOnlyList<SchematicAiUsageEvent> MapEvents(SchematicAiOptions options, UsageDetails usage, string? modelId)
        => options.MapUsage(usage, modelId).Where(static e => e.Quantity > 0).ToArray();

    /// <summary>Buffered send, clamped to the SDK's <see cref="int"/> quantity.</summary>
    public static void TrackBuffered(ISchematicGateClient schematic, SchematicFlagContext context, SchematicAiUsageEvent usageEvent)
    {
        var quantity = (int)Math.Min(usageEvent.Quantity, int.MaxValue);
        schematic.Track(usageEvent.EventName, context.Company, context.User, usageEvent.Traits ?? new(), quantity);
    }
}
