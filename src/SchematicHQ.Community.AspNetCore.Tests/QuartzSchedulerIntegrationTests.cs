using Microsoft.Extensions.DependencyInjection;
using Quartz;
using SchematicHQ.Community.AspNetCore.Tests.Infrastructure;
using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Community.Extensions.Quartz;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

/// <summary>
/// Runs the listeners through a real in-memory Quartz scheduler to prove the AddSchematicQuartz /
/// AddSchematic wiring, not just the listener logic.
/// </summary>
internal sealed class QuartzSchedulerIntegrationTests
{
    public sealed class ExecutionProbe
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Executed => _tcs.Task;
        public void Signal() => _tcs.TrySetResult();
    }

    [RequireFeature("job-flag")]
    public sealed class GatedProbeJob : IJob
    {
        private readonly ExecutionProbe _probe;
        public GatedProbeJob(ExecutionProbe probe) => _probe = probe;

        public Task Execute(IJobExecutionContext context)
        {
            _probe.Signal();
            return Task.CompletedTask;
        }
    }

    [TrackFeature("job-event", Quantity = 3)]
    public sealed class TrackedProbeJob : IJob
    {
        private readonly ExecutionProbe _probe;
        public TrackedProbeJob(ExecutionProbe probe) => _probe = probe;

        public Task Execute(IJobExecutionContext context)
        {
            _probe.Signal();
            return Task.CompletedTask;
        }
    }

    private static async Task<(ServiceProvider Provider, IScheduler Scheduler, FakeGateClient Fake, ExecutionProbe Probe)> StartSchedulerAsync()
    {
        var fake = new FakeGateClient();
        var probe = new ExecutionProbe();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISchematicGateClient>(fake);
        services.AddSingleton(probe);
        services.AddSchematicQuartz();
        services.AddQuartz(q =>
        {
            // The scheduler repository is static per process; parallel tests need distinct names.
            q.SchedulerName = $"schematic-tests-{Guid.NewGuid():N}";
            q.AddSchematic();
        });

        var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        return (provider, scheduler, fake, probe);
    }

    private static async Task StopAsync(ServiceProvider provider, IScheduler scheduler)
    {
        await scheduler.Shutdown(waitForJobsToComplete: true);
        await provider.DisposeAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(25, cts.Token);
        }
    }

    private static (IJobDetail Job, ITrigger Trigger) BuildJob<TJob>() where TJob : IJob
        => (JobBuilder.Create<TJob>().WithIdentity(typeof(TJob).Name).Build(),
            TriggerBuilder.Create().WithIdentity($"{typeof(TJob).Name}-now")
                .UsingSchematicCompany("id", "acme")
                .StartNow().Build());

    [Test]
    public async Task Gated_job_executes_when_the_flag_allows()
    {
        var (provider, scheduler, fake, probe) = await StartSchedulerAsync();
        try
        {
            fake.RespondToCheck(flag => CheckResponses.Allow(flag));
            var (job, trigger) = BuildJob<GatedProbeJob>();
            await scheduler.ScheduleJob(job, trigger);

            await probe.Executed.WaitAsync(TimeSpan.FromSeconds(10));

            var call = fake.CheckCalls.ShouldHaveSingleItem();
            call.FlagKey.ShouldBe("job-flag");
            call.Company.ShouldBe(new Dictionary<string, string> { ["id"] = "acme" });
        }
        finally
        {
            await StopAsync(provider, scheduler);
        }
    }

    [Test]
    public async Task Gated_job_is_vetoed_when_the_flag_denies()
    {
        var (provider, scheduler, fake, probe) = await StartSchedulerAsync();
        try
        {
            fake.RespondToCheck(flag => CheckResponses.Deny(flag));
            var (job, trigger) = BuildJob<GatedProbeJob>();
            await scheduler.ScheduleJob(job, trigger);

            await WaitUntilAsync(() => fake.CheckCalls.Count > 0);
            await Task.Delay(250);

            probe.Executed.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            await StopAsync(provider, scheduler);
        }
    }

    [Test]
    public async Task Tracked_job_emits_the_event_after_execution()
    {
        var (provider, scheduler, fake, probe) = await StartSchedulerAsync();
        try
        {
            var (job, trigger) = BuildJob<TrackedProbeJob>();
            await scheduler.ScheduleJob(job, trigger);

            await probe.Executed.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => fake.TrackCalls.Count > 0);

            var track = fake.TrackCalls.ShouldHaveSingleItem();
            track.EventName.ShouldBe("job-event");
            track.Quantity.ShouldBe(3);
            track.Company.ShouldBe(new Dictionary<string, string> { ["id"] = "acme" });
        }
        finally
        {
            await StopAsync(provider, scheduler);
        }
    }
}
