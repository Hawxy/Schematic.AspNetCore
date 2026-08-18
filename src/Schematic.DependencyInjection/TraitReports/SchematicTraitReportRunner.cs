using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Schematic.DependencyInjection;

internal sealed class SchematicTraitReportRunner : ISchematicTraitReportRunner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEnumerable<SchematicTraitReportRegistration> _registrations;
    private readonly ISchematicTraitPusher _pusher;
    private readonly ILogger<SchematicTraitReportRunner> _logger;

    public SchematicTraitReportRunner(
        IServiceScopeFactory scopeFactory,
        IEnumerable<SchematicTraitReportRegistration> registrations,
        ISchematicTraitPusher pusher,
        ILogger<SchematicTraitReportRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _registrations = registrations;
        _pusher = pusher;
        _logger = logger;
    }

    public async Task<TraitReportResult> RunReportAsync(string reportName, CancellationToken cancellationToken = default)
    {
        var registration = _registrations.FirstOrDefault(r => r.Name == reportName)
            ?? throw new InvalidOperationException($"No Schematic trait report named '{reportName}' is registered.");

        var context = new TraitReportContext(registration.Name, DateTimeOffset.UtcNow);
        var succeeded = 0;
        var failed = 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var catalog = (ISchematicTenantCatalog)scope.ServiceProvider.GetRequiredService(registration.CatalogType);
        var source = (ISchematicTraitReportSource)scope.ServiceProvider.GetRequiredService(registration.SourceType);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = registration.Options.MaxDegreeOfParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(catalog.GetTenantIdsAsync(context, cancellationToken), parallelOptions,
            async (tenantId, token) =>
            {
                try
                {
                    var report = await source.GetReportAsync(tenantId, context, token);
                    if (report is null)
                        return;

                    await _pusher.PushAsync(report, token);
                    Interlocked.Increment(ref succeeded);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                // One failing tenant must not sink the rest of the report; it is retried on the next run.
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    _logger.LogError(ex,
                        "Schematic trait report '{ReportName}' failed for tenant '{TenantId}'.",
                        registration.Name, tenantId);
                }
            });

        _logger.LogInformation(
            "Schematic trait report '{ReportName}' completed: {Succeeded} succeeded, {Failed} failed.",
            registration.Name, succeeded, failed);

        return new TraitReportResult(succeeded, failed);
    }
}
