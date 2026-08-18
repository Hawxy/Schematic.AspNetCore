using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quartz;
using SchematicHQ.Community.AspNetCore.Tests.Infrastructure;
using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Community.Extensions.Quartz;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

internal sealed class QuartzListenerTests
{
    [RequireFeature("job-flag")]
    private sealed class GatedJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    [TrackFeature("job-event", Quantity = 3)]
    [TrackFeature("job-event-secondary")]
    private sealed class TrackedJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    private sealed class PlainJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    private static SchematicGateTriggerListener CreateGateListener(
        FakeGateClient client, SchematicFailurePolicy policy = SchematicFailurePolicy.FailClosed)
        => new(
            client,
            new JobDataMapContextResolver(),
            Microsoft.Extensions.Options.Options.Create(new SchematicQuartzOptions { FailurePolicy = policy }),
            NullLogger<SchematicGateTriggerListener>.Instance);

    private static SchematicTrackJobListener CreateTrackListener(FakeGateClient client)
        => new(client, new JobDataMapContextResolver(), NullLogger<SchematicTrackJobListener>.Instance);

    private static FakeJobExecutionContext CreateContext<TJob>(string? companyId = "acme") where TJob : IJob
    {
        var dataMap = new JobDataMap();
        if (companyId is not null)
            dataMap[SchematicJobDataKeys.CompanyPrefix + "id"] = companyId;

        return new FakeJobExecutionContext(
            JobBuilder.Create<TJob>().WithIdentity(typeof(TJob).Name).Build(), dataMap);
    }

    private static ITrigger CreateTrigger() => TriggerBuilder.Create().WithIdentity("test").StartNow().Build();

    [Test]
    public async Task Gate_ignores_jobs_without_the_attribute()
    {
        var fake = new FakeGateClient();
        var vetoed = await CreateGateListener(fake).VetoJobExecution(CreateTrigger(), CreateContext<PlainJob>());

        vetoed.ShouldBeFalse();
        fake.CheckCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Gate_vetoes_when_no_context_is_resolvable()
    {
        var fake = new FakeGateClient();
        var vetoed = await CreateGateListener(fake)
            .VetoJobExecution(CreateTrigger(), CreateContext<GatedJob>(companyId: null));

        vetoed.ShouldBeTrue();
        fake.CheckCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Gate_allows_when_the_flag_check_passes()
    {
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.Allow(flag));

        var vetoed = await CreateGateListener(fake).VetoJobExecution(CreateTrigger(), CreateContext<GatedJob>());

        vetoed.ShouldBeFalse();
        var call = fake.CheckCalls.ShouldHaveSingleItem();
        call.FlagKey.ShouldBe("job-flag");
        call.Company.ShouldBe(new Dictionary<string, string> { ["id"] = "acme" });
    }

    [Test]
    public async Task Gate_vetoes_when_the_flag_check_denies()
    {
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.Deny(flag));

        var vetoed = await CreateGateListener(fake).VetoJobExecution(CreateTrigger(), CreateContext<GatedJob>());

        vetoed.ShouldBeTrue();
    }

    [Test]
    public async Task Gate_check_failure_vetoes_under_fail_closed()
    {
        var fake = new FakeGateClient();
        fake.RespondToCheck(_ => throw new InvalidOperationException("backend down"));

        var vetoed = await CreateGateListener(fake, SchematicFailurePolicy.FailClosed)
            .VetoJobExecution(CreateTrigger(), CreateContext<GatedJob>());

        vetoed.ShouldBeTrue();
    }

    [Test]
    public async Task Gate_check_failure_allows_under_fail_open()
    {
        var fake = new FakeGateClient();
        fake.RespondToCheck(_ => throw new InvalidOperationException("backend down"));

        var vetoed = await CreateGateListener(fake, SchematicFailurePolicy.FailOpen)
            .VetoJobExecution(CreateTrigger(), CreateContext<GatedJob>());

        vetoed.ShouldBeFalse();
    }

    [Test]
    public async Task Track_emits_one_event_per_attribute_on_success()
    {
        var fake = new FakeGateClient();
        await CreateTrackListener(fake).JobWasExecuted(CreateContext<TrackedJob>(), jobException: null);

        fake.TrackCalls.Count.ShouldBe(2);
        fake.TrackCalls.Single(c => c.EventName == "job-event").Quantity.ShouldBe(3);
        fake.TrackCalls.Single(c => c.EventName == "job-event-secondary").Quantity.ShouldBeNull();
        fake.TrackCalls[0].Company.ShouldBe(new Dictionary<string, string> { ["id"] = "acme" });
    }

    [Test]
    public async Task Track_skips_faulted_executions()
    {
        var fake = new FakeGateClient();
        await CreateTrackListener(fake).JobWasExecuted(
            CreateContext<TrackedJob>(), new JobExecutionException("boom"));

        fake.TrackCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Track_skips_when_no_context_is_resolvable()
    {
        var fake = new FakeGateClient();
        await CreateTrackListener(fake).JobWasExecuted(CreateContext<TrackedJob>(companyId: null), jobException: null);

        fake.TrackCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Track_failure_is_swallowed()
    {
        var fake = new FakeGateClient { ThrowOnTrack = true };
        await CreateTrackListener(fake).JobWasExecuted(CreateContext<TrackedJob>(), jobException: null);

        fake.TrackCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Resolver_reads_company_and_user_prefixes_and_ignores_other_entries()
    {
        var dataMap = new JobDataMap
        {
            [SchematicJobDataKeys.CompanyPrefix + "id"] = "acme",
            [SchematicJobDataKeys.UserPrefix + "email"] = "jo@acme.test",
            ["unrelated"] = "value",
        };
        var context = new FakeJobExecutionContext(
            JobBuilder.Create<PlainJob>().WithIdentity("plain").Build(), dataMap);

        var resolved = await new JobDataMapContextResolver().ResolveAsync(context, CancellationToken.None);

        resolved.ShouldNotBeNull();
        resolved.Company.ShouldBe(new Dictionary<string, string> { ["id"] = "acme" });
        resolved.User.ShouldBe(new Dictionary<string, string> { ["email"] = "jo@acme.test" });
    }
}
