using Microsoft.Extensions.AI;
using SchematicHQ.Community.Extensions.AI;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

/// <summary>
/// The default mapping decides what an app meters unless it supplies its own, so anything it drops is
/// billed as if it never happened. Providers report cache and reasoning tokens in
/// <see cref="UsageDetails.AdditionalCounts"/>, which is why they are passed through rather than ignored.
/// </summary>
internal sealed class DefaultUsageMappingTests
{
    private static Dictionary<string, long> Map(UsageDetails usage, string? modelId = "test-model")
        => SchematicAiOptions.DefaultUsageMapping(usage, modelId)
            .ToDictionary(e => e.EventName, e => e.Quantity);

    private static UsageDetails Usage(long? input, long? output, params (string Key, long Count)[] additional)
    {
        var usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output };

        if (additional.Length > 0)
        {
            usage.AdditionalCounts = new AdditionalPropertiesDictionary<long>();
            foreach (var (key, count) in additional)
                usage.AdditionalCounts[key] = count;
        }

        return usage;
    }

    [Test]
    public async Task Maps_input_and_output_tokens()
    {
        Map(Usage(120, 45)).ShouldBe(new Dictionary<string, long>
        {
            ["ai.input-tokens"] = 120,
            ["ai.output-tokens"] = 45,
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task Emits_nothing_for_a_response_that_reported_no_usage()
    {
        SchematicAiOptions.DefaultUsageMapping(new UsageDetails(), "test-model").ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Passes_through_additional_counts_such_as_cache_reads()
    {
        var events = Map(Usage(
            1000,
            200,
            ("cache_read_input_tokens", 8000),
            ("cache_creation_input_tokens", 450)));

        events.ShouldBe(new Dictionary<string, long>
        {
            ["ai.input-tokens"] = 1000,
            ["ai.output-tokens"] = 200,
            ["ai.cache-read-input-tokens"] = 8000,
            ["ai.cache-creation-input-tokens"] = 450,
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// Providers disagree on casing, and an event name is a billing identifier — it must not change
    /// because a provider adapter switched from snake_case to PascalCase.
    /// </summary>
    [Test]
    [Arguments("cache_read_input_tokens", "ai.cache-read-input-tokens")]
    [Arguments("CacheReadInputTokens", "ai.cache-read-input-tokens")]
    [Arguments("cacheReadInputTokens", "ai.cache-read-input-tokens")]
    [Arguments("cache-read-input-tokens", "ai.cache-read-input-tokens")]
    [Arguments("reasoning tokens", "ai.reasoning-tokens")]
    public async Task Normalises_count_keys_to_kebab_case(string key, string expected)
    {
        Map(Usage(null, null, (key, 5))).Keys.ShouldHaveSingleItem().ShouldBe(expected);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Skips_zero_and_negative_counts()
    {
        var events = Map(Usage(0, 0, ("cache_read_input_tokens", 0), ("weird", -1)));

        events.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Tags_every_event_with_the_model()
    {
        var events = SchematicAiOptions
            .DefaultUsageMapping(Usage(10, 20, ("cache_read_input_tokens", 30)), "test-model")
            .ToList();

        events.Count.ShouldBe(3);
        events.ShouldAllBe(e => e.Traits!["model"]!.Equals("test-model"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Omits_traits_when_the_response_carried_no_model_id()
    {
        var events = SchematicAiOptions.DefaultUsageMapping(Usage(10, 20), modelId: null).ToList();

        events.ShouldAllBe(e => e.Traits == null);
        await Task.CompletedTask;
    }
}
