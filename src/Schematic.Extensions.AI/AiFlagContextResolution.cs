using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schematic.AspNetCore.Internal;
using Schematic.AspNetCore.Options;
using Schematic.AspNetCore.Resolvers;

namespace Schematic.Extensions.AI;

/// <summary>
/// Resolves the Schematic identity for an AI call: the ambient HTTP request's flag context (same
/// resolution as the endpoint filters) when available, otherwise <see cref="SchematicAiOptions.FallbackContext"/>.
/// </summary>
internal static class AiFlagContextResolution
{
    public static async ValueTask<SchematicFlagContext?> ResolveAsync(
        IHttpContextAccessor? httpContextAccessor,
        SchematicAiOptions options)
    {
        if (httpContextAccessor?.HttpContext is { } http)
        {
            var aspNetCoreOptions = http.RequestServices
                .GetRequiredService<IOptions<SchematicAspNetCoreOptions>>().Value;
            if (await FlagContextResolution.ResolveAsync(http, aspNetCoreOptions) is { } context)
                return context;
        }

        return options.FallbackContext;
    }
}
