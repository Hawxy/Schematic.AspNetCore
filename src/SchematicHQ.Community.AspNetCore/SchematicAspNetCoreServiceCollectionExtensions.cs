using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchematicHQ.Community.AspNetCore.Filters;
using SchematicHQ.Community.AspNetCore.Options;
using SchematicHQ.Community.AspNetCore.Resolvers;
using SchematicHQ.Community.AspNetCore.Snapshots;

namespace SchematicHQ.Community.AspNetCore;

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
    /// Registers <see cref="ISchematicEntitlementSnapshotProvider"/>, which evaluates a set of flags for one
    /// identity in one call, for code that needs several answers at once rather than one gate per request.
    /// Identity resolution and the gate client are shared with the filters, so the snapshot agrees with the gates,
    /// and each check is served from the SDK flag cache when one is configured. Failed checks follow
    /// <see cref="SchematicEntitlementSnapshotOptions.FailurePolicy"/>.
    /// </summary>
    public static IServiceCollection AddSchematicEntitlementSnapshots(
        this IServiceCollection services,
        Action<SchematicEntitlementSnapshotOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSchematicAspNetCore();
        var optionsBuilder = services.AddOptions<SchematicEntitlementSnapshotOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);

        services.TryAddSingleton<ISchematicEntitlementSnapshotProvider, SchematicEntitlementSnapshotProvider>();
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
