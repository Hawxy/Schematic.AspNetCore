using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schematic.AspNetCore.Filters;
using Schematic.AspNetCore.Options;
using Schematic.AspNetCore.Resolvers;

namespace Schematic.AspNetCore;

public static class SchematicAspNetCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the AspNetCore-side helpers for Schematic: options, the gate + track endpoint filters,
    /// and the request-pipeline middleware. The Schematic client itself is registered separately via
    /// <c>AddSchematic(apiKey, ...)</c>.
    /// </summary>
    public static IServiceCollection AddSchematicAspNetCore(
        this IServiceCollection services,
        Action<SchematicAspNetCoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<SchematicAspNetCoreOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);

        services.TryAddSingleton<RequireFeatureFilter>();
        services.TryAddSingleton<TrackFeatureFilter>();
        services.TryAddSingleton<SchematicWebhookSignatureFilter>();

        return services;
    }

    /// <summary>
    /// Registers the flag-context resolver used by the gate filter to identify the company/user for each
    /// request. Replaces any previously registered resolver.
    /// </summary>
    public static IServiceCollection AddSchematicFlagContextResolver<TResolver>(this IServiceCollection services)
        where TResolver : class, ISchematicFlagContextResolver
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<ISchematicFlagContextResolver>();
        services.AddScoped<ISchematicFlagContextResolver, TResolver>();
        return services;
    }

    /// <summary>
    /// Registers the identify-context resolver used by <c>UseSchematicIdentify</c> to call
    /// <c>Schematic.Identify</c> per request. Replaces any previously registered resolver.
    /// </summary>
    public static IServiceCollection AddSchematicIdentifyContextResolver<TResolver>(this IServiceCollection services)
        where TResolver : class, ISchematicIdentifyContextResolver
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<ISchematicIdentifyContextResolver>();
        services.AddScoped<ISchematicIdentifyContextResolver, TResolver>();
        return services;
    }
}
