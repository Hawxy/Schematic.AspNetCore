using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchematicHQ.Client;
using SchematicHQ.Community.AspNetCore.Tests.Infrastructure;
using SchematicHQ.Community.Extensions.AI;
using SchematicHQ.Community.Testing;
using Shouldly;
using static SchematicHQ.Community.AspNetCore.Tests.Infrastructure.AiTestPipeline;

namespace SchematicHQ.Community.AspNetCore.Tests;

internal sealed class AiInferenceTests
{
    private const string EventName = "support-chat";

    private static readonly ChatClientMetadata AnthropicMetadata = new("anthropic", defaultModelId: "claude-sonnet-5-5");

    private static readonly Dictionary<string, double> Rates = new()
    {
        ["input_tokens"] = 0.0002,
        ["cached_input_tokens"] = 0.00002,
        ["cache_creation_input_tokens"] = 0.00025,
        ["output_tokens"] = 0.001,
    };

    private static IChatClient BuildInferencePipeline(
        IChatClient inner,
        FakeSchematicGateClient fake,
        Action<SchematicInferenceOptions>? configure = null)
        => BuildPipeline(inner, fake, b => b.UseSchematicUsageTracking(o =>
        {
            o.FallbackContext = Identity;
            o.Inference = new SchematicInferenceOptions { EventName = EventName };
            configure?.Invoke(o.Inference);
        }));

    private static IChatClient BuildInferenceLeasePipeline(
        IChatClient inner,
        FakeSchematicGateClient fake,
        Action<SchematicInferenceOptions>? configure = null)
        => BuildLeasePipeline(inner, fake, o =>
        {
            o.Inference = new SchematicInferenceOptions();
            configure?.Invoke(o.Inference);
        });

    [Test]
    public async Task Tracking_sends_one_inference_event_instead_of_track_events()
    {
        var inner = new StubChatClient
        {
            Metadata = AnthropicMetadata,
            Response = ResponseWithUsage(input: 2_667, output: 104, modelId: "claude-sonnet-5-5-20261001"),
        };
        var fake = new FakeSchematicGateClient();
        var client = BuildInferencePipeline(inner, fake);

        await client.GetResponseAsync("hi", new ChatOptions { ModelId = "claude-sonnet-5-5" });

        fake.TrackCalls.ShouldBeEmpty();
        var call = fake.InferenceCalls.ShouldHaveSingleItem();
        call.Options.ShouldNotBeNull().IdempotencyKey.ShouldNotBeNullOrWhiteSpace();
        var body = call.Body;
        body.Event.ShouldBe(EventName);
        body.Company!["id"].ShouldBe("company_ai");
        body.User!["id"].ShouldBe("user_ai");
        body.Provider.ShouldBe("anthropic");
        body.RequestModel.ShouldBe("claude-sonnet-5-5");
        body.ResponseModel.ShouldBe("claude-sonnet-5-5-20261001");
        body.InputTokens.ShouldBe(2_667);
        body.OutputTokens.ShouldBe(104);
        body.LeaseId.ShouldBeNull();
        body.Cost.ShouldBeNull();
    }

