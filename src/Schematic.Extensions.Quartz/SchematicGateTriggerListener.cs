using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Schematic.DependencyInjection;

namespace Schematic.Extensions.Quartz;

/// <summary>
/// Vetoes executions of jobs decorated with <see cref="RequireFeatureAttribute"/> when the Schematic
/// entitlement check denies. A veto skips the single execution; the trigger keeps its schedule.
/// </summary>
internal sealed class SchematicGateTriggerListener : ITriggerListener
{
    private readonly ISchematicGateClient _client;
    private readonly ISchematicJobContextResolver _resolver;
    private readonly IOptions<SchematicQuartzOptions> _options;
    private readonly ILogger<SchematicGateTriggerListener> _logger;

    public SchematicGateTriggerListener(
        ISchematicGateClient client,
        ISchematicJobContextResolver resolver,
        IOptions<SchematicQuartzOptions> options,
        ILogger<SchematicGateTriggerListener> logger)
    {
        _client = client;
        _resolver = resolver;
        _options = options;
        _logger = logger;
    }

    public string Name => "schematic-gate";

    public async Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var attribute = context.JobDetail.JobType.GetCustomAttribute<RequireFeatureAttribute>(inherit: true);
        if (attribute is null)
            return false;

        var flagContext = await _resolver.ResolveAsync(context, cancellationToken);
        if (flagContext is null)
        {
            _logger.LogWarning(
                "No Schematic context resolved for gated job {JobKey}; vetoing execution of flag '{FlagKey}'.",
                context.JobDetail.Key, attribute.FlagKey);
            return true;
        }

        try
        {
            var response = await _client.CheckFlagWithEntitlementAsync(
                attribute.FlagKey, flagContext.Company, flagContext.User, cancellationToken);

            if (!response.Value)
            {
                _logger.LogInformation(
                    "Schematic flag '{FlagKey}' denied for job {JobKey}; execution vetoed. Reason: {Reason}",
                    attribute.FlagKey, context.JobDetail.Key, response.Reason);
                return true;
            }

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var policy = _options.Value.FailurePolicy;
            _logger.LogError(ex,
                "Schematic entitlement check for flag '{FlagKey}' on job {JobKey} failed; applying {FailurePolicy}.",
                attribute.FlagKey, context.JobDetail.Key, policy);
            return policy == SchematicFailurePolicy.FailClosed;
        }
    }

    public Task TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
