using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SchematicHQ.Community.AspNetCore.Options;
using SchematicHQ.Community.AspNetCore.Resolvers;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.AspNetCore.Internal;

/// <summary>
/// Resolves the flag context for a request: a DI-registered <see cref="ISchematicFlagContextResolver"/>
/// wins over the <see cref="SchematicAspNetCoreOptions.ResolveContext"/> delegate. Public so companion
/// packages (e.g. SchematicHQ.Community.Extensions.AI) resolve the same identity as the filters; most consumers
/// should not need it directly.
/// </summary>
public static class FlagContextResolution
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