    [Test]
    public async Task Each_event_gets_its_own_idempotency_key()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 10) };
        var fake = new FakeSchematicGateClient();
        var client = BuildInferencePipeline(inner, fake);

        await client.GetResponseAsync("one");
        await client.GetResponseAsync("two");

        fake.InferenceCalls.Select(c => c.Options!.IdempotencyKey).Distinct().Count().ShouldBe(2);
    }

    [Test]
    public async Task Provider_option_overrides_the_client_metadata()
    {
        var inner = new StubChatClient
        {
            Metadata = new ChatClientMetadata("aws.bedrock"),
            Response = ResponseWithUsage(input: 10, output: 10),
        };
        var fake = new FakeSchematicGateClient();
        var client = BuildInferencePipeline(inner, fake, i => i.Provider = "anthropic");

        await client.GetResponseAsync("hi");

        fake.InferenceCalls.ShouldHaveSingleItem().Body.Provider.ShouldBe("anthropic");
    }

    [Test]
    public async Task Missing_provider_and_models_fall_back_to_the_requested_model_and_unknown()
    {
        var inner = new StubChatClient
        {
            Metadata = new ChatClientMetadata(defaultModelId: "default-model"),
            Response = ResponseWithUsage(input: 10, output: 10, modelId: null),
        };
        var fake = new FakeSchematicGateClient();
        var client = BuildInferencePipeline(inner, fake);

        await client.GetResponseAsync("hi");

        var body = fake.InferenceCalls.ShouldHaveSingleItem().Body;
        body.Provider.ShouldBe("unknown");
        body.RequestModel.ShouldBe("default-model");
        body.ResponseModel.ShouldBe("default-model");
    }

    [Test]
    public async Task Missing_provider_and_model_are_each_warned_about_once()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 10, modelId: null) };
        var fake = new FakeSchematicGateClient();
        var logs = new CapturingLoggerProvider();
        var client = BuildPipeline(inner, fake,
            b => b.UseSchematicUsageTracking(o =>
            {
                o.FallbackContext = Identity;
                o.Inference = new SchematicInferenceOptions { EventName = EventName };
            }),
            services => services.AddLogging(l => l.AddProvider(logs)));

        await client.GetResponseAsync("one");
        await client.GetResponseAsync("two");

        var warnings = logs.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToArray();
        warnings.Count(m => m.Contains("provider 'unknown'")).ShouldBe(1);
        warnings.Count(m => m.Contains("model 'unknown'")).ShouldBe(1);
        fake.InferenceCalls.Count.ShouldBe(2);
    }

    [Test]
    public async Task Known_provider_and_model_log_no_warning()
    {
        var inner = new StubChatClient { Metadata = AnthropicMetadata, Response = ResponseWithUsage(input: 10, output: 10) };
        var fake = new FakeSchematicGateClient();
        var logs = new CapturingLoggerProvider();
        var client = BuildPipeline(inner, fake,
            b => b.UseSchematicUsageTracking(o =>
            {
                o.FallbackContext = Identity;
                o.Inference = new SchematicInferenceOptions { EventName = EventName };
            }),
            services => services.AddLogging(l => l.AddProvider(logs)));

        await client.GetResponseAsync("hi");

        logs.Entries.ShouldNotContain(e => e.Level == LogLevel.Warning);
    }

    [Test]
    public async Task Default_mapping_passes_cached_and_reasoning_counts_through()
    {
        var inner = new StubChatClient
        {
            Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 1_000,
                    CachedInputTokenCount = 800,
                    OutputTokenCount = 300,
                    ReasoningTokenCount = 120,
                },
            },
        };
        var fake = new FakeSchematicGateClient();
        var client = BuildInferencePipeline(inner, fake);

        await client.GetResponseAsync("hi");

        var body = fake.InferenceCalls.ShouldHaveSingleItem().Body;
        body.InputTokens.ShouldBe(1_000);
        body.CachedInputTokens.ShouldBe(800);
        body.CacheCreationInputTokens.ShouldBeNull();
        body.OutputTokens.ShouldBe(300);
        body.ReasoningTokens.ShouldBe(120);
    }

    [Test]
    public async Task Bucketed_mapping_adds_cache_reads_and_writes_to_the_input_count()
    {
        var inner = new StubChatClient
        {
            Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 4,
                    OutputTokenCount = 104,
                    AdditionalCounts = new AdditionalPropertiesDictionary<long>
                    {
                        ["cache_read_input_tokens"] = 2_259,
                        ["CacheCreationInputTokens"] = 404,
                    },
                },
            },
        };
        var fake = new FakeSchematicGateClient();
        var client = BuildInferencePipeline(inner, fake, i => i.MapUsage = SchematicInferenceOptions.BucketedCacheUsageMapping);

        await client.GetResponseAsync("hi");

        var body = fake.InferenceCalls.ShouldHaveSingleItem().Body;
        body.InputTokens.ShouldBe(2_667);
        body.CachedInputTokens.ShouldBe(2_259);
        body.CacheCreationInputTokens.ShouldBe(404);
        body.OutputTokens.ShouldBe(104);
    }

    [Test]
    public async Task Cost_is_sent_as_an_invariant_decimal_string_rounded_to_eight_places()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 10) };
        var fake = new FakeSchematicGateClient();
        var client = BuildInferencePipeline(inner, fake, i => i.MapUsage = (usage, _) =>
            new SchematicInferenceUsage(usage.InputTokenCount ?? 0, usage.OutputTokenCount ?? 0) { Cost = 0.012345678912m });

        var culture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            await client.GetResponseAsync("hi");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }

        fake.InferenceCalls.ShouldHaveSingleItem().Body.Cost.ShouldBe("0.01234568");
    }

    [Test]
    public async Task Zero_or_missing_usage_sends_nothing()
    {
        var fake = new FakeSchematicGateClient();
        var zero = BuildInferencePipeline(new StubChatClient { Response = ResponseWithUsage(0, 0) }, fake);
        var missing = BuildInferencePipeline(new StubChatClient(), fake);

        await zero.GetResponseAsync("hi");
        await missing.GetResponseAsync("hi");

        fake.InferenceCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Streaming_aggregates_usage_and_sends_once()
    {
        var inner = new StubChatClient
        {
            Metadata = AnthropicMetadata,
            Updates =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "hel") { ModelId = "stream-model" },
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
        var fake = new FakeSchematicGateClient();
        var client = BuildInferencePipeline(inner, fake);

        await foreach (var _ in client.GetStreamingResponseAsync("hi"))
            fake.InferenceCalls.ShouldBeEmpty();

        var body = fake.InferenceCalls.ShouldHaveSingleItem().Body;
        body.ResponseModel.ShouldBe("stream-model");
        body.InputTokens.ShouldBe(7);
        body.OutputTokens.ShouldBe(5);
    }

    [Test]
    public async Task Abandoned_stream_still_sends_usage_reported_before_abandonment()
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
        var fake = new FakeSchematicGateClient();
        var client = BuildInferencePipeline(inner, fake);

        var enumerator = client.GetStreamingResponseAsync("hi").GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        await enumerator.DisposeAsync();

        fake.InferenceCalls.ShouldHaveSingleItem().Body.InputTokens.ShouldBe(9);
    }

    [Test]
    public async Task No_identity_sends_nothing()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(10, 10) };
        var fake = new FakeSchematicGateClient();
        var client = BuildPipeline(inner, fake, b => b.UseSchematicUsageTracking(o =>
            o.Inference = new SchematicInferenceOptions { EventName = EventName }));

        await client.GetResponseAsync("hi");

        fake.InferenceCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task A_send_failure_does_not_fail_the_call()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(10, 10) };
        var fake = new FakeSchematicGateClient { ThrowOnInference = new InvalidOperationException("down") };
        var client = BuildInferencePipeline(inner, fake);

        var response = await client.GetResponseAsync("hi");

        response.Text.ShouldBe("hello");
    }

    [Test]
    public void Tracking_without_an_event_name_fails_when_the_pipeline_is_built()
    {
        var fake = new FakeSchematicGateClient();

        Should.Throw<ArgumentException>(() => BuildPipeline(new StubChatClient(), fake, b =>
            b.UseSchematicUsageTracking(o => o.Inference = new SchematicInferenceOptions())));
    }

    [Test]
    public void Default_credit_cost_prices_each_token_type_and_the_flat_rate()
    {
        var entitlement = CheckResponses.AllowWithInferenceRates("f", Rates, consumptionRate: 1).Entitlement!;
        var usage = new SchematicInferenceUsage(2_667, 104)
        {
            CachedInputTokens = 2_259,
            CacheCreationInputTokens = 404,
        };

        // 1 + 4 * 0.0002 + 2259 * 0.00002 + 404 * 0.00025 + 104 * 0.001
        SchematicInferenceOptions.DefaultCreditCost(usage, entitlement).ShouldBe(1.25098, tolerance: 1e-9);
    }

    [Test]
    public async Task Lease_is_sized_from_per_token_rates_and_settled_with_one_inference_event()
    {
        var inner = new StubChatClient { Metadata = AnthropicMetadata, Response = ResponseWithUsage(input: 120, output: 45) };
        var fake = new FakeSchematicGateClient();
        fake.RespondToCheck(flag => CheckResponses.AllowWithInferenceRates(flag, Rates));
        var client = BuildInferenceLeasePipeline(inner, fake);

        await client.GetResponseAsync("hi");

        // Estimate is 100 input + 200 output.
        fake.LeaseCalls.ShouldHaveSingleItem().RequestedAmount.ShouldBe(100 * 0.0002 + 200 * 0.001, tolerance: 1e-9);

        var call = fake.LeaseInferenceCalls.ShouldHaveSingleItem();
        call.LeaseId.ShouldBe("lease_1");
        call.IdempotencyKey.ShouldNotBeNullOrWhiteSpace();
        call.Body.Event.ShouldBe("support-chat");
        call.Body.LeaseId.ShouldBe("lease_1");
        call.Body.Provider.ShouldBe("anthropic");
        call.Body.InputTokens.ShouldBe(120);
        call.Body.OutputTokens.ShouldBe(45);

        fake.ReleasedLeases.ShouldBe(["lease_1"]);
        fake.ExtendLeaseCalls.ShouldBeEmpty();
        fake.InferenceCalls.ShouldBeEmpty();
        fake.LeaseTrackCalls.ShouldBeEmpty();
        fake.TrackCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Lease_event_name_option_overrides_the_entitlement_subtype()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 1, output: 1) };
        var fake = new FakeSchematicGateClient();
        fake.RespondToCheck(flag => CheckResponses.AllowWithInferenceRates(flag, Rates));
        var client = BuildInferenceLeasePipeline(inner, fake, i => i.EventName = "custom-event");

        await client.GetResponseAsync("hi");

        fake.LeaseInferenceCalls.ShouldHaveSingleItem().Body.Event.ShouldBe("custom-event");
    }

    [Test]
    public async Task Usage_beyond_the_hold_extends_the_lease_before_sending()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 1_000, output: 500) };
        var fake = new FakeSchematicGateClient();
        fake.RespondToCheck(flag => CheckResponses.AllowWithInferenceRates(flag, Rates));
        var client = BuildInferenceLeasePipeline(inner, fake);

        await client.GetResponseAsync("hi");

        // Hold 0.22; actual 1000 * 0.0002 + 500 * 0.001 = 0.7.
        fake.ExtendLeaseCalls.ShouldHaveSingleItem().AdditionalAmount.ShouldBe(0.48, tolerance: 1e-9);
        fake.LeaseInferenceCalls.ShouldHaveSingleItem();
        fake.ReleasedLeases.ShouldBe(["lease_1"]);
    }

    [Test]
    public async Task A_failed_lease_send_falls_back_to_the_buffered_path_with_the_same_key()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 10) };
        var fake = new FakeSchematicGateClient { ThrowOnLeaseInference = new InvalidOperationException("down") };
        fake.RespondToCheck(flag => CheckResponses.AllowWithInferenceRates(flag, Rates));
        var client = BuildInferenceLeasePipeline(inner, fake);

        await client.GetResponseAsync("hi");

        var fallback = fake.InferenceCalls.ShouldHaveSingleItem();
        fallback.Body.LeaseId.ShouldBeNull();
        fallback.Options.ShouldNotBeNull().IdempotencyKey.ShouldNotBeNullOrWhiteSpace();
        fake.ReleasedLeases.ShouldBe(["lease_1"]);
    }

    [Test]
    public async Task Entitlement_without_credit_rates_sends_buffered_without_a_lease()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 10) };
        var fake = new FakeSchematicGateClient();
        fake.RespondToCheck(flag => CheckResponses.AllowWithInferenceRates(flag, quantityRates: []));
        var client = BuildInferenceLeasePipeline(inner, fake);

        await client.GetResponseAsync("hi");

        fake.LeaseCalls.ShouldBeEmpty();
        fake.InferenceCalls.ShouldHaveSingleItem().Body.Event.ShouldBe("support-chat");
    }

    [Test]
    public async Task Lease_without_any_event_name_sends_nothing_and_releases()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 10) };
        var fake = new FakeSchematicGateClient();
        fake.RespondToCheck(flag => CheckResponses.AllowWithInferenceRates(flag, Rates, eventSubtype: null));
        var client = BuildInferenceLeasePipeline(inner, fake);

        await client.GetResponseAsync("hi");

        fake.LeaseInferenceCalls.ShouldBeEmpty();
        fake.InferenceCalls.ShouldBeEmpty();
        fake.ReleasedLeases.ShouldBe(["lease_1"]);
    }

    [Test]
    public async Task Overdrawn_call_sends_the_inference_event_buffered()
    {
        var inner = new StubChatClient { Response = ResponseWithUsage(input: 10, output: 10) };
        var fake = new FakeSchematicGateClient();
        fake.RespondToCheck(CheckResponses.DenyForSpentCredits);
        var client = BuildLeasePipeline(inner, fake, o =>
        {
            o.AllowOverdraft = true;
            o.Inference = new SchematicInferenceOptions { EventName = EventName };
        });

        await client.GetResponseAsync("hi");

        fake.LeaseCalls.ShouldBeEmpty();
        fake.InferenceCalls.ShouldHaveSingleItem().Body.Event.ShouldBe(EventName);
    }
}
