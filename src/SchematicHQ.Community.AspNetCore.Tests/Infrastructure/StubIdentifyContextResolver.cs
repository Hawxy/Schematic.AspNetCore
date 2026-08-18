using Microsoft.AspNetCore.Http;
using SchematicHQ.Community.AspNetCore.Resolvers;

namespace SchematicHQ.Community.AspNetCore.Tests.Infrastructure;

internal sealed class StubIdentifyContextResolver : ISchematicIdentifyContextResolver
{
    private SchematicIdentifyContext? _next;

    public StubIdentifyContextResolver(SchematicIdentifyContext? context = null) => _next = context;

    public void SetContext(SchematicIdentifyContext? context) => _next = context;

    public ValueTask<SchematicIdentifyContext?> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(_next);
}
