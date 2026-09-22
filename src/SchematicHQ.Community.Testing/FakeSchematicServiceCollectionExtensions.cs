using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Testing;

public static class FakeSchematicServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the registered <see cref="ISchematicGateClient"/> with <paramref name="fake"/>, so the filters,
    /// AI middlewares and Quartz listeners of a real application host answer from the test's denials instead
    /// of Schematic. Call it from the test host's service configuration after the application's own
    /// registrations have run.
    /// </summary>
    public static IServiceCollection AddSchematicFake(this IServiceCollection services, FakeSchematicGateClient fake)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(fake);

        services.RemoveAll<ISchematicGateClient>();
        services.RemoveAll<FakeSchematicGateClient>();
        services.AddSingleton<ISchematicGateClient>(fake);
        services.AddSingleton(fake);
        return services;
    }
}
