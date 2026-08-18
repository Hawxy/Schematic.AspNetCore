using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using SchematicHQ.Community.AspNetCore.Options;
using SchematicHQ.Community.AspNetCore.Resolvers;
using SchematicHQ.Community.AspNetCore.Tests.Infrastructure;
using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Community.Extensions.AI;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

internal sealed class AiChatClientTests
{
    private static readonly SchematicFlagContext Identity = new(
        Company: new() { ["id"] = "company_ai" },
        User: new() { ["id"] = "user_ai" });

    private static IChatClient BuildPipeline(
        StubChatClient inner,
        FakeGateClient fake,
        Func<ChatClientBuilder, ChatClientBuilder> configurePipeline,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISchematicGateClient>(fake);
        configureServices?.Invoke(services);

        return configurePipeline(new ChatClientBuilder(inner)).Build(services.BuildServiceProvider());
    }

    private static ChatResponse ResponseWithUsage(long input, long output, string? modelId = "test-model") =>
        new(new ChatMessage(ChatRole.Assistant, "hello"))
        {
            ModelId = modelId,
            Usage = new UsageDetails
            {
                InputTokenCount = input,
                OutputTokenCount = output,
                TotalTokenCount = input + output,
            },
        };

    [Test]
    public async Task Tracking_emits_default_input_and_output_events_with_model_trait()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 120, output: 45) };
        var fake = new FakeGateClient();
        var client = BuildPipeline(inner, fake,
            b => b.UseSchematicUsageTracking(o => o.FallbackContext = Identity));

        await client.GetResponseAsync("hi");

        fake.TrackCalls.Count.ShouldBe(2);
        fake.TrackCalls[0].EventName.ShouldBe("ai.input-tokens");
        fake.TrackCalls[0].Quantity.ShouldBe(120);
        fake.TrackCalls[0].Company["id"].ShouldBe("company_ai");
        fake.TrackCalls[0].Traits["model"].ShouldBe("test-model");
        fake.TrackCalls[1].EventName.ShouldBe("ai.output-tokens");
        fake.TrackCalls[1].Quantity.ShouldBe(45);
    }

    [Test]
    public async Task Tracking_honors_a_custom_usage_mapping()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 20) };
        var fake = new FakeGateClient();
        var client = BuildPipeline(inner, fake, b => b.UseSchematicUsageTracking(o =>
        {
            o.FallbackContext = Identity;
            o.MapUsage = (usage, _) => [new SchematicAiUsageEvent("ai.total", usage.TotalTokenCount ?? 0)];
        }));

        await client.GetResponseAsync("hi");

        fake.TrackCalls.Count.ShouldBe(1);
        fake.TrackCalls[0].EventName.ShouldBe("ai.total");
        fake.TrackCalls[0].Quantity.ShouldBe(30);
    }

    [Test]
    public async Task Tracking_clamps_quantities_above_int_max()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: (long)int.MaxValue + 500, output: 0) };
        var fake = new FakeGateClient();
        var client = BuildPipeline(inner, fake,
            b => b.UseSchematicUsageTracking(o => o.FallbackContext = Identity));

        await client.GetResponseAsync("hi");

        fake.TrackCalls.Count.ShouldBe(1);
        fake.TrackCalls[0].Quantity.ShouldBe(int.MaxValue);
    }

    [Test]
    public async Task Streaming_aggregates_usage_across_updates_and_tracks_once()
    {
        var inner = new StubChatClient
        {
            Updates =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "hel") { ModelId = "test-model" },
                new ChatResponseUpdate(ChatRole.Assistant, "lo"),
                new ChatResponseUpdate
                {
                    Contents = [new UsageContent(new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3 })],
                },
                new ChatResponseUpdate
                {
                    Contents = [new UsageContent(new UsageDetails { OutputTokenCount = 2 })],
                },
            ],
        };
        var fake = new FakeGateClient();
        var client = BuildPipeline(inner, fake,
            b => b.UseSchematicUsageTracking(o => o.FallbackContext = Identity));

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync("hi"))
            updates.Add(update);

        updates.Count.ShouldBe(4);
        fake.TrackCalls.Count.ShouldBe(2);
        fake.TrackCalls[0].EventName.ShouldBe("ai.input-tokens");
        fake.TrackCalls[0].Quantity.ShouldBe(7);
        fake.TrackCalls[0].Traits["model"].ShouldBe("test-model");
        fake.TrackCalls[1].EventName.ShouldBe("ai.output-tokens");
        fake.TrackCalls[1].Quantity.ShouldBe(5);
    }

    [Test]
    public async Task Abandoned_stream_still_tracks_usage_reported_before_abandonment()
    {
        var inner = new StubChatClient
        {
            Updates =
            [
                new ChatResponseUpdate
                {
                    Contents = [new UsageContent(new UsageDetails { InputTokenCount = 9 })],
                },
                new ChatResponseUpdate(ChatRole.Assistant, "never consumed"),
            ],
        };
        var fake = new FakeGateClient();
        var client = BuildPipeline(inner, fake,
            b => b.UseSchematicUsageTracking(o => o.FallbackContext = Identity));

        var enumerator = client.GetStreamingResponseAsync("hi").GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        await enumerator.DisposeAsync();

        fake.TrackCalls.Count.ShouldBe(1);
        fake.TrackCalls[0].EventName.ShouldBe("ai.input-tokens");
        fake.TrackCalls[0].Quantity.ShouldBe(9);
    }

    [Test]
    public async Task Tracking_skips_when_no_identity_resolves()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 10) };
        var fake = new FakeGateClient();
        var client = BuildPipeline(inner, fake, b => b.UseSchematicUsageTracking());

        var response = await client.GetResponseAsync("hi");

        response.ShouldNotBeNull();
        fake.TrackCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Tracking_failure_does_not_fail_the_ai_call()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 10) };
        var fake = new FakeGateClient { ThrowOnTrack = true };
        var client = BuildPipeline(inner, fake,
            b => b.UseSchematicUsageTracking(o => o.FallbackContext = Identity));

        var response = await client.GetResponseAsync("hi");

        response.Text.ShouldBe("hello");
        fake.TrackCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Tracking_uses_the_http_request_identity_when_available()
    {
        var requestServices = new ServiceCollection()
            .AddOptions()
            .AddSingleton<ISchematicFlagContextResolver>(new StubFlagContextResolver())
            .BuildServiceProvider();
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { RequestServices = requestServices },
        };

        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 5) };
        var fake = new FakeGateClient();
        var client = BuildPipeline(inner, fake,
            b => b.UseSchematicUsageTracking(),
            services => services.AddSingleton<IHttpContextAccessor>(accessor));

        await client.GetResponseAsync("hi");

        fake.TrackCalls.Count.ShouldBe(2);
        fake.TrackCalls[0].Company["id"].ShouldBe("company_test");
        fake.TrackCalls[0].User["id"].ShouldBe("user_test");
    }

    [Test]
    public async Task Gating_allows_entitled_calls_through()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 1, output: 1) };
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.Allow(flag));
        var client = BuildPipeline(inner, fake,
            b => b.UseSchematicRequireFeature("ai-chat", o => o.FallbackContext = Identity));

        var response = await client.GetResponseAsync("hi");

        response.Text.ShouldBe("hello");
        inner.Calls.ShouldBe(1);
        fake.CheckCalls.Count.ShouldBe(1);
        fake.CheckCalls[0].FlagKey.ShouldBe("ai-chat");
    }

    [Test]
    public async Task Gating_denies_before_invoking_the_model()
    {
        var inner = new StubChatClient();
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.Deny(flag, reason: "no_ai_entitlement"));
        var client = BuildPipeline(inner, fake,
            b => b.UseSchematicRequireFeature("ai-chat", o => o.FallbackContext = Identity));

        var ex = await Should.ThrowAsync<SchematicFeatureDeniedException>(() => client.GetResponseAsync("hi"));

        ex.FlagKey.ShouldBe("ai-chat");
        ex.Reason.ShouldBe("no_ai_entitlement");
        inner.Calls.ShouldBe(0);
    }

    [Test]
    public async Task Gating_denies_streaming_on_first_move_next()
    {
        var inner = new StubChatClient();
        var fake = new FakeGateClient();
        fake.RespondToCheck(flag => CheckResponses.Deny(flag));
        var client = BuildPipeline(inner, fake,
            b => b.UseSchematicRequireFeature("ai-chat", o => o.FallbackContext = Identity));

        var enumerator = client.GetStreamingResponseAsync("hi").GetAsyncEnumerator();
        await Should.ThrowAsync<SchematicFeatureDeniedException>(async () => await enumerator.MoveNextAsync());

        inner.Calls.ShouldBe(0);
    }

    [Test]
    public async Task Gating_denies_when_no_identity_resolves()
    {
        var inner = new StubChatClient();
        var fake = new FakeGateClient();
        var client = BuildPipeline(inner, fake, b => b.UseSchematicRequireFeature("ai-chat"));

        var ex = await Should.ThrowAsync<SchematicFeatureDeniedException>(() => client.GetResponseAsync("hi"));

        ex.Reason.ShouldBe("no_schematic_context");
        fake.CheckCalls.ShouldBeEmpty();
        inner.Calls.ShouldBe(0);
    }

    [Test]
    public async Task Gating_check_failure_fails_closed_by_default()
    {
        var inner = new StubChatClient();
        var fake = new FakeGateClient();
        fake.RespondToCheck(_ => throw new HttpRequestException("schematic unreachable"));
        var client = BuildPipeline(inner, fake,
            b => b.UseSchematicRequireFeature("ai-chat", o => o.FallbackContext = Identity));

        var ex = await Should.ThrowAsync<SchematicFeatureDeniedException>(() => client.GetResponseAsync("hi"));

        ex.Reason.ShouldBe("entitlement_check_failed");
        ex.InnerException.ShouldBeOfType<HttpRequestException>();
        inner.Calls.ShouldBe(0);
    }

    [Test]
    public async Task Gating_check_failure_with_fail_open_invokes_the_model()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 1, output: 1) };
        var fake = new FakeGateClient();
        fake.RespondToCheck(_ => throw new HttpRequestException("schematic unreachable"));
        var client = BuildPipeline(inner, fake, b => b.UseSchematicRequireFeature("ai-chat", o =>
        {
            o.FallbackContext = Identity;
            o.FailurePolicy = SchematicFailurePolicy.FailOpen;
        }));

        var response = await client.GetResponseAsync("hi");

        response.Text.ShouldBe("hello");
        inner.Calls.ShouldBe(1);
    }
}
