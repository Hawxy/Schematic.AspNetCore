using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Quartz;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.Quartz;

public static class SchematicQuartzServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Schematic Quartz integration: options, the default job-data context resolver and the
    /// gate/track listeners. Pair with <c>AddQuartz(q =&gt; q.AddSchematic())</c>, which wires the
    /// listeners into the scheduler and schedules a job for every trait report registered with
    /// <see cref="TraitReportOptions.Cron"/> set and <see cref="TraitReportOptions.ScheduleEnabled"/> left on.
    /// </summary>
    public static IServiceCollection AddSchematicQuartz(
        this IServiceCollection services,
        Action<SchematicQuartzOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<SchematicQuartzOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);

        services.TryAddSingleton<ISchematicJobContextResolver, JobDataMapContextResolver>();
        services.TryAddSingleton<SchematicGateTriggerListener>();
        services.TryAddSingleton<SchematicTrackJobListener>();

        return services;
    }
}

public static class SchematicQuartzBuilderExtensions
{
    /// <summary>
    /// Wires the Schematic gate/track listeners into the scheduler and schedules the registered trait
    /// reports on their cron expressions. Call inside <c>AddQuartz(q =&gt; ...)</c>; pair with
    /// <c>services.AddSchematicQuartz()</c>, which registers the listeners and their dependencies.
    /// </summary>
    public static IQuartzBuilder AddSchematic(this IQuartzBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddTriggerListener(static sp => sp.GetRequiredService<SchematicGateTriggerListener>(), [Matchers.AllTriggers()]);
        builder.AddJobListener(static sp => sp.GetRequiredService<SchematicTrackJobListener>(), [Matchers.AllJobs()]);
        builder.AddPlugin(
            static sp => new SchematicTraitReportSchedulerPlugin(
                sp.GetServices<SchematicTraitReportRegistration>(),
                sp.GetRequiredService<ILogger<SchematicTraitReportSchedulerPlugin>>()),
            SchematicTraitReportSchedulerPlugin.PluginName);
        return builder;
    }
}
