using Quartz;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.Quartz;

/// <summary>
/// Resolves the company/user identity for a job execution. The default implementation reads
/// <c>schematic.company.*</c> / <c>schematic.user.*</c> entries from the merged job data map (see
/// <see cref="SchematicJobDataKeys"/>); register a custom implementation to source the identity from
/// elsewhere (e.g. a tenant accessor).
/// </summary>
public interface ISchematicJobContextResolver
{
    /// <summary>
    /// Return <c>null</c> to signal the execution has no identifiable customer (a gated job is vetoed;
    /// a tracked job emits nothing).
    /// </summary>
    ValueTask<SchematicFlagContext?> ResolveAsync(IJobExecutionContext context, CancellationToken cancellationToken);
}
