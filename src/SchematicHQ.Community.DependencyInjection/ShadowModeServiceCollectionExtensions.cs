using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SchematicHQ.Community.DependencyInjection;

public static class ShadowModeServiceCollectionExtensions
{
    /// <summary>
    /// Wraps the registered <see cref="ISchematicGateClient"/> in <see cref="ShadowModeSchematicGateClient"/>,
    /// which logs every denied entitlement check as a warning and allows it anyway. Tracking, identify and
    /// credit leases are unaffected. Call it after <c>AddSchematic</c> or <c>AddSchematicNoOp</c> (and after any
    /// custom gate client registration) so there is a client to wrap. The wrapper is also registered under its
    /// own type, so it can be resolved directly.
    /// <para>
    /// Ship a gated build with shadow mode on, read the logs for "would deny", fix the plans that would have
    /// blocked a company that should have access, then turn it off. It is a rollout control, not a failure
    /// policy: a real deny is a successful evaluation, so no fail-open setting covers a plan that is missing a
    /// feature.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enabled">
    /// <c>false</c> leaves the registration untouched, so a configuration value can be passed straight in:
    /// <c>services.AddSchematicShadowMode(config.GetValue&lt;bool&gt;("Schematic:ShadowMode"))</c>.
    /// </param>
    public static IServiceCollection AddSchematicShadowMode(this IServiceCollection services, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!enabled || services.Any(static d => d.ServiceType == typeof(ShadowModeSchematicGateClient)))
            return services;

        var index = -1;
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(ISchematicGateClient) && !services[i].IsKeyedService)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"No {nameof(ISchematicGateClient)} is registered. Call AddSchematic or AddSchematicNoOp before AddSchematicShadowMode.");
        }

        var inner = services[index];
        var createInner = InnerFactory(inner);

        services.Add(ServiceDescriptor.Describe(
            typeof(ShadowModeSchematicGateClient),
            sp => new ShadowModeSchematicGateClient(createInner(sp), sp.GetRequiredService<ILogger<ShadowModeSchematicGateClient>>()),
            inner.Lifetime));
        services[index] = ServiceDescriptor.Describe(
            typeof(ISchematicGateClient),
            static sp => sp.GetRequiredService<ShadowModeSchematicGateClient>(),
            inner.Lifetime);

        return services;
    }

    /// <summary>Resolves the descriptor's shape once, so the wrapper's factory captures only what it needs.</summary>
    private static Func<IServiceProvider, ISchematicGateClient> InnerFactory(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is ISchematicGateClient instance)
            return _ => instance;

        if (descriptor.ImplementationFactory is { } factory)
            return sp => (ISchematicGateClient)factory(sp);

        // A non-keyed descriptor always carries an instance, a factory or a type; this is the remaining case.
        var activate = ActivatorUtilities.CreateFactory(descriptor.ImplementationType!, Type.EmptyTypes);
        return sp => (ISchematicGateClient)activate(sp, null);
    }
}
