using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SchematicHQ.Client;
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

/// <summary>
/// Resolves the provider and models an inference event reports. The API requires a provider and a response
/// model, so a missing one is sent as <c>unknown</c>, with a warning logged once per middleware.
/// </summary>
internal sealed class AiInferenceModels
{
    private const string Unknown = "unknown";

    private readonly ChatClientMetadata? _metadata;
    private readonly ILogger _logger;
    private int _warnedResponseModel;

    public AiInferenceModels(SchematicInferenceOptions inference, ChatClientMetadata? metadata, ILogger logger)
    {
        _metadata = metadata;
        _logger = logger;
        Provider = inference.Provider ?? metadata?.ProviderName ?? Unknown;

        if (inference.Provider is null && metadata?.ProviderName is null)
        {
            logger.LogWarning(
                "The chat client reports no provider name, so Schematic inference events are sent with provider '{Provider}'. " +
                "Per-model rates and list-price cost do not apply to them; set SchematicInferenceOptions.Provider.",
                Unknown);
        }
    }

    /// <summary><see cref="SchematicInferenceOptions.Provider"/>, else the inner client's provider name.</summary>
    public string Provider { get; }

    /// <summary>The call's <see cref="ChatOptions.ModelId"/>, else the client's default model.</summary>
    public string? RequestModel(ChatOptions? options) => options?.ModelId ?? _metadata?.DefaultModelId;

    /// <summary>The model that served the call, falling back to the requested one.</summary>
    public string ResponseModel(string? responseModelId, string? requestModel)
    {
        if ((responseModelId ?? requestModel) is { } model)
            return model;

        if (Interlocked.Exchange(ref _warnedResponseModel, 1) == 0)
        {
            _logger.LogWarning(
                "A response reported no model id and none was requested, so its Schematic inference event is sent with model '{Model}'. " +
                "Per-model rates and list-price cost do not apply to it; set ChatOptions.ModelId. Logged once.",
                Unknown);
        }

        return Unknown;
    }
}

/// <summary>Builds and sends inference events, shared by the metering middlewares.</summary>
internal static class AiInferenceTracking
{
    /// <summary>Returns <c>null</c> when there is nothing to send.</summary>
    public static EventBodyInference? CreateEvent(
        string eventName,
        SchematicFlagContext context,
        SchematicInferenceUsage? usage,
        AiInferenceModels models,
        string? requestModel,
        string? responseModelId)
    {
        if (usage is null || (usage.InputTokens <= 0 && usage.OutputTokens <= 0))
            return null;

        return new EventBodyInference
        {
            Event = eventName,
            Company = context.Company,
            User = context.User,
            Provider = models.Provider,
            RequestModel = requestModel,
            ResponseModel = models.ResponseModel(responseModelId, requestModel),
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CachedInputTokens = usage.CachedInputTokens,
            CacheCreationInputTokens = usage.CacheCreationInputTokens,
            ReasoningTokens = usage.ReasoningTokens,
            Cost = usage.Cost is { } cost ? decimal.Round(cost, 8).ToString(CultureInfo.InvariantCulture) : null,
        };
    }

    /// <summary>A key per event, so a resend after a lost response is not billed twice.</summary>
    public static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");

    public static void TrackBuffered(ISchematicGateClient schematic, EventBodyInference body)
        => schematic.TrackInference(body, new TrackOptions { IdempotencyKey = NewIdempotencyKey() });
}
