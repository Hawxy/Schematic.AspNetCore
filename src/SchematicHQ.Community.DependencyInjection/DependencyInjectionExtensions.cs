using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchematicHQ.Client;
using SchematicHQ.Client.Cache;

namespace SchematicHQ.Community.DependencyInjection;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddSchematic(this IServiceCollection services, string apiKey)
        => services.AddSchematic(apiKey, _ => { });

    public static IServiceCollection AddSchematic(
        this IServiceCollection services,
        string apiKey,
        Action<ClientOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.AddOptions<ClientOptions>().Configure(configureOptions);

        // Flushes buffered Track/Identify events via Schematic.Shutdown() when the provider is disposed.
        services.TryAddSingleton<SchematicClientLifetime>();

        // Testability seam used by the integration packages (AspNetCore filters, AI middleware, Quartz
        // listeners); TryAdd so tests or advanced callers can register their own implementation first.
        services.TryAddSingleton<ISchematicGateClient, SchematicGateClient>();

        services.AddSingleton(sp =>
        {
            var clientOptions = sp.GetRequiredService<IOptions<ClientOptions>>();
            var options = clientOptions.Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            options.LoggerFactory = loggerFactory;
            options.CacheProvider ??= sp.GetService<ICacheProvider>();
            var client = new SchematicHQ.Client.Schematic(apiKey, options);
            sp.GetRequiredService<SchematicClientLifetime>().Client = client;
            return client;
        });

        return services;
    }

    /// <summary>
    /// Registers an <see cref="ISchematicGateClient"/> that talks to no backend, for environments with no
    /// API key: tests, local development, CI, ephemeral preview deployments. Call it instead of
    /// <see cref="AddSchematic(IServiceCollection, string)"/> — never as well as, since whichever runs
    /// first wins — so the rest of the wiring (<c>AddSchematicAspNetCore</c>, the AI middlewares, the
    /// Quartz listeners) stays unconditional and those code paths still execute off a key.
    /// <para>
    /// Track and Identify are discarded. Entitlement checks answer <paramref name="allowAll"/> without a
    /// network call, so gated features behave as if every company were entitled. That is what makes a
    /// keyless environment usable, and it is why reaching this in production would open every gate: branch
    /// on whether a key is configured rather than making this the fallback for a key that failed to load.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="allowAll">
    /// What entitlement checks resolve to. <c>true</c> (the default) allows every gated feature; pass
    /// <c>false</c> to deny instead, for tests asserting denial paths.
    /// </param>
    public static IServiceCollection AddSchematicNoOp(this IServiceCollection services, bool allowAll = true)
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd for symmetry with AddSchematic: a client registered earlier by a test or an advanced
        // caller keeps precedence.
        services.TryAddSingleton<ISchematicGateClient>(new NoOpSchematicGateClient(allowAll));

        return services;
    }
}
