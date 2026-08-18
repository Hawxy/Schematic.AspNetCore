using Quartz;
using Schematic.DependencyInjection;

namespace Schematic.Extensions.Quartz;

internal sealed class JobDataMapContextResolver : ISchematicJobContextResolver
{
    public ValueTask<SchematicFlagContext?> ResolveAsync(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var company = new Dictionary<string, string>();
        var user = new Dictionary<string, string>();

        foreach (var entry in context.MergedJobDataMap)
        {
            if (entry.Value?.ToString() is not { } value)
                continue;

            if (entry.Key.StartsWith(SchematicJobDataKeys.CompanyPrefix, StringComparison.Ordinal))
                company[entry.Key[SchematicJobDataKeys.CompanyPrefix.Length..]] = value;
            else if (entry.Key.StartsWith(SchematicJobDataKeys.UserPrefix, StringComparison.Ordinal))
                user[entry.Key[SchematicJobDataKeys.UserPrefix.Length..]] = value;
        }

        if (company.Count == 0 && user.Count == 0)
            return ValueTask.FromResult<SchematicFlagContext?>(null);

        return ValueTask.FromResult<SchematicFlagContext?>(new SchematicFlagContext(company, user));
    }
}
