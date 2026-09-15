using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.AspNetCore.Tests.Infrastructure;

/// <summary>Builds a chat pipeline over a fake gate client for the AI middleware tests.</summary>
internal static class AiTestPipeline
{
    public static readonly SchematicFlagContext Identity = new(
        Company: new() { ["id"] = "company_ai" },
        User: new() { ["id"] = "user_ai" });

    public static IChatClient BuildPipeline(
        IChatClient inner,
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
