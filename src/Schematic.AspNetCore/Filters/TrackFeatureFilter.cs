using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schematic.AspNetCore.Attributes;
using Schematic.AspNetCore.Internal;
using Schematic.AspNetCore.Options;
using Schematic.DependencyInjection;

namespace Schematic.AspNetCore.Filters;

/// <summary>
/// Endpoint filter that emits Schematic <c>Track</c> events on success (response status &lt; 400). Reads
/// any <see cref="ITrackFeatureMetadata"/> on the matched endpoint, plus the auto-track key stashed by
/// <see cref="RequireFeatureFilter"/> when <c>RequireFeature.Track == true</c>. No-op when neither
/// signal is present.
/// </summary>
public sealed class TrackFeatureFilter : IEndpointFilter
{
    private readonly ISchematicGateClient _client;
    private readonly IOptions<SchematicAspNetCoreOptions> _options;
    private readonly ILogger<TrackFeatureFilter> _logger;

    public TrackFeatureFilter(
        ISchematicGateClient client,
        IOptions<SchematicAspNetCoreOptions> options,
        ILogger<TrackFeatureFilter> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        // CORS preflight requests aren't user activity worth metering — never track them.
        if (HttpMethods.IsOptions(http.Request.Method))
            return await next(context);

        var endpoint = http.GetEndpoint();

        var trackEvents = endpoint?.Metadata.GetOrderedMetadata<ITrackFeatureMetadata>();
        var hasExplicitTrack = trackEvents is { Count: > 0 };
        var hasAutoTrack = http.Items.ContainsKey(SchematicFilterItemKeys.AutoTrackFlagKey);

        if (!hasAutoTrack && !hasExplicitTrack)
            return await next(context);

        var result = await next(context);

        // For minimal API endpoints `result` is an unexecuted IResult, so http.Response.StatusCode is
        // still the default at this point. Inspect the result object first; fall back to the response
        // status (which IS final for controller endpoints, where MVC has already executed the action).
        if (IsErrorResult(result, http))
            return result;

        var flagContext = http.Items[SchematicFilterItemKeys.FlagContext] as SchematicFlagContext
            ?? await FlagContextResolution.ResolveAsync(http, _options.Value);
        
        if (flagContext is null)
            return result;

        if (http.Items[SchematicFilterItemKeys.AutoTrackFlagKey] is string autoTrackEventName)
            TrackSafely(autoTrackEventName, flagContext, quantity: null);

        if (hasExplicitTrack)
        {
            foreach (var meta in trackEvents!)
                TrackSafely(meta.EventName, flagContext, meta.Quantity);
        }

        return result;
    }

    // A tracking failure must never fail a response that already succeeded.
    private void TrackSafely(string eventName, SchematicFlagContext flagContext, int? quantity)
    {
        try
        {
            _client.Track(eventName, flagContext.Company, flagContext.User, traits: new(), quantity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schematic track for event '{EventName}' failed; response is unaffected.", eventName);
        }
    }

    private static bool IsErrorResult(object? result, HttpContext http)
    {
        if (result is IStatusCodeHttpResult { StatusCode: int minimalStatus })
            return minimalStatus >= 400;
        if (result is IStatusCodeActionResult { StatusCode: int mvcStatus })
            return mvcStatus >= 400;
        return http.Response.StatusCode >= 400;
    }

}
