using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SchematicHQ.Client;

namespace SchematicHQ.Community.DependencyInjection;

/// <summary>
/// Queues inference events and sends them in batches from a background loop, since the SDK only buffers
/// Track and Identify. Pending events are flushed when the service provider is disposed.
/// </summary>
internal sealed class SchematicInferenceEventBuffer : IAsyncDisposable
{
    internal const int Capacity = 10_000;
    internal const int MaxBatchSize = 100;

    private static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromSeconds(1);

    // Disposal has no cancellation token, so bound the flush ourselves to never hang shutdown.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly Func<IReadOnlyList<CreateEventRequestBody>, CancellationToken, Task> _send;
    private readonly ILogger<SchematicInferenceEventBuffer> _logger;
    private readonly TimeSpan _flushInterval;
    private readonly Channel<CreateEventRequestBody> _channel = Channel.CreateBounded<CreateEventRequestBody>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _worker;
    private int _disposed;

    public SchematicInferenceEventBuffer(
        Func<IReadOnlyList<CreateEventRequestBody>, CancellationToken, Task> send,
        ILogger<SchematicInferenceEventBuffer> logger,
        TimeSpan? flushInterval = null)
    {
        _send = send;
        _logger = logger;
        _flushInterval = flushInterval ?? DefaultFlushInterval;
        _worker = Task.Run(RunAsync);
    }

    public void Enqueue(CreateEventRequestBody body)
    {
        if (!_channel.Writer.TryWrite(body))
            _logger.LogWarning("Schematic inference event buffer is full or shut down; the event was dropped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _channel.Writer.TryComplete();
        _stopping.Cancel();

        try
        {
            await _worker.WaitAsync(ShutdownTimeout);
            _stopping.Dispose();
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Schematic inference event flush did not complete within {Timeout}; buffered events may be lost.",
                ShutdownTimeout);
        }
    }

    private async Task RunAsync()
    {
        var reader = _channel.Reader;
        while (await reader.WaitToReadAsync())
        {
            // Wait briefly so a burst of calls goes out as one batch, unless shutting down.
            if (reader.Count < MaxBatchSize && !_stopping.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_flushInterval, _stopping.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }

            while (reader.Count > 0)
            {
                var batch = new List<CreateEventRequestBody>(Math.Min(reader.Count, MaxBatchSize));
                while (batch.Count < MaxBatchSize && reader.TryRead(out var body))
                    batch.Add(body);

                await SendAsync(batch);
            }
        }
    }

    // The SDK client retries transient failures itself, so a failure here is final.
    private async Task SendAsync(IReadOnlyList<CreateEventRequestBody> batch)
    {
        try
        {
            await _send(batch, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sending {Count} Schematic inference events failed; they were dropped.", batch.Count);
        }
    }
}
