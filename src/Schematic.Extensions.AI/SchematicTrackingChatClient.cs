using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Schematic.AspNetCore.Internal;

namespace Schematic.Extensions.AI;

/// <summary>
/// Chat pipeline middleware that emits Schematic Track events for each response's token usage, mapped
/// through <see cref="SchematicAiOptions.MapUsage"/>. For streaming responses, usage reported across
/// updates is aggregated and tracked when enumeration ends — including when the consumer abandons the
/// stream. A tracking failure is logged and never fails the AI call.
/// </summary>
public sealed class SchematicTrackingChatClient : DelegatingChatClient
{
    private readonly ISchematicGateClient _schematic;
    private readonly SchematicAiOptions _options;
    private readonly ILogger<SchematicTrackingChatClient> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public SchematicTrackingChatClient(
        IChatClient innerClient,
        ISchematicGateClient schematic,
        SchematicAiOptions options,
        ILogger<SchematicTrackingChatClient> logger,
        IHttpContextAccessor? httpContextAccessor = null)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(schematic);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _schematic = schematic;
        _options = options;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        await TrackUsageAsync(response.Usage, response.ModelId);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        UsageDetails? usage = null;
        string? modelId = null;

        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                modelId ??= update.ModelId;
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                        (usage ??= new UsageDetails()).Add(usageContent.Details);
                }

                yield return update;
            }
        }
        finally
        {
            await TrackUsageAsync(usage, modelId);
        }
    }

    private async ValueTask TrackUsageAsync(UsageDetails? usage, string? modelId)
    {
        if (usage is null)
            return;

        try
        {
            var context = await AiFlagContextResolution.ResolveAsync(_httpContextAccessor, _options);
            if (context is null)
            {
                _logger.LogDebug("No Schematic identity resolved; AI usage not tracked.");
                return;
            }

            foreach (var usageEvent in _options.MapUsage(usage, modelId))
            {
                if (usageEvent.Quantity <= 0)
                    continue;

                var quantity = (int)Math.Min(usageEvent.Quantity, int.MaxValue);
                _schematic.Track(usageEvent.EventName, context.Company, context.User, usageEvent.Traits ?? new(), quantity);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schematic AI usage tracking failed; the response is unaffected.");
        }
    }
}
