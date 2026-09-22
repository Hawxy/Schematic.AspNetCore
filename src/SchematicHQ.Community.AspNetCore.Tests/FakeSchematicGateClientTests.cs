using Microsoft.Extensions.DependencyInjection;
using SchematicHQ.Community.AspNetCore.Tests.Infrastructure;
using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Community.Testing;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

/// <summary>
/// The fake is what consumers assert against, so its own contract is pinned here: allow by default, deny
/// company-wide or per user, script or fail a check, record everything, and swap cleanly into a host.
/// </summary>
internal sealed class FakeSchematicGateClientTests
{
    private static readonly Dictionary<string, string> Company = new() { ["id"] = "company_1" };

    private static async Task<bool> Check(FakeSchematicGateClient fake, string flag, string userId = "user_1")
    {
        var response = await fake.CheckFlagWithEntitlementAsync(flag, Company, new() { ["id"] = userId }, CancellationToken.None);
        return response.Value;
    }

    [Test]
    public async Task Allows_everything_by_default_and_records_the_check()
    {
        var fake = new FakeSchematicGateClient();

        (await Check(fake, "reports")).ShouldBeTrue();

        var call = fake.CheckCalls.ShouldHaveSingleItem();
        call.FlagKey.ShouldBe("reports");
        call.Company["id"].ShouldBe("company_1");
        call.User["id"].ShouldBe("user_1");
    }

    [Test]
    public async Task Deny_applies_to_every_identity()
    {
        var fake = new FakeSchematicGateClient().Deny("reports", "exports");

        (await Check(fake, "reports")).ShouldBeFalse();
        (await Check(fake, "exports", "user_2")).ShouldBeFalse();
        (await Check(fake, "ai-chat")).ShouldBeTrue();
    }

    [Test]
    public async Task DenyForUser_applies_only_to_that_user()
    {
        var fake = new FakeSchematicGateClient().DenyForUser("user_2", "reports");

        (await Check(fake, "reports", "user_1")).ShouldBeTrue();
        (await Check(fake, "reports", "user_2")).ShouldBeFalse();
        (await fake.CheckFlagWithEntitlementAsync("reports", Company, new(), CancellationToken.None)).Value.ShouldBeTrue();
    }

    [Test]
    public async Task Scripted_response_wins_over_denials()
    {
        var fake = new FakeSchematicGateClient().Deny("reports")
            .RespondToCheck(flag => CheckResponses.Allow(flag, reason: "scripted"));

        var response = await fake.CheckFlagWithEntitlementAsync("reports", Company, new(), CancellationToken.None);

        response.Value.ShouldBeTrue();
        response.Reason.ShouldBe("scripted");
    }

    [Test]
    public async Task ThrowOnCheck_fails_the_check_after_recording_it()
    {
        var fake = new FakeSchematicGateClient { ThrowOnCheck = new HttpRequestException("unreachable") };

        await Should.ThrowAsync<HttpRequestException>(() => Check(fake, "reports"));

        fake.CheckCalls.Count.ShouldBe(1);
    }

    [Test]
    public async Task Reset_clears_state_and_recordings()
    {
        var fake = new FakeSchematicGateClient { ThrowOnTrack = new InvalidOperationException(), RejectLease = true }.Deny("reports");
        await Check(fake, "exports");

        fake.Reset();

        (await Check(fake, "reports")).ShouldBeTrue();
        fake.CheckCalls.Count.ShouldBe(1);
        fake.ThrowOnTrack.ShouldBeNull();
        fake.RejectLease.ShouldBeFalse();
        (await fake.AcquireCreditLeaseAsync("company_1", "credit_1", 5, null, CancellationToken.None))!.Id.ShouldBe("lease_1");
    }

    [Test]
    public void Recorded_lists_are_snapshots()
    {
        var fake = new FakeSchematicGateClient();
        var before = fake.TrackCalls;

        fake.Track("exports", Company, new(), new(), 1);

        before.ShouldBeEmpty();
        fake.TrackCalls.ShouldHaveSingleItem().EventName.ShouldBe("exports");
    }

    [Test]
    public void AddSchematicFake_replaces_the_registered_gate_client()
    {
        var fake = new FakeSchematicGateClient();
        var services = new ServiceCollection();
        services.AddSchematicNoOp();
        services.AddSchematicFake(new FakeSchematicGateClient());
        services.AddSchematicFake(fake);
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISchematicGateClient>().ShouldBeSameAs(fake);
        provider.GetRequiredService<FakeSchematicGateClient>().ShouldBeSameAs(fake);
        provider.GetServices<ISchematicGateClient>().Count().ShouldBe(1);
        provider.GetServices<FakeSchematicGateClient>().Count().ShouldBe(1);
    }
}
