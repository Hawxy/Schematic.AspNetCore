using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;
using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Community.Extensions.Quartz;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

internal sealed class TraitReportTests
{
    private sealed class FakeTraitPusher : ISchematicTraitPusher
    {
        private readonly object _lock = new();
        public List<CompanyTraitReport> Pushed { get; } = new();
        public Func<CompanyTraitReport, bool>? FailWhen { get; set; }

        public Task PushAsync(CompanyTraitReport report, CancellationToken cancellationToken)
        {
            if (FailWhen?.Invoke(report) == true)
                throw new InvalidOperationException("push failed");
            lock (_lock) Pushed.Add(report);
            return Task.CompletedTask;
        }
    }

    private sealed class ListTenantCatalog : ISchematicTenantCatalog
    {
        public List<string> TenantIds { get; } = new();
        public TraitReportContext? LastContext { get; private set; }

        public async IAsyncEnumerable<string> GetTenantIdsAsync(
            TraitReportContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastContext = context;
            await Task.Yield();
            foreach (var id in TenantIds)
                yield return id;
        }
    }

    private sealed class SeatSource : ISchematicTraitReportSource
    {
        public Func<string, CompanyTraitReport?> ReportFor { get; set; } = tenantId => Company(tenantId, seats: 1);

        public Task<CompanyTraitReport?> GetReportAsync(string tenantId, TraitReportContext context, CancellationToken cancellationToken)
            => Task.FromResult(ReportFor(tenantId));
    }

    private static CompanyTraitReport Company(string id, int seats)
        => new(new Dictionary<string, string> { ["id"] = id },
            new Dictionary<string, object?> { ["seats"] = seats });

    private static (ISchematicTraitReportRunner Runner, FakeTraitPusher Pusher) BuildRunner(
        ListTenantCatalog catalog, SeatSource? source = null)
    {
        var pusher = new FakeTraitPusher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(catalog);
        services.AddSingleton(source ?? new SeatSource());
        services.AddSingleton<ISchematicTraitPusher>(pusher);
        services.AddSchematicTraitReport<ListTenantCatalog, SeatSource>("seats");
        return (services.BuildServiceProvider().GetRequiredService<ISchematicTraitReportRunner>(), pusher);
    }

    [Test]
    public async Task Run_pushes_every_tenant_and_reports_counts()
    {
        var catalog = new ListTenantCatalog { TenantIds = { "acme", "globex", "initech" } };
        var (runner, pusher) = BuildRunner(catalog);

        var result = await runner.RunReportAsync("seats");

        result.ShouldBe(new TraitReportResult(Succeeded: 3, Failed: 0));
        pusher.Pushed.Select(r => r.Keys["id"]).ShouldBe(["acme", "globex", "initech"], ignoreOrder: true);
        catalog.LastContext.ShouldNotBeNull().ReportName.ShouldBe("seats");
    }

    [Test]
    public async Task Run_skips_tenants_whose_source_returns_null()
    {
        var catalog = new ListTenantCatalog { TenantIds = { "acme", "globex" } };
        var source = new SeatSource { ReportFor = tenantId => tenantId == "globex" ? null : Company(tenantId, 1) };
        var (runner, pusher) = BuildRunner(catalog, source);

        var result = await runner.RunReportAsync("seats");

        result.ShouldBe(new TraitReportResult(Succeeded: 1, Failed: 0));
        pusher.Pushed.ShouldHaveSingleItem().Keys["id"].ShouldBe("acme");
    }

    [Test]
    public async Task Run_isolates_per_tenant_source_failures()
    {
        var catalog = new ListTenantCatalog { TenantIds = { "acme", "globex", "initech" } };
        var source = new SeatSource
        {
            ReportFor = tenantId => tenantId == "globex"
                ? throw new InvalidOperationException("tenant db down")
                : Company(tenantId, 1),
        };
        var (runner, pusher) = BuildRunner(catalog, source);

        var result = await runner.RunReportAsync("seats");

        result.ShouldBe(new TraitReportResult(Succeeded: 2, Failed: 1));
        pusher.Pushed.Select(r => r.Keys["id"]).ShouldBe(["acme", "initech"], ignoreOrder: true);
    }

    [Test]
    public async Task Run_isolates_per_tenant_push_failures()
    {
        var catalog = new ListTenantCatalog { TenantIds = { "acme", "globex", "initech" } };
        var (runner, pusher) = BuildRunner(catalog);
        pusher.FailWhen = report => report.Keys["id"] == "globex";

        var result = await runner.RunReportAsync("seats");

        result.ShouldBe(new TraitReportResult(Succeeded: 2, Failed: 1));
        pusher.Pushed.Select(r => r.Keys["id"]).ShouldBe(["acme", "initech"], ignoreOrder: true);
    }

