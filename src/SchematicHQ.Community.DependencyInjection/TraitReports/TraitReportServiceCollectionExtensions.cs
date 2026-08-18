using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SchematicHQ.Community.DependencyInjection;

public static class TraitReportServiceCollectionExtensions
{
    /// <summary>
    /// Registers a named trait report: <typeparamref name="TCatalog"/> enumerates the tenant ids each
    /// run and <typeparamref name="TSource"/> produces each tenant's traits. Requires the Schematic
    /// client (<c>AddSchematic</c>). Set <see cref="TraitReportOptions.Cron"/> to have a scheduler
    /// adapter (e.g. SchematicHQ.Community.Extensions.Quartz) run it automatically; otherwise trigger it via
    /// <see cref="ISchematicTraitReportRunner"/>.
    /// </summary>
    public static IServiceCollection AddSchematicTraitReport<TCatalog, TSource>(
        this IServiceCollection services,
        string name,
        Action<TraitReportOptions>? configure = null)
        where TCatalog : class, ISchematicTenantCatalog
        where TSource : class, ISchematicTraitReportSource
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var duplicate = services.Any(d =>
            d.ImplementationInstance is SchematicTraitReportRegistration existing && existing.Name == name);
        if (duplicate)
            throw new InvalidOperationException($"A Schematic trait report named '{name}' is already registered.");

        var options = new TraitReportOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<ISchematicTraitReportRunner, SchematicTraitReportRunner>();
        services.TryAddSingleton<ISchematicTraitPusher, SchematicTraitPusher>();
        services.TryAddScoped<TCatalog>();
        services.TryAddScoped<TSource>();
        services.AddSingleton(new SchematicTraitReportRegistration(name, typeof(TCatalog), typeof(TSource), options));

        return services;
    }
}
