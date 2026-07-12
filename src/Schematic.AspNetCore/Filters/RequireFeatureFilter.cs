using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schematic.AspNetCore.Attributes;
using Schematic.AspNetCore.Denial;
using Schematic.AspNetCore.Internal;
using Schematic.AspNetCore.Options;
using Schematic.AspNetCore.Resolvers;

namespace Schematic.AspNetCore.Filters;

internal static class SchematicFilterItemKeys
{
    public const string CheckResult = "__schematic.check.result";
    public const string AutoTrackFlagKey = "__schematic.check.auto_track";
    public const string FlagContext = "__schematic.flag.context";
}

/// <summary>
/// Endpoint filter that enforces <see cref="IRequireFeatureMetadata"/> on the matched endpoint. No-op when
/// the endpoint carries no such metadata. Stashes the SDK response in <c>HttpContext.Items</c> so a
/// downstream <see cref="TrackFeatureFilter"/> can re-use it without re-checking.
/// </summary>
public sealed class RequireFeatureFilter : IEndpointFilter
{
    private readonly ISchematicGateClient _client;
    private readonly IOptions<SchematicAspNetCoreOptions> _options;

    public RequireFeatureFilter(ISchematicGateClient client, IOptions<SchematicAspNetCoreOptions> options)
    {
        _client = client;
        _options = options;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        // CORS preflight requests carry no user intent. Gating would break CORS for any browser client
        // whose flag is denied. UseCors() typically short-circuits these before routing, but skip
        // defensively in case the consumer hasn't wired CORS or has a route that matches OPTIONS.
        if (HttpMethods.IsOptions(http.Request.Method))
            return await next(context);

        var metadata = http.GetEndpoint()?.Metadata.GetMetadata<IRequireFeatureMetadata>();
        if (metadata is null)
            return await next(context);

        var options = _options.Value;
        var flagContext = await ResolveFlagContextAsync(http, options);
        if (flagContext is null)
            return Results.Unauthorized();

        http.Items[SchematicFilterItemKeys.FlagContext] = flagContext;

        var response = await _client.CheckFlagWithEntitlementAsync(metadata.FlagKey, flagContext.Company, flagContext.User);

        if (response.Value)
        {
            http.Items[SchematicFilterItemKeys.CheckResult] = response;
            if (metadata.Track)
                http.Items[SchematicFilterItemKeys.AutoTrackFlagKey] = metadata.FlagKey;
            return await next(context);
        }

        var denial = new SchematicDenialContext(
            FeatureId: metadata.FlagKey,
            RequestedUsage: metadata.RequestedUsage,
            RequestedValue: metadata.RequestedValue,
            Reason: response.Reason);

        if (options.OnDenied is { } onDenied)
        {
            await onDenied(http, denial);
        }
        else
        {
            await DefaultDenialResponseWriter.WriteAsync(http, denial);
        }

        return Results.Empty;
    }

    private async ValueTask<SchematicFlagContext?> ResolveFlagContextAsync(HttpContext http, SchematicAspNetCoreOptions options)
    {
        var resolver = http.RequestServices.GetService<ISchematicFlagContextResolver>();
        if (resolver is not null)
            return await resolver.ResolveAsync(http, http.RequestAborted);

        if (options.ResolveContext is { } resolve)
            return await resolve(http);

        return null;
    }

}
