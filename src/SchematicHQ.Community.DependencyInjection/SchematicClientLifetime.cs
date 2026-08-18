using Microsoft.Extensions.Logging;

namespace SchematicHQ.Community.DependencyInjection;

/// <summary>
/// Flushes the Schematic client's buffered Track/Identify events when the service provider is disposed
/// (i.e. on host shutdown). The client SDK buffers events and only sends them periodically; without a
/// <c>Shutdown()</c> call, events buffered since the last flush are lost when the process exits.
/// </summary>
internal sealed class SchematicClientLifetime : IAsyncDisposable
{
    // Disposal has no cancellation token, so bound the flush ourselves to never hang shutdown.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<SchematicClientLifetime> _logger;

    public SchematicClientLifetime(ILogger<SchematicClientLifetime> logger)
    {
        _logger = logger;
    }

    /// <summary>Set by the client singleton factory; stays null when the client is never resolved.</summary>
    public SchematicHQ.Client.Schematic? Client { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (Client is not { } client)
            return;

        try
        {
            await client.Shutdown().WaitAsync(ShutdownTimeout);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Schematic client shutdown flush did not complete within {Timeout}; buffered events may be lost.",
                ShutdownTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schematic client shutdown flush failed; buffered events may be lost.");
        }
    }
}
