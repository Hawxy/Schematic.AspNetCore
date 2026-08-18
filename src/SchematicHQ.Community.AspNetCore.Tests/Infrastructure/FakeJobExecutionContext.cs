using Quartz;

namespace SchematicHQ.Community.AspNetCore.Tests.Infrastructure;

internal sealed class FakeJobExecutionContext : IJobExecutionContext
{
    public FakeJobExecutionContext(IJobDetail jobDetail, JobDataMap? mergedJobDataMap = null)
    {
        JobDetail = jobDetail;
        MergedJobDataMap = mergedJobDataMap ?? new JobDataMap();
    }

    public IScheduler Scheduler => throw new NotSupportedException();
    public ITrigger Trigger => throw new NotSupportedException();
    public ICalendar? Calendar => null;
    public bool Recovering => false;
    public TriggerKey RecoveringTriggerKey => throw new NotSupportedException();
    public int RefireCount => 0;
    public JobDataMap MergedJobDataMap { get; }
    public IJobDetail JobDetail { get; }
    public IJob JobInstance => throw new NotSupportedException();
    public DateTimeOffset FireTimeUtc => DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFireTimeUtc => null;
    public DateTimeOffset? PreviousFireTimeUtc => null;
    public DateTimeOffset? NextFireTimeUtc => null;
    public string FireInstanceId => "fake";
    public object? Result { get; set; }
    public TimeSpan JobRunTime => TimeSpan.Zero;
    public CancellationToken CancellationToken => CancellationToken.None;

    public void Put(object key, object objectValue)
    {
    }

    public object? Get(object key) => null;
}
