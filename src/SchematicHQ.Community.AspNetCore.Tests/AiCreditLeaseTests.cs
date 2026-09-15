using Microsoft.Extensions.AI;
using SchematicHQ.Community.AspNetCore.Tests.Infrastructure;
using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Community.Extensions.AI;
using Shouldly;
using static SchematicHQ.Community.AspNetCore.Tests.Infrastructure.AiTestPipeline;

namespace SchematicHQ.Community.AspNetCore.Tests;

internal sealed class AiCreditLeaseTests
{
    private const string Flag = "ai-chat";

    /// <summary>Deterministic hold: 100 input + 200 output tokens.</summary>
    private static UsageDetails FixedEstimate(IEnumerable<ChatMessage> messages, ChatOptions? options)
        => new() { InputTokenCount = 100, OutputTokenCount = 200 };

    private static IChatClient BuildLeasePipeline(
        IChatClient inner,
        FakeGateClient fake,
        Action<SchematicCreditLeaseOptions>? configure = null,
        Func<IEnumerable<ChatMessage>, ChatOptions?, UsageDetails>? estimate = null)
        => BuildPipeline(inner, fake, b => b.UseSchematicCreditLease(Flag, o =>
        {
            o.FallbackContext = Identity;
            o.EstimateUsage = estimate ?? FixedEstimate;
            configure?.Invoke(o);
        }));

