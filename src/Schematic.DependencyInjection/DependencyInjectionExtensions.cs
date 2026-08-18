using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchematicHQ.Client;
using SchematicHQ.Client.Cache;

namespace Schematic.DependencyInjection;

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
}
