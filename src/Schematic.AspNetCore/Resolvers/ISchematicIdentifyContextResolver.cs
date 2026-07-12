using Microsoft.AspNetCore.Http;

namespace Schematic.AspNetCore.Resolvers;

public interface ISchematicIdentifyContextResolver
{
    ValueTask<SchematicIdentifyContext?> ResolveAsync(HttpContext context, CancellationToken cancellationToken);
}