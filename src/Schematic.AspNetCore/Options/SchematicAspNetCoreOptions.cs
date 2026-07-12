using Microsoft.AspNetCore.Http;
using Schematic.AspNetCore.Denial;
using Schematic.AspNetCore.Resolvers;

namespace Schematic.AspNetCore.Options;

public sealed class SchematicAspNetCoreOptions
{
    /// <summary>
    /// Simple delegate-based shortcut for resolving the Schematic flag context from an incoming request.
    /// Ignored when an <see cref="ISchematicFlagContextResolver"/> is registered in DI.
    /// </summary>
    public Func<HttpContext, ValueTask<SchematicFlagContext?>>? ResolveContext { get; set; }

    /// <summary>
    /// Custom denial response writer. When set, the filter invokes this delegate instead of the default
    /// ProblemDetails implementation. The delegate is responsible for writing a complete response.
    /// </summary>
    public Func<HttpContext, SchematicDenialContext, Task>? OnDenied { get; set; }
}
