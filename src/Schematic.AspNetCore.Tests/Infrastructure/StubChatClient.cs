using Microsoft.Extensions.AI;

namespace Schematic.AspNetCore.Tests.Infrastructure;

internal sealed class StubChatClient : IChatClient
{
    public ChatResponse Response { get; set; } = new(new ChatMessage(ChatRole.Assistant, "ok"));
    public IReadOnlyList<ChatResponseUpdate> Updates { get; set; } = [];
    public int Calls { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(Response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls++;
        foreach (var update in Updates)
        {
            await Task.Yield();
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
