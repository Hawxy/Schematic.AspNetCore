using Microsoft.Extensions.AI;
using SchematicHQ.Community.AspNetCore.Tests.Infrastructure;
using SchematicHQ.Community.Extensions.AI;
using SchematicHQ.Community.Testing;
using Shouldly;
using static SchematicHQ.Community.AspNetCore.Tests.Infrastructure.AiTestPipeline;

namespace SchematicHQ.Community.AspNetCore.Tests;

/// <summary>
/// The gating middlewares can be told to observe a denial rather than act on it — the whole call goes through,
/// the denial is reported — and the lease middleware can be told that a spent balance is overdraft, not a
/// refusal. Both exist so an app can bill past zero instead of blocking.
/// </summary>
internal sealed class AiDenialBehaviorTests
{
    private static IChatClient BuildGatedPipeline(
        IChatClient inner,
        FakeSchematicGateClient fake,
        Action<SchematicAiOptions> configure)
        => BuildPipeline(inner, fake, b => b
            .UseSchematicRequireFeature(Flag, o =>
            {
                o.FallbackContext = Identity;
                configure(o);
            })
            .UseSchematicUsageTracking(o => o.FallbackContext = Identity));

    private static Func<SchematicAiDenial, ValueTask> Collect(List<SchematicAiDenial> denials)
        => d => { denials.Add(d); return ValueTask.CompletedTask; };

