using Microsoft.AspNetCore.Http;

namespace SchematicHQ.Community.AspNetCore.Resolvers;

public interface ISchematicIdentifyContextResolver
{
    ValueTask<SchematicIdentifyContext?> ResolveAsync(HttpContext context, CancellationToken cancellationToken);
}