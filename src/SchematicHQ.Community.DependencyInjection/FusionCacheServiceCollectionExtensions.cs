using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchematicHQ.Client.Cache;
using ZiggyCreatures.Caching.Fusion;

namespace SchematicHQ.Community.DependencyInjection;

public static class FusionCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers a FusionCache-backed <see cref="ICacheProvider"/> that <c>AddSchematic</c> wires into
    /// the Schematic client. Uses the default <see cref="IFusionCache"/>, so <c>AddFusionCache()</c>
    /// must also be registered. Entries live for <paramref name="defaultTtl"/>, defaulting to the SDK's
    /// built-in cache TTL.
    /// </summary>
    public static IServiceCollection AddSchematicFusionCache(this IServiceCollection services, TimeSpan? defaultTtl = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICacheProvider>(sp =>
            new FusionCacheProvider(sp.GetRequiredService<IFusionCache>(), defaultTtl));
        return services;
    }

    /// <summary>
    /// Registers a FusionCache-backed <see cref="ICacheProvider"/> using the named cache, for apps that
    /// register multiple FusionCache instances via <c>AddFusionCache(name)</c>. Entries live for
    /// <paramref name="defaultTtl"/>, defaulting to the SDK's built-in cache TTL.
    /// </summary>
    public static IServiceCollection AddSchematicFusionCache(
        this IServiceCollection services,
        string cacheName,
        TimeSpan? defaultTtl = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheName);
        services.TryAddSingleton<ICacheProvider>(sp =>
            new FusionCacheProvider(sp.GetRequiredService<IFusionCacheProvider>().GetCache(cacheName), defaultTtl));
        return services;
    }
}
