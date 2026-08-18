using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Schematic.DependencyInjection;
using SchematicHQ.Client.RulesEngine;

namespace Schematic.Extensions.AI;

/// <summary>
/// Chat pipeline middleware that checks a Schematic flag/entitlement before invoking the model and
/// throws <see cref="SchematicFeatureDeniedException"/> when access is denied. A failed check applies
/// <see cref="SchematicAiOptions.FailurePolicy"/>.
/// </summary>
public sealed class SchematicGatingChatClient : DelegatingChatClient
{
    private readonly ISchematicGateClient _schematic;
    private readonly string _flagKey;
    private readonly SchematicAiOptions _options;
    private readonly ILogger<SchematicGatingChatClient> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public SchematicGatingChatClient(
        IChatClient innerClient,
        ISchematicGateClient schematic,
        string flagKey,
        SchematicAiOptions options,
        ILogger<SchematicGatingChatClient> logger,
        IHttpContextAccessor? httpContextAccessor = null)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(schematic);
        ArgumentException.ThrowIfNullOrWhiteSpace(flagKey);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _schematic = schematic;
        _flagKey = flagKey;
        _options = options;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureEntitledAsync(cancellationToken);
        return await base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureEntitledAsync(cancellationToken);

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            yield return update;
    }

    private async ValueTask EnsureEntitledAsync(CancellationToken cancellationToken)
    {
        var context = await AiFlagContextResolution.ResolveAsync(_httpContextAccessor, _options)
            ?? throw new SchematicFeatureDeniedException(_flagKey, "no_schematic_context");

        CheckFlagWithEntitlementResponse response;
        try
        {
            response = await _schematic.CheckFlagWithEntitlementAsync(_flagKey, context.Company, context.User, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Schematic entitlement check for flag '{FlagKey}' failed; applying {FailurePolicy}.",
                _flagKey, _options.FailurePolicy);

            if (_options.FailurePolicy == SchematicFailurePolicy.FailOpen)
                return;

            throw new SchematicFeatureDeniedException(_flagKey, "entitlement_check_failed", ex);
        }

        if (!response.Value)
            throw new SchematicFeatureDeniedException(_flagKey, response.Reason);
    }
}
