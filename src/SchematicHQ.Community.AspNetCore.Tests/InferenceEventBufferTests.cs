using Microsoft.Extensions.Logging.Abstractions;
using SchematicHQ.Client;
using SchematicHQ.Community.DependencyInjection;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

internal sealed class InferenceEventBufferTests
{
    private sealed class RecordingSender
    {
        private readonly object _lock = new();
        private readonly List<IReadOnlyList<CreateEventRequestBody>> _batches = [];

        public Func<int, Exception?> FailAttempt { get; set; } = _ => null;

        public int Attempts { get; private set; }

        public IReadOnlyList<IReadOnlyList<CreateEventRequestBody>> Batches
        {
            get
            {
                lock (_lock)
                    return _batches.ToArray();
            }
        }

        public Task SendAsync(IReadOnlyList<CreateEventRequestBody> batch, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                Attempts++;
                if (FailAttempt(Attempts) is { } failure)
                    throw failure;
                _batches.Add(batch.ToArray());
            }

            return Task.CompletedTask;
        }
    }

    private static SchematicInferenceEventBuffer CreateBuffer(RecordingSender sender, TimeSpan flushInterval)
        => new(sender.SendAsync, NullLogger<SchematicInferenceEventBuffer>.Instance, flushInterval);

    private static CreateEventRequestBody Event(int n) => new()
    {
        EventType = EventType.Inference,
        IdempotencyKey = $"key_{n}",
        Body = new EventBodyInference
        {
            Provider = "openai",
            ResponseModel = "gpt",
            InputTokens = n,
            OutputTokens = 1,
        },
    };

    [Test]
    public async Task Events_queued_together_are_sent_as_one_batch()
    {
        var sender = new RecordingSender();
        var buffer = CreateBuffer(sender, TimeSpan.FromMilliseconds(50));

        buffer.Enqueue(Event(1));
        buffer.Enqueue(Event(2));
        buffer.Enqueue(Event(3));

        await WaitUntil(() => sender.Batches.Count > 0);
        await buffer.DisposeAsync();

        sender.Batches.ShouldHaveSingleItem().Select(e => e.IdempotencyKey).ShouldBe(["key_1", "key_2", "key_3"]);
    }

    [Test]
    public async Task Batches_are_capped_at_the_maximum_size()
    {
        var sender = new RecordingSender();
        var buffer = CreateBuffer(sender, TimeSpan.FromHours(1));

        for (var i = 0; i < SchematicInferenceEventBuffer.MaxBatchSize * 2 + 50; i++)
            buffer.Enqueue(Event(i));
        await buffer.DisposeAsync();

        sender.Batches.Sum(b => b.Count).ShouldBe(SchematicInferenceEventBuffer.MaxBatchSize * 2 + 50);
        sender.Batches.ShouldAllBe(b => b.Count <= SchematicInferenceEventBuffer.MaxBatchSize);
    }

    [Test]
    public async Task Dispose_flushes_pending_events_without_waiting_for_the_interval()
    {
        var sender = new RecordingSender();
        var buffer = CreateBuffer(sender, TimeSpan.FromHours(1));

        buffer.Enqueue(Event(1));
        await buffer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        sender.Batches.ShouldHaveSingleItem().ShouldHaveSingleItem().IdempotencyKey.ShouldBe("key_1");
    }

    [Test]
    public async Task A_failed_batch_is_dropped_without_a_retry_and_later_batches_still_send()
    {
        var sender = new RecordingSender { FailAttempt = attempt => attempt == 1 ? new HttpRequestException("down") : null };
        var buffer = CreateBuffer(sender, TimeSpan.FromMilliseconds(20));

        buffer.Enqueue(Event(1));
        await WaitUntil(() => sender.Attempts > 0);
        buffer.Enqueue(Event(2));
        await buffer.DisposeAsync();

        sender.Attempts.ShouldBe(2);
        sender.Batches.ShouldHaveSingleItem().ShouldHaveSingleItem().IdempotencyKey.ShouldBe("key_2");
    }

    [Test]
    public async Task Disposing_twice_is_safe()
    {
        var sender = new RecordingSender();
        var buffer = CreateBuffer(sender, TimeSpan.FromHours(1));
        buffer.Enqueue(Event(1));

        await buffer.DisposeAsync();
        await buffer.DisposeAsync();

        sender.Batches.ShouldHaveSingleItem();
    }

    [Test]
    public async Task Events_enqueued_after_dispose_are_dropped()
    {
        var sender = new RecordingSender();
        var buffer = CreateBuffer(sender, TimeSpan.FromHours(1));
        await buffer.DisposeAsync();

        buffer.Enqueue(Event(1));

        sender.Batches.ShouldBeEmpty();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }
}
