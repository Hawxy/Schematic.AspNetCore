using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Community.Extensions.AI;
using SchematicHQ.Community.Testing;

namespace SchematicHQ.Community.AspNetCore.Tests.Infrastructure;

/// <summary>Builds a chat pipeline over a fake gate client for the AI middleware tests.</summary>
internal static class AiTestPipeline
{
    public const string Flag = "ai-chat";

    public static readonly SchematicFlagContext Identity = new(
        Company: new() { ["id"] = "company_ai" },
        User: new() { ["id"] = "user_ai" });

    public static IChatClient BuildPipeline(
        IChatClient inner,
        FakeSchematicGateClient fake,
        Func<ChatClientBuilder, ChatClientBuilder> configurePipeline,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISchematicGateClient>(fake);
        configureServices?.Invoke(services);

        return configurePipeline(new ChatClientBuilder(inner)).Build(services.BuildServiceProvider());
    }

    /// <summary>
    /// <c>UseSchematicCreditLease</c> over <see cref="Flag"/> with <see cref="Identity"/> and a deterministic
    /// 100 input + 200 output token estimate, so hold sizes are predictable.
    /// </summary>
    public static IChatClient BuildLeasePipeline(
        IChatClient inner,
        FakeSchematicGateClient fake,
        Action<SchematicCreditLeaseOptions>? configure = null)
        => BuildPipeline(inner, fake, b => b.UseSchematicCreditLease(Flag, o =>
        {
            o.FallbackContext = Identity;
            o.EstimateUsage = FixedEstimate;
            configure?.Invoke(o);
        }));

    public static UsageDetails FixedEstimate(IEnumerable<ChatMessage> messages, ChatOptions? options)
        => new() { InputTokenCount = 100, OutputTokenCount = 200 };

    public static ChatResponse ResponseWithUsage(long input, long output, string? modelId = "test-model") =>
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
}
