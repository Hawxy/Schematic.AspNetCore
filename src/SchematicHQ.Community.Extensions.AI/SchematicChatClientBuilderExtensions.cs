using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.AI;

/// <summary>
/// Schematic middlewares for the <see cref="ChatClientBuilder"/> pipeline. All require
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

    /// <summary>
    /// Gates model calls behind a credit-burndown entitlement and reserves the estimated credits with a
    /// lease before the model is invoked, then tracks the actual usage against the lease and releases
    /// the remainder. Replaces the <c>UseSchematicRequireFeature</c> + <c>UseSchematicUsageTracking</c>
    /// pair for that flag; when the entitlement is not credit-based it behaves like that pair.
    /// </summary>
    public static ChatClientBuilder UseSchematicCreditLease(
        this ChatClientBuilder builder,
        string flagKey,
        Action<SchematicCreditLeaseOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(flagKey);
        return builder.Use((inner, sp) => new SchematicCreditLeaseChatClient(
            inner,
            sp.GetRequiredService<ISchematicGateClient>(),
            flagKey,
            BuildOptions(configure),
            CreateLogger<SchematicCreditLeaseChatClient>(sp),
            sp.GetService<IHttpContextAccessor>()));
    }

    private static TOptions BuildOptions<TOptions>(Action<TOptions>? configure)
        where TOptions : SchematicAiOptions, new()
    {
        var options = new TOptions();
        configure?.Invoke(options);
        return options;
    }

    private static ILogger<T> CreateLogger<T>(IServiceProvider sp)
        => (sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger<T>();
}
