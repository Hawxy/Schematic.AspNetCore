using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Extensibility;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.Quartz;

/// <summary>
/// Schedules a <see cref="SchematicTraitReportJob"/> for every trait report registered with a cron
/// expression and <see cref="TraitReportOptions.ScheduleEnabled"/> left on. Runs as a scheduler plugin so
/// the registrations are read from the container when the scheduler starts, not when it is configured.
/// </summary>
internal sealed class SchematicTraitReportSchedulerPlugin : ISchedulerPlugin
{
    public const string PluginName = "schematic-trait-reports";
    private const string Group = "schematic";

    private readonly IEnumerable<SchematicTraitReportRegistration> _registrations;
    private readonly ILogger<SchematicTraitReportSchedulerPlugin> _logger;
    private IScheduler? _scheduler;

    public SchematicTraitReportSchedulerPlugin(
        IEnumerable<SchematicTraitReportRegistration> registrations,
        ILogger<SchematicTraitReportSchedulerPlugin> logger)
    {
        _registrations = registrations;
        _logger = logger;
    }

    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        _scheduler = scheduler;
        return ValueTask.CompletedTask;
    }

    public async ValueTask Start(CancellationToken cancellationToken = default)
    {
        var scheduler = _scheduler ?? throw new InvalidOperationException("Initialize must run before Start.");

        foreach (var registration in _registrations)
        {
            if (string.IsNullOrWhiteSpace(registration.Options.Cron))
                continue;

            // Registered but deliberately unscheduled — still runnable via ISchematicTraitReportRunner.
            if (!registration.Options.ScheduleEnabled)
                continue;

            var (job, trigger) = Build(registration.Name, registration.Options.Cron);
            // Replacing: a cron changed between deployments takes effect against a persistent store.
            await scheduler.ScheduleJob(job, trigger, ScheduleJobOptions.Replacing, cancellationToken);
            _logger.LogDebug(
                "Scheduled Schematic trait report '{ReportName}' on cron '{Cron}'.",
                registration.Name, registration.Options.Cron);
        }
    }

    internal static (IJobDetail Job, ITrigger Trigger) Build(string reportName, string cron)
    {
        var jobKey = new JobKey($"trait-report-{reportName}", Group);
        var job = JobBuilder.Create<SchematicTraitReportJob>()
            .WithIdentity(jobKey)
            .UsingJobData(SchematicTraitReportJob.ReportNameKey, reportName)
            .Build();

        // FireAndProceed: a run missed while the app was down fires once on startup instead of being
        // skipped — safe because trait upserts are idempotent.
        var trigger = TriggerBuilder.Create<SchematicTraitReportJob>()
            .WithIdentity($"trait-report-{reportName}", Group)
            .ForJob(jobKey)
            .WithSchedule(CronScheduleBuilder.Create(cron).WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed))
            .Build();

        return (job, trigger);
    }
}