    [Test]
    public async Task Lease_covers_the_estimated_credits_and_usage_is_tracked_against_it()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 120, output: 45) };
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.AllowWithCredits(flag, consumptionRate: 2));
        var client = BuildLeasePipeline(inner, fake);

        await client.GetResponseAsync("hi");

        var lease = fake.LeaseCalls.ShouldHaveSingleItem();
        lease.CompanyId.ShouldBe("company_1");
        lease.CreditTypeId.ShouldBe("credit_1");
        lease.RequestedAmount.ShouldBe(600); // (100 + 200) * 2
        lease.ExpiresAt.ShouldNotBeNull().ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(5));

        fake.LeaseTrackCalls.Count.ShouldBe(2);
        fake.LeaseTrackCalls.ShouldAllBe(c => c.LeaseId == "lease_1");
        fake.LeaseTrackCalls[0].EventName.ShouldBe("ai.input-tokens");
        fake.LeaseTrackCalls[0].Quantity.ShouldBe(120);
        fake.LeaseTrackCalls[0].Company["id"].ShouldBe("company_ai");
        fake.LeaseTrackCalls[0].Traits["model"].ShouldBe("test-model");
        fake.LeaseTrackCalls[1].EventName.ShouldBe("ai.output-tokens");
        fake.LeaseTrackCalls[1].Quantity.ShouldBe(45);

        fake.ReleasedLeases.ShouldBe(["lease_1"]);
        fake.ExtendLeaseCalls.ShouldBeEmpty();
        fake.TrackCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Default_estimate_uses_message_length_and_max_output_tokens()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(1, 1) };
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.AllowWithCredits(flag, consumptionRate: 1));
        var client = BuildLeasePipeline(inner, fake, estimate: SchematicCreditLeaseOptions.DefaultUsageEstimate);

        // "hello world" is 11 characters => 3 input tokens; 50 output tokens from the options.
        await client.GetResponseAsync("hello world", new ChatOptions { MaxOutputTokens = 50 });

        fake.LeaseCalls.ShouldHaveSingleItem().RequestedAmount.ShouldBe(53);
    }

    [Test]
    public async Task Usage_beyond_the_hold_extends_the_lease_before_tracking()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 1_000, output: 500) };
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.AllowWithCredits(flag, consumptionRate: 1));
        var client = BuildLeasePipeline(inner, fake);

        await client.GetResponseAsync("hi");

        // Hold was 300; actual cost is 1500.
        var extend = fake.ExtendLeaseCalls.ShouldHaveSingleItem();
        extend.LeaseId.ShouldBe("lease_1");
        extend.AdditionalAmount.ShouldBe(1_200);
        fake.LeaseTrackCalls.Count.ShouldBe(2);
        fake.ReleasedLeases.ShouldBe(["lease_1"]);
    }

    [Test]
    public async Task Custom_credit_cost_can_price_events_differently()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 100, output: 10) };
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.AllowWithCredits(flag, consumptionRate: 1));
        var client = BuildLeasePipeline(inner, fake, o => o.CreditCost = (events, _) =>
            events.Sum(e => e.EventName == "ai.output-tokens" ? e.Quantity * 5.0 : e.Quantity));

        await client.GetResponseAsync("hi");

        fake.LeaseCalls.ShouldHaveSingleItem().RequestedAmount.ShouldBe(1_100); // 100 + 200 * 5
        fake.ExtendLeaseCalls.ShouldBeEmpty();                                  // actual 100 + 10 * 5 = 150
    }

    [Test]
    public async Task Streaming_settles_when_enumeration_completes()
    {
        var inner = new StubChatClient
        {
            Updates =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "hel") { ModelId = "stream-model" },
                new ChatResponseUpdate(ChatRole.Assistant, "lo"),
                new ChatResponseUpdate
                {
                    Contents = [new UsageContent(new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3 })],
                },
            ],
        };
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.AllowWithCredits(flag, consumptionRate: 1));
        var client = BuildLeasePipeline(inner, fake);

        var chunks = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync("hi"))
        {
            chunks.Add(update.Text);
            fake.LeaseTrackCalls.ShouldBeEmpty();
            fake.ReleasedLeases.ShouldBeEmpty();
        }

        string.Concat(chunks).ShouldBe("hello");
        fake.LeaseCalls.ShouldHaveSingleItem();
        fake.LeaseTrackCalls.Count.ShouldBe(2);
        fake.LeaseTrackCalls[0].Quantity.ShouldBe(7);
        fake.LeaseTrackCalls[0].Traits["model"].ShouldBe("stream-model");
        fake.ReleasedLeases.ShouldBe(["lease_1"]);
    }

    [Test]
    public async Task Model_failure_releases_the_lease_without_tracking()
    {
        var inner = new StubChatClient { Throws = new InvalidOperationException("model down") };
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.AllowWithCredits(flag, consumptionRate: 1));
        var client = BuildLeasePipeline(inner, fake);

        await Should.ThrowAsync<InvalidOperationException>(() => client.GetResponseAsync("hi"));

        inner.Calls.ShouldBe(1);
        fake.LeaseCalls.ShouldHaveSingleItem();
        fake.LeaseTrackCalls.ShouldBeEmpty();
        fake.TrackCalls.ShouldBeEmpty();
        fake.ReleasedLeases.ShouldBe(["lease_1"]);
    }

    [Test]
    public async Task Denied_check_throws_before_any_lease_or_model_call()
    {
        var inner = new StubChatClient();
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.Deny(flag, "no_credits"));
        var client = BuildLeasePipeline(inner, fake);

        var ex = await Should.ThrowAsync<SchematicFeatureDeniedException>(() => client.GetResponseAsync("hi"));

        ex.FlagKey.ShouldBe(Flag);
        ex.Reason.ShouldBe("no_credits");
        inner.Calls.ShouldBe(0);
        fake.LeaseCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Non_credit_entitlement_gates_and_tracks_without_a_lease()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 20) };
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.Allow(flag));
        var client = BuildLeasePipeline(inner, fake);

        await client.GetResponseAsync("hi");

        fake.LeaseCalls.ShouldBeEmpty();
        fake.LeaseTrackCalls.ShouldBeEmpty();
        fake.TrackCalls.Count.ShouldBe(2);
        fake.TrackCalls[0].EventName.ShouldBe("ai.input-tokens");
        fake.TrackCalls[0].Quantity.ShouldBe(10);
    }

    [Test]
    public async Task Rejected_lease_denies_the_call()
    {
        var inner = new StubChatClient();
        var fake = new FakeGateClient { RejectLease = true };
        fake.RespondToCheck(flag => CheckResponses.AllowWithCredits(flag, consumptionRate: 1));
        var client = BuildLeasePipeline(inner, fake);

        var ex = await Should.ThrowAsync<SchematicFeatureDeniedException>(() => client.GetResponseAsync("hi"));

        ex.Reason.ShouldBe("insufficient_credits");
        inner.Calls.ShouldBe(0);
        fake.LeaseCalls.ShouldHaveSingleItem();
        fake.ReleasedLeases.ShouldBeEmpty();
    }

    [Test]
    public async Task Lease_failure_under_fail_closed_denies_the_call()
    {
        var inner = new StubChatClient();
        var fake = new FakeGateClient { ThrowOnAcquireLease = new HttpRequestException("backend down") };
        fake.RespondToCheck(flag => CheckResponses.AllowWithCredits(flag, consumptionRate: 1));
        var client = BuildLeasePipeline(inner, fake);

        var ex = await Should.ThrowAsync<SchematicFeatureDeniedException>(() => client.GetResponseAsync("hi"));

        ex.Reason.ShouldBe("credit_lease_failed");
        ex.InnerException.ShouldBeOfType<HttpRequestException>();
        inner.Calls.ShouldBe(0);
    }

    [Test]
    public async Task Lease_failure_under_fail_open_falls_back_to_buffered_tracking()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 20) };
        var fake = new FakeGateClient { ThrowOnAcquireLease = new HttpRequestException("backend down") };
        fake.RespondToCheck(flag => CheckResponses.AllowWithCredits(flag, consumptionRate: 1));
        var client = BuildLeasePipeline(inner, fake, o => o.FailurePolicy = SchematicFailurePolicy.FailOpen);

        await client.GetResponseAsync("hi");

        inner.Calls.ShouldBe(1);
        fake.LeaseTrackCalls.ShouldBeEmpty();
        fake.TrackCalls.Count.ShouldBe(2);
    }

    [Test]
    public async Task Lease_track_failure_falls_back_to_buffered_tracking_and_still_releases()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 20) };
        var fake = new FakeGateClient { ThrowOnLeaseTrack = true };
        fake.RespondToCheck(flag => CheckResponses.AllowWithCredits(flag, consumptionRate: 1));
        var client = BuildLeasePipeline(inner, fake);

        var response = await client.GetResponseAsync("hi");

        response.Text.ShouldBe("hello");
        fake.TrackCalls.Count.ShouldBe(2);
        fake.ReleasedLeases.ShouldBe(["lease_1"]);
    }

    [Test]
    public async Task Missing_identity_denies_the_call()
    {
        var inner = new StubChatClient();
        var fake = new FakeGateClient();
        var client = BuildLeasePipeline(inner, fake, o => o.FallbackContext = null);

        var ex = await Should.ThrowAsync<SchematicFeatureDeniedException>(() => client.GetResponseAsync("hi"));

        ex.Reason.ShouldBe("no_schematic_context");
        fake.CheckCalls.ShouldBeEmpty();
    }
}
