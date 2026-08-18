using Microsoft.AspNetCore.Http;
using Schematic.AspNetCore.Resolvers;
using Schematic.DependencyInjection;

namespace Schematic.AspNetCore.Tests.Infrastructure;

internal sealed class StubFlagContextResolver : ISchematicFlagContextResolver
{
    public static readonly SchematicFlagContext DefaultContext = new(
        Company: new() { ["id"] = "company_test" },
        User: new() { ["id"] = "user_test" });

    private SchematicFlagContext? _next = DefaultContext;

    public void SetContext(SchematicFlagContext? context) => _next = context;

    public void Reset() => _next = DefaultContext;

    public ValueTask<SchematicFlagContext?> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(_next);
}
