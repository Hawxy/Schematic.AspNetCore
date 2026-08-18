using Microsoft.AspNetCore.Http;
using Schematic.DependencyInjection;

namespace Schematic.AspNetCore.Resolvers;

public interface ISchematicFlagContextResolver
{
    /// <summary>
    /// Resolve the Schematic flag request context. Return <c>null</c> to signal the request has no identifiable
    /// customer (the entitlement filter will respond <c>401 Unauthorized</c>).
    /// </summary>
    ValueTask<SchematicFlagContext?> ResolveAsync(HttpContext context, CancellationToken cancellationToken);
}