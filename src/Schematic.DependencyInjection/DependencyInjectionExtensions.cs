using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchematicHQ.Client;

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

        services.AddSingleton(sp =>
        {
            var clientOptions = sp.GetRequiredService<IOptions<ClientOptions>>();
            return new SchematicHQ.Client.Schematic(apiKey, clientOptions.Value);
        });

        return services;
    }
}
