using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.AI;

/// <summary>
/// Schematic middlewares for the <see cref="ChatClientBuilder"/> pipeline. Both require
/// <c>AddSchematicAspNetCore()</c> (for the Schematic gate client); identity comes from the ambient
/// HTTP request's flag-context resolver when <c>AddHttpContextAccessor()</c> is registered, else from
/// <see cref="SchematicAiOptions.FallbackContext"/>.
/// </summary>
public static class SchematicChatClientBuilderExtensions
{
    /// <summary>
    /// Emits Schematic Track events for each response's token usage. Place after
    /// <c>UseSchematicRequireFeature</c> so denied calls are not metered.
    /// </summary>
    public static ChatClientBuilder UseSchematicUsageTracking(
        this ChatClientBuilder builder,
        Action<SchematicAiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use((inner, sp) => new SchematicTrackingChatClient(
            inner,
            sp.GetRequiredService<ISchematicGateClient>(),
            BuildOptions(configure),
            CreateLogger<SchematicTrackingChatClient>(sp),
            sp.GetService<IHttpContextAccessor>()));
    }

    /// <summary>
    /// Gates model calls behind a Schematic flag/entitlement; denied calls throw
    /// <see cref="SchematicFeatureDeniedException"/> before the model is invoked.
    /// </summary>
    public static ChatClientBuilder UseSchematicRequireFeature(
        this ChatClientBuilder builder,
        string flagKey,
        Action<SchematicAiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(flagKey);
        return builder.Use((inner, sp) => new SchematicGatingChatClient(
            inner,
            sp.GetRequiredService<ISchematicGateClient>(),
            flagKey,
            BuildOptions(configure),
            CreateLogger<SchematicGatingChatClient>(sp),
            sp.GetService<IHttpContextAccessor>()));
    }

    private static SchematicAiOptions BuildOptions(Action<SchematicAiOptions>? configure)
    {
        var options = new SchematicAiOptions();
        configure?.Invoke(options);
        return options;
    }

    private static ILogger<T> CreateLogger<T>(IServiceProvider sp)
        => (sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger<T>();
}
