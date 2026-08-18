using System.Reflection;
using Microsoft.Extensions.Logging;
using Quartz;
using Schematic.DependencyInjection;

namespace Schematic.Extensions.Quartz;

/// <summary>
/// Emits Schematic <c>Track</c> events for jobs decorated with <see cref="TrackFeatureAttribute"/> after
/// each successful execution. Vetoed and faulted executions are not tracked.
/// </summary>
internal sealed class SchematicTrackJobListener : IJobListener
{
    private readonly ISchematicGateClient _client;
    private readonly ISchematicJobContextResolver _resolver;
    private readonly ILogger<SchematicTrackJobListener> _logger;

    public SchematicTrackJobListener(
        ISchematicGateClient client,
        ISchematicJobContextResolver resolver,
        ILogger<SchematicTrackJobListener> logger)
    {
        _client = client;
        _resolver = resolver;
        _logger = logger;
    }

    public string Name => "schematic-track";

    public async Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        if (jobException is not null)
            return;

        var attributes = context.JobDetail.JobType.GetCustomAttributes<TrackFeatureAttribute>(inherit: true).ToArray();
        if (attributes.Length == 0)
            return;

        // A tracking failure must never fail an execution that already succeeded.
        try
        {
            var flagContext = await _resolver.ResolveAsync(context, cancellationToken);
            if (flagContext is null)
            {
                _logger.LogDebug(
                    "No Schematic context resolved for tracked job {JobKey}; skipping track.",
                    context.JobDetail.Key);
                return;
            }

            foreach (var attribute in attributes)
            {
                _client.Track(
                    attribute.EventName, flagContext.Company, flagContext.User,
                    traits: new(), attribute.EffectiveQuantity);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Schematic track for job {JobKey} failed; execution result is unaffected.",
                context.JobDetail.Key);
        }
    }

    public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