    [Test]
    public async Task Run_with_unknown_name_throws()
    {
        var (runner, _) = BuildRunner(new ListTenantCatalog());

        await Should.ThrowAsync<InvalidOperationException>(() => runner.RunReportAsync("nope"));
    }

    [Test]
    public void Duplicate_report_names_are_rejected()
    {
        var services = new ServiceCollection();
        services.AddSchematicTraitReport<ListTenantCatalog, SeatSource>("seats");

        Should.Throw<InvalidOperationException>(
            () => services.AddSchematicTraitReport<ListTenantCatalog, SeatSource>("seats"));
    }

    [Test]
    public void Reports_with_a_cron_are_scheduled_into_quartz_options()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchematicTraitReport<ListTenantCatalog, SeatSource>("seats", o => o.Cron = "0 0 3 * * ?");
        services.AddSchematicTraitReport<ListTenantCatalog, SeatSource>("manual-only");
        services.AddSchematicQuartz();

        var quartzOptions = services.BuildServiceProvider().GetRequiredService<IOptions<QuartzOptions>>().Value;

        var job = quartzOptions.JobDetails.ShouldHaveSingleItem();
        job.Key.ShouldBe(new JobKey("trait-report-seats", "schematic"));
        job.JobType.ShouldBe(typeof(SchematicTraitReportJob));
        job.JobDataMap.GetString(SchematicTraitReportJob.ReportNameKey).ShouldBe("seats");

        var trigger = quartzOptions.Triggers.ShouldHaveSingleItem();
        trigger.JobKey.ShouldBe(job.Key);
        ((ICronTrigger)trigger).CronExpressionString.ShouldBe("0 0 3 * * ?");
    }

    /// <summary>
    /// An integration test host boots the real application, cron and all. Without this it would fan out
    /// over every tenant and write to Schematic partway through a suite.
    /// </summary>
    [Test]
    public void Reports_with_scheduling_disabled_are_registered_but_not_scheduled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchematicTraitReport<ListTenantCatalog, SeatSource>("seats", o =>
        {
            o.Cron = "0 0 3 * * ?";
            o.ScheduleEnabled = false;
        });
        services.AddSchematicQuartz();

        var provider = services.BuildServiceProvider();

        var quartzOptions = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;
        quartzOptions.JobDetails.ShouldBeEmpty();
        quartzOptions.Triggers.ShouldBeEmpty();

        // Still registered, so it remains runnable on demand.
        provider.GetServices<SchematicTraitReportRegistration>()
            .ShouldHaveSingleItem()
            .Name.ShouldBe("seats");
    }

    [Test]
    public async Task Unscheduled_reports_can_still_be_run_on_demand()
    {
        var catalog = new ListTenantCatalog { TenantIds = { "acme" } };
        var pusher = new FakeTraitPusher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(catalog);
        services.AddSingleton(new SeatSource());
        services.AddSingleton<ISchematicTraitPusher>(pusher);
        services.AddSchematicTraitReport<ListTenantCatalog, SeatSource>("seats", o =>
        {
            o.Cron = "0 0 3 * * ?";
            o.ScheduleEnabled = false;
        });

        var runner = services.BuildServiceProvider().GetRequiredService<ISchematicTraitReportRunner>();
        var result = await runner.RunReportAsync("seats");

        result.ShouldBe(new TraitReportResult(Succeeded: 1, Failed: 0));
        pusher.Pushed.ShouldHaveSingleItem().Keys["id"].ShouldBe("acme");
    }

    [Test]
    public async Task Trait_report_job_runs_the_named_report()
    {
        var catalog = new ListTenantCatalog { TenantIds = { "acme" } };
        var (runner, pusher) = BuildRunner(catalog);
        var job = new SchematicTraitReportJob(runner);

        var detail = JobBuilder.Create<SchematicTraitReportJob>()
            .WithIdentity("trait-report-seats", "schematic")
            .UsingJobData(SchematicTraitReportJob.ReportNameKey, "seats")
            .Build();
        await job.Execute(new Infrastructure.FakeJobExecutionContext(detail, detail.JobDataMap));

        pusher.Pushed.ShouldHaveSingleItem().Keys["id"].ShouldBe("acme");
    }
}