    [Test]
    public async Task Allow_invokes_the_model_on_a_denied_check_and_reports_the_denial()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 20) };
        var fake = new FakeSchematicGateClient().Deny(Flag);
        var denials = new List<SchematicAiDenial>();
        var client = BuildGatedPipeline(inner, fake, o =>
        {
            o.DenialBehavior = SchematicDenialBehavior.Allow;
            o.OnDenied = Collect(denials);
        });

        var response = await client.GetResponseAsync("hi");

        response.Text.ShouldBe("hello");
        inner.Calls.ShouldBe(1);
        var denial = denials.ShouldHaveSingleItem();
        denial.FlagKey.ShouldBe(Flag);
        denial.Reason.ShouldBe("denied by FakeSchematicGateClient");
        denial.Allowed.ShouldBeTrue();
        denial.Context.ShouldBe(Identity);
        denial.Response.ShouldNotBeNull().Value.ShouldBeFalse();
        fake.TrackCalls.Count.ShouldBe(2);
    }

    [Test]
    public async Task Allow_invokes_the_model_when_no_identity_resolves()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 20) };
        var fake = new FakeSchematicGateClient();
        var denials = new List<SchematicAiDenial>();
        var client = BuildGatedPipeline(inner, fake, o =>
        {
            o.FallbackContext = null;
            o.DenialBehavior = SchematicDenialBehavior.Allow;
            o.OnDenied = Collect(denials);
        });

        await client.GetResponseAsync("hi");

        inner.Calls.ShouldBe(1);
        fake.CheckCalls.ShouldBeEmpty();
        var denial = denials.ShouldHaveSingleItem();
        denial.Reason.ShouldBe("no_schematic_context");
        denial.Context.ShouldBeNull();
        denial.Response.ShouldBeNull();
        denial.Allowed.ShouldBeTrue();
    }

    [Test]
    public async Task Deny_reports_the_denial_and_then_throws()
    {
        var inner = new StubChatClient();
        var fake = new FakeSchematicGateClient().Deny(Flag);
        var denials = new List<SchematicAiDenial>();
        var client = BuildGatedPipeline(inner, fake, o => o.OnDenied = Collect(denials));

        await Should.ThrowAsync<SchematicFeatureDeniedException>(() => client.GetResponseAsync("hi"));

        inner.Calls.ShouldBe(0);
        denials.ShouldHaveSingleItem().Allowed.ShouldBeFalse();
    }

    [Test]
    public async Task Allowed_check_does_not_report()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(1, 1) };
        var denials = new List<SchematicAiDenial>();
        var client = BuildGatedPipeline(inner, new FakeSchematicGateClient(), o => o.OnDenied = Collect(denials));

        await client.GetResponseAsync("hi");

        denials.ShouldBeEmpty();
    }

    [Test]
    public async Task OnDenied_throwing_does_not_change_the_outcome()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(1, 1) };
        var client = BuildGatedPipeline(inner, new FakeSchematicGateClient().Deny(Flag), o =>
        {
            o.DenialBehavior = SchematicDenialBehavior.Allow;
            o.OnDenied = _ => throw new InvalidOperationException("callback bug");
        });

        await client.GetResponseAsync("hi");

        inner.Calls.ShouldBe(1);
    }

    [Test]
    public async Task Allow_does_not_cover_a_failed_check_under_fail_closed()
    {
        var inner = new StubChatClient();
        var fake = new FakeSchematicGateClient { ThrowOnCheck = new HttpRequestException("unreachable") };
        var client = BuildGatedPipeline(inner, fake, o => o.DenialBehavior = SchematicDenialBehavior.Allow);

        var ex = await Should.ThrowAsync<SchematicFeatureDeniedException>(() => client.GetResponseAsync("hi"));

        ex.Reason.ShouldBe("entitlement_check_failed");
        inner.Calls.ShouldBe(0);
    }

    [Test]
    public async Task Overdraft_lets_a_spent_balance_through_without_a_hold()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 20) };
        var fake = new FakeSchematicGateClient().RespondToCheck(CheckResponses.DenyForSpentCredits);
        var denials = new List<SchematicAiDenial>();
        var client = BuildLeasePipeline(inner, fake, o =>
        {
            o.AllowOverdraft = true;
            o.OnDenied = Collect(denials);
        });

        await client.GetResponseAsync("hi");

        inner.Calls.ShouldBe(1);
        fake.LeaseCalls.ShouldBeEmpty();
        fake.LeaseTrackCalls.ShouldBeEmpty();
        fake.TrackCalls.Count.ShouldBe(2);
        var denial = denials.ShouldHaveSingleItem();
        denial.Reason.ShouldBe("credit_balance_exhausted");
        denial.Allowed.ShouldBeTrue();
        denial.Response.ShouldNotBeNull().Entitlement.ShouldNotBeNull().CreditRemaining.ShouldBe(0);
    }

    [Test]
    public async Task Overdraft_still_denies_when_the_denial_is_not_about_credit()
    {
        var inner = new StubChatClient();
        var fake = new FakeSchematicGateClient().Deny(Flag);
        var client = BuildLeasePipeline(inner, fake, o => o.AllowOverdraft = true);

        await Should.ThrowAsync<SchematicFeatureDeniedException>(() => client.GetResponseAsync("hi"));

        inner.Calls.ShouldBe(0);
    }

    /// <summary>Either switch lets a refused hold through: the model runs and usage takes the buffered path.</summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Refused_hold_is_let_through_by_overdraft_or_allow(bool viaOverdraft)
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 20) };
        var fake = new FakeSchematicGateClient { RejectLease = true }
            .RespondToCheck(flag => CheckResponses.AllowWithCredits(flag, consumptionRate: 1));
        var denials = new List<SchematicAiDenial>();
        var client = BuildLeasePipeline(inner, fake, o =>
        {
            if (viaOverdraft)
                o.AllowOverdraft = true;
            else
                o.DenialBehavior = SchematicDenialBehavior.Allow;
            o.OnDenied = Collect(denials);
        });

        await client.GetResponseAsync("hi");

        inner.Calls.ShouldBe(1);
        fake.LeaseCalls.ShouldHaveSingleItem();
        fake.ReleasedLeases.ShouldBeEmpty();
        fake.TrackCalls.Count.ShouldBe(2);
        var denial = denials.ShouldHaveSingleItem();
        denial.Reason.ShouldBe("insufficient_credits");
        denial.Allowed.ShouldBeTrue();
    }

    [Test]
    public async Task Overdraft_does_not_report_when_the_balance_funds_the_hold()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 20) };
        var fake = new FakeSchematicGateClient().RespondToCheck(flag => CheckResponses.AllowWithCredits(flag, consumptionRate: 1));
        var denials = new List<SchematicAiDenial>();
        var client = BuildLeasePipeline(inner, fake, o =>
        {
            o.AllowOverdraft = true;
            o.OnDenied = Collect(denials);
        });

        await client.GetResponseAsync("hi");

        denials.ShouldBeEmpty();
        fake.ReleasedLeases.ShouldBe(["lease_1"]);
    }

    [Test]
    public async Task Allow_behaviour_without_identity_invokes_the_model_and_tracks_nothing()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 20) };
        var fake = new FakeSchematicGateClient();
        var client = BuildLeasePipeline(inner, fake, o =>
        {
            o.FallbackContext = null;
            o.DenialBehavior = SchematicDenialBehavior.Allow;
        });

        await client.GetResponseAsync("hi");

        inner.Calls.ShouldBe(1);
        fake.CheckCalls.ShouldBeEmpty();
        fake.LeaseCalls.ShouldBeEmpty();
        fake.TrackCalls.ShouldBeEmpty();
    }
}
