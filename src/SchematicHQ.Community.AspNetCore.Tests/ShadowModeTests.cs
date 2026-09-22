using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchematicHQ.Community.AspNetCore.Tests.Infrastructure;
using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Community.Testing;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

/// <summary>
/// Shadow mode is the rollout guard between "gating is wired" and "gating is enforced": every denial is logged
/// and allowed, everything else reaches the real client untouched.
/// </summary>
internal sealed class ShadowModeTests
{
    private static readonly Dictionary<string, string> Company = new() { ["id"] = "company_1" };
    private static readonly Dictionary<string, string> User = new() { ["id"] = "user_1" };

    private static (ISchematicGateClient Client, CapturingLoggerProvider Logs) Build(Action<IServiceCollection> configure)
    {
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logs));
        configure(services);
        return (services.BuildServiceProvider().GetRequiredService<ISchematicGateClient>(), logs);
    }

    private static (ISchematicGateClient Client, CapturingLoggerProvider Logs) BuildOver(ISchematicGateClient inner)
        => Build(s =>
        {
            s.AddSingleton(inner);
            s.AddSchematicShadowMode();
        });

    private static async Task<bool> Check(ISchematicGateClient client, string flag = "reports")
        => (await client.CheckFlagWithEntitlementAsync(flag, Company, User, CancellationToken.None)).Value;

    [Test]
    public async Task Denied_check_is_answered_as_allowed_and_logged()
    {
        var fake = new FakeSchematicGateClient().Deny("reports");
        var (client, logs) = BuildOver(fake);

        var response = await client.CheckFlagWithEntitlementAsync("reports", Company, User, CancellationToken.None);

        response.Value.ShouldBeTrue();
        response.Reason.ShouldBe("denied by FakeSchematicGateClient");
        fake.CheckCalls.ShouldHaveSingleItem().FlagKey.ShouldBe("reports");

        var entry = logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("would deny flag 'reports'");
        entry.Message.ShouldContain("id=company_1");
        entry.Message.ShouldContain("id=user_1");
    }

    /// <summary>The inner client may serve the same instance from a cache, so the denial must survive there.</summary>
    [Test]
    public async Task Denied_response_from_the_inner_client_is_left_untouched()
    {
        var denied = CheckResponses.Deny("reports", "not_entitled");
        var (client, _) = BuildOver(new FakeSchematicGateClient().RespondToCheck(_ => denied));

        var response = await client.CheckFlagWithEntitlementAsync("reports", Company, User, CancellationToken.None);

        response.Value.ShouldBeTrue();
        response.ShouldNotBeSameAs(denied);
        denied.Value.ShouldBeFalse();
    }

    [Test]
    public async Task Allowed_check_passes_through_silently()
    {
        var (client, logs) = BuildOver(new FakeSchematicGateClient());

        (await Check(client)).ShouldBeTrue();
        logs.Entries.ShouldBeEmpty();
    }

    [Test]
    public async Task Tracking_identify_and_leases_reach_the_inner_client()
    {
        var fake = new FakeSchematicGateClient();
        var (client, _) = BuildOver(fake);

        client.Track("exports", Company, User, new(), 3);
        client.Identify(Company, null, "Acme", null);
        var lease = await client.AcquireCreditLeaseAsync("company_1", "credit_1", 10, null, CancellationToken.None);
        await client.TrackAgainstLeaseAsync(lease!.Id, "ai.tokens", Company, User, new(), 4, CancellationToken.None);
        await client.ExtendCreditLeaseAsync(lease.Id, 2, CancellationToken.None);
        await client.ReleaseCreditLeaseAsync(lease.Id, CancellationToken.None);

        fake.TrackCalls.ShouldHaveSingleItem().Quantity.ShouldBe(3);
        fake.IdentifyCalls.ShouldHaveSingleItem().Name.ShouldBe("Acme");
        fake.LeaseCalls.ShouldHaveSingleItem().RequestedAmount.ShouldBe(10);
        fake.LeaseTrackCalls.ShouldHaveSingleItem().Quantity.ShouldBe(4);
        fake.ExtendLeaseCalls.ShouldHaveSingleItem().AdditionalAmount.ShouldBe(2);
        fake.ReleasedLeases.ShouldBe([lease.Id]);
    }

    [Test]
    public async Task Wraps_a_type_registration_such_as_the_no_op_client()
    {
        var (client, _) = Build(s =>
        {
            s.AddSchematicNoOp(allowAll: false);
            s.AddSchematicShadowMode();
        });

        client.ShouldBeOfType<ShadowModeSchematicGateClient>();
        (await Check(client)).ShouldBeTrue();
    }

    [Test]
    public async Task Wraps_a_factory_registration()
    {
        var fake = new FakeSchematicGateClient().Deny("reports");
        var (client, _) = Build(s =>
        {
            s.AddSingleton<ISchematicGateClient>(_ => fake);
            s.AddSchematicShadowMode();
        });

        (await Check(client)).ShouldBeTrue();
        fake.CheckCalls.Count.ShouldBe(1);
    }

    [Test]
    public async Task Disabled_leaves_the_registration_alone()
    {
        var fake = new FakeSchematicGateClient().Deny("reports");
        var (client, _) = Build(s =>
        {
            s.AddSingleton<ISchematicGateClient>(fake);
            s.AddSchematicShadowMode(enabled: false);
        });

        client.ShouldBeSameAs(fake);
        (await Check(client)).ShouldBeFalse();
    }

    [Test]
    public void Registering_twice_wraps_once_and_the_wrapper_is_resolvable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchematicNoOp(allowAll: false);
        services.AddSchematicShadowMode();
        services.AddSchematicShadowMode();
        var provider = services.BuildServiceProvider();

        var wrapper = provider.GetRequiredService<ShadowModeSchematicGateClient>();
        provider.GetRequiredService<ISchematicGateClient>().ShouldBeSameAs(wrapper);
        wrapper.Inner.ShouldNotBeOfType<ShadowModeSchematicGateClient>();
    }

    [Test]
    public void Throws_when_no_gate_client_is_registered()
    {
        var services = new ServiceCollection();

        var ex = Should.Throw<InvalidOperationException>(() => services.AddSchematicShadowMode());

        ex.Message.ShouldContain("AddSchematic");
    }
}
