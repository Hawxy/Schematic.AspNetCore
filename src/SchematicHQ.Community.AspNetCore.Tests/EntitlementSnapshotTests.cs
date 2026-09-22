using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SchematicHQ.Community.AspNetCore.Options;
using SchematicHQ.Community.AspNetCore.Resolvers;
using SchematicHQ.Community.AspNetCore.Snapshots;
using SchematicHQ.Community.AspNetCore.Tests.Infrastructure;
using SchematicHQ.Community.DependencyInjection;
using Shouldly;
using SchematicHQ.Community.Testing;

namespace SchematicHQ.Community.AspNetCore.Tests;

/// <summary>
/// A snapshot is what callers branch on instead of a gate, so it must agree with the gates: same client, same
/// identity, and a failed check must not fail the call.
/// </summary>
internal sealed class EntitlementSnapshotTests
{
    private static readonly string[] Flags = ["reports", "exports", "ai-chat"];

    private static readonly SchematicFlagContext Identity = new(
        Company: new() { ["id"] = "company_1" },
        User: new() { ["id"] = "user_1" });

    private static readonly StubFlagContextResolver Resolver = new();

    private static (ISchematicEntitlementSnapshotProvider Provider, IServiceProvider Services) Build(
        FakeSchematicGateClient fake,
        Action<SchematicEntitlementSnapshotOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchematicFake(fake);
        services.AddSchematicEntitlementSnapshots(configure);
        services.AddSingleton<ISchematicFlagContextResolver>(Resolver);
        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ISchematicEntitlementSnapshotProvider>(), provider);
    }

    [Test]
    public async Task Evaluates_every_flag_for_the_identity()
    {
        var fake = new FakeSchematicGateClient().Deny("exports");
        var (provider, _) = Build(fake);

        var snapshot = await provider.GetAsync(Identity, Flags);

        snapshot.IsEntitled("reports").ShouldBeTrue();
        snapshot.IsEntitled("exports").ShouldBeFalse();
        snapshot.IsEntitled("ai-chat").ShouldBeTrue();
        snapshot["exports"].Reason.ShouldBe("denied by FakeSchematicGateClient");
        snapshot.AnyCheckFailed.ShouldBeFalse();
        fake.CheckCalls.Count.ShouldBe(3);
        fake.CheckCalls.ShouldAllBe(c => c.Company["id"] == "company_1" && c.User["id"] == "user_1");
    }

    [Test]
    public async Task Carries_the_entitlement_schematic_reported()
    {
        var fake = new FakeSchematicGateClient().RespondToCheck(flag =>
            CheckResponses.AllowWithCredits(flag, consumptionRate: 2, creditRemaining: 42));
        var (provider, _) = Build(fake);

        var snapshot = await provider.GetAsync(Identity, ["ai-chat"]);

        snapshot["ai-chat"].Entitlement.ShouldNotBeNull().CreditRemaining.ShouldBe(42);
    }

    [Test]
    public async Task Unknown_flag_throws_rather_than_guessing()
    {
        var (provider, _) = Build(new FakeSchematicGateClient());

        var snapshot = await provider.GetAsync(Identity, ["reports"]);

        Should.Throw<KeyNotFoundException>(() => snapshot.IsEntitled("exports"));
    }

    [Test]
    public async Task Failed_check_reads_false_under_fail_closed_and_is_marked()
    {
        var fake = new FakeSchematicGateClient { ThrowOnCheck = new HttpRequestException("unreachable") };
        var (provider, _) = Build(fake);

        var snapshot = await provider.GetAsync(Identity, Flags);

        snapshot.AnyCheckFailed.ShouldBeTrue();
        snapshot["reports"].Value.ShouldBeFalse();
        snapshot["reports"].CheckFailed.ShouldBeTrue();
        snapshot["reports"].Reason.ShouldBe("unreachable");
    }

    [Test]
    public async Task Failed_check_reads_true_under_fail_open()
    {
        var fake = new FakeSchematicGateClient { ThrowOnCheck = new HttpRequestException("unreachable") };
        var (provider, _) = Build(fake, o => o.FailurePolicy = SchematicFailurePolicy.FailOpen);

        var snapshot = await provider.GetAsync(Identity, Flags);

        snapshot.AnyCheckFailed.ShouldBeTrue();
        snapshot.IsEntitled("reports").ShouldBeTrue();
    }

    [Test]
    public async Task Request_overload_resolves_the_identity_like_the_gate()
    {
        var fake = new FakeSchematicGateClient().Deny("exports");
        var (provider, services) = Build(fake);
        var http = new DefaultHttpContext { RequestServices = services };

        var snapshot = await provider.GetAsync(http, Flags);

        snapshot.ShouldNotBeNull().IsEntitled("exports").ShouldBeFalse();
        fake.CheckCalls[0].Company["id"].ShouldBe(StubFlagContextResolver.DefaultContext.Company["id"]);
    }

    [Test]
    public async Task Request_overload_returns_null_without_an_identity()
    {
        var fake = new FakeSchematicGateClient();
        var resolver = new StubFlagContextResolver();
        resolver.SetContext(null);
        var (provider, services) = Build(fake, configureServices: s => s.AddSingleton<ISchematicFlagContextResolver>(resolver));
        var http = new DefaultHttpContext { RequestServices = services };

        var snapshot = await provider.GetAsync(http, Flags);

        snapshot.ShouldBeNull();
        fake.CheckCalls.ShouldBeEmpty();
    }

    /// <summary>The snapshot mirrors the gates unless told otherwise, so one setting governs both.</summary>
    [Test]
    public async Task Failure_policy_follows_the_gate_options_by_default()
    {
        var fake = new FakeSchematicGateClient { ThrowOnCheck = new HttpRequestException("unreachable") };
        var (provider, _) = Build(fake, configureServices: s =>
            s.Configure<SchematicAspNetCoreOptions>(o => o.FailurePolicy = SchematicFailurePolicy.FailOpen));

        var snapshot = await provider.GetAsync(Identity, Flags);

        snapshot.IsEntitled("reports").ShouldBeTrue();
        snapshot.AnyCheckFailed.ShouldBeTrue();
    }

    [Test]
    public void Registration_is_idempotent_and_options_compose()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISchematicGateClient>(new FakeSchematicGateClient());
        services.AddSchematicEntitlementSnapshots(o => o.FailurePolicy = SchematicFailurePolicy.FailOpen);
        services.AddSchematicEntitlementSnapshots();

        var provider = services.BuildServiceProvider();

        provider.GetServices<ISchematicEntitlementSnapshotProvider>().Count().ShouldBe(1);
        provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SchematicEntitlementSnapshotOptions>>()
            .Value.FailurePolicy.ShouldBe(SchematicFailurePolicy.FailOpen);
        provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SchematicAspNetCoreOptions>>().ShouldNotBeNull();
    }
}
