using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.Quartz;

public static class SchematicQuartzServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Schematic Quartz integration: options, the default job-data context resolver, the
    /// gate/track listeners, and a cron job + trigger for every trait report registered with
    /// <see cref="TraitReportOptions.Cron"/> set and <see cref="TraitReportOptions.ScheduleEnabled"/> left
    /// on. Pair with <c>AddQuartz(q =&gt; q.AddSchematic())</c>, which wires the listeners into the
    /// scheduler.
    /// </summary>
    public static IServiceCollection AddSchematicQuartz(
        this IServiceCollection services,
        Action<SchematicQuartzOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Guards the QuartzOptions configuration below against duplicate AddSchematicQuartz calls.
        var alreadyAdded = services.Any(d => d.ServiceType == typeof(SchematicGateTriggerListener));

        var optionsBuilder = services.AddOptions<SchematicQuartzOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);

        services.TryAddSingleton<ISchematicJobContextResolver, JobDataMapContextResolver>();
        services.TryAddSingleton<SchematicGateTriggerListener>();
        services.TryAddSingleton<SchematicTrackJobListener>();

        if (alreadyAdded)
            return services;

        services.AddOptions<QuartzOptions>().Configure<IEnumerable<SchematicTraitReportRegistration>>(
            static (quartzOptions, registrations) =>
            {
                foreach (var registration in registrations)
                {
                    if (string.IsNullOrWhiteSpace(registration.Options.Cron))
                        continue;

                    // Registered but deliberately unscheduled — still runnable via
                    // ISchematicTraitReportRunner. See TraitReportOptions.ScheduleEnabled.
                    if (!registration.Options.ScheduleEnabled)
                        continue;

                    var jobKey = new JobKey($"trait-report-{registration.Name}", "schematic");
                    quartzOptions.AddJob<SchematicTraitReportJob>(job => job
                        .WithIdentity(jobKey)
                        .UsingJobData(SchematicTraitReportJob.ReportNameKey, registration.Name));
                    // FireAndProceed: a run missed while the app was down fires once on startup instead of
                    // being skipped — safe because trait upserts are idempotent.
                    quartzOptions.AddTrigger(trigger => trigger
                        .ForJob(jobKey)
                        .WithIdentity($"trait-report-{registration.Name}", "schematic")
                        .WithCronSchedule(registration.Options.Cron!, cron => cron.WithMisfireHandlingInstructionFireAndProceed()));
                }
            });

        return services;
    }
}

public static class SchematicQuartzConfiguratorExtensions
{
    /// <summary>
    /// Wires the Schematic gate/track listeners into the scheduler. Call inside
    /// <c>AddQuartz(q =&gt; ...)</c>; pair with <c>services.AddSchematicQuartz()</c>, which registers
    /// the listeners and their dependencies.
    /// </summary>
    public static IServiceCollectionQuartzConfigurator AddSchematic(this IServiceCollectionQuartzConfigurator configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);

        configurator.AddTriggerListener(static sp => sp.GetRequiredService<SchematicGateTriggerListener>());
        configurator.AddJobListener(static sp => sp.GetRequiredService<SchematicTrackJobListener>());
        return configurator;
    }
}
