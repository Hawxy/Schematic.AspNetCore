using Quartz;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.Quartz;

/// <summary>
/// Quartz adapter for <see cref="ISchematicTraitReportRunner"/>. Scheduled automatically by
/// <c>AddSchematicQuartz</c> for each trait report registered with a cron expression; can also be
/// scheduled manually with the report name in job data under <see cref="ReportNameKey"/>.
/// </summary>
[DisallowConcurrentExecution]
public sealed class SchematicTraitReportJob : IJob
{
    public const string ReportNameKey = "schematic.trait-report";

    private readonly ISchematicTraitReportRunner _runner;

    public SchematicTraitReportJob(ISchematicTraitReportRunner runner)
    {
        _runner = runner;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var reportName = context.MergedJobDataMap.GetString(ReportNameKey)
            ?? throw new JobExecutionException($"Job data '{ReportNameKey}' is missing.");

        await _runner.RunReportAsync(reportName, context.CancellationToken);
    }
}
