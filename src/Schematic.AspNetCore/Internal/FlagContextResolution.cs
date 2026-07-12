using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Schematic.AspNetCore.Options;
using Schematic.AspNetCore.Resolvers;

namespace Schematic.AspNetCore.Internal;

/// <summary>
/// Resolves the flag context for a request: a DI-registered <see cref="ISchematicFlagContextResolver"/>
/// wins over the <see cref="SchematicAspNetCoreOptions.ResolveContext"/> delegate.
/// </summary>
internal static class FlagContextResolution
{
    public static async ValueTask<SchematicFlagContext?> ResolveAsync(HttpContext http, SchematicAspNetCoreOptions options)
    {
        var resolver = http.RequestServices.GetService<ISchematicFlagContextResolver>();
        if (resolver is not null)
            return await resolver.ResolveAsync(http, http.RequestAborted);

        if (options.ResolveContext is { } resolve)
            return await resolve(http);

        return null;
    }
}
