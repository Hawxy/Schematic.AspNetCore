using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchematicHQ.Community.AspNetCore.Internal;
using SchematicHQ.Community.AspNetCore.Options;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.AspNetCore.Snapshots;

/// <summary>
/// Checks each flag through the gate client. Each check is served from the SDK's flag cache when one is
/// configured, so the snapshot has no cache of its own.
/// </summary>
internal sealed class SchematicEntitlementSnapshotProvider : ISchematicEntitlementSnapshotProvider
{
    private readonly ISchematicGateClient _client;
    private readonly IOptions<SchematicAspNetCoreOptions> _aspNetCoreOptions;
    private readonly SchematicEntitlementSnapshotOptions _options;
    private readonly ILogger<SchematicEntitlementSnapshotProvider> _logger;

    public SchematicEntitlementSnapshotProvider(
        ISchematicGateClient client,
        IOptions<SchematicAspNetCoreOptions> aspNetCoreOptions,
        IOptions<SchematicEntitlementSnapshotOptions> options,
        ILogger<SchematicEntitlementSnapshotProvider> logger)
    {
        _client = client;
        _aspNetCoreOptions = aspNetCoreOptions;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SchematicEntitlementSnapshot?> GetAsync(
        HttpContext http,
        IReadOnlyCollection<string> flagKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);

        var context = await FlagContextResolution.ResolveAsync(http, _aspNetCoreOptions.Value);
        return context is null ? null : await GetAsync(context, flagKeys, cancellationToken);
    }

    public async Task<SchematicEntitlementSnapshot> GetAsync(
        SchematicFlagContext context,
        IReadOnlyCollection<string> flagKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(flagKeys);

        var policy = _options.FailurePolicy ?? _aspNetCoreOptions.Value.FailurePolicy;
        var checks = await Task.WhenAll(
            flagKeys.Distinct(StringComparer.Ordinal).Select(key => CheckAsync(key, context, policy, cancellationToken)));
        return new SchematicEntitlementSnapshot(checks.ToDictionary(static e => e.FlagKey, StringComparer.Ordinal));
    }

    private async Task<SchematicEntitlement> CheckAsync(
        string flagKey,
        SchematicFlagContext context,
        SchematicFailurePolicy policy,
        CancellationToken cancellationToken)
    {
        var outcome = await _client.TryCheckFlagAsync(flagKey, context, policy, _logger, cancellationToken);
        return outcome.Response is { } response
            ? new SchematicEntitlement(flagKey, response.Value, response.Reason, response.Entitlement, CheckFailed: false)
            : new SchematicEntitlement(
                flagKey,
                Value: policy == SchematicFailurePolicy.FailOpen,
                Reason: outcome.Failure!.Message,
                Entitlement: null,
                CheckFailed: true);
    }
}
