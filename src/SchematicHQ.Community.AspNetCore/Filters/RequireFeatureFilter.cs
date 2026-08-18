using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchematicHQ.Community.AspNetCore.Attributes;
using SchematicHQ.Community.AspNetCore.Denial;
using SchematicHQ.Community.AspNetCore.Internal;
using SchematicHQ.Community.AspNetCore.Options;
using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Client.RulesEngine;

namespace SchematicHQ.Community.AspNetCore.Filters;

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
    private readonly ILogger<RequireFeatureFilter> _logger;

    public RequireFeatureFilter(
        ISchematicGateClient client,
        IOptions<SchematicAspNetCoreOptions> options,
        ILogger<RequireFeatureFilter> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
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
        var flagContext = await FlagContextResolution.ResolveAsync(http, options);
        if (flagContext is null)
            return Results.Unauthorized();

        http.Items[SchematicFilterItemKeys.FlagContext] = flagContext;

        CheckFlagWithEntitlementResponse response;
        try
        {
            response = await _client.CheckFlagWithEntitlementAsync(
                metadata.FlagKey, flagContext.Company, flagContext.User, http.RequestAborted);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Schematic entitlement check for flag '{FlagKey}' failed; applying {FailurePolicy}.",
                metadata.FlagKey, options.FailurePolicy);

            if (options.FailurePolicy == SchematicFailurePolicy.FailOpen)
                return await next(context);

            await WriteCheckFailureAsync(http, metadata.FlagKey);
            return Results.Empty;
        }

        if (response.Value)
        {
            http.Items[SchematicFilterItemKeys.CheckResult] = response;
            if (metadata.Track)
                http.Items[SchematicFilterItemKeys.AutoTrackFlagKey] = metadata.FlagKey;
            return await next(context);
        }

        var denial = new SchematicDenialContext(
            FeatureId: metadata.FlagKey,
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

    private static Task WriteCheckFailureAsync(HttpContext http, string flagKey)
    {
        var problem = new ProblemDetails
        {
            Title = "Entitlement check failed",
            Status = StatusCodes.Status503ServiceUnavailable,
            Detail = $"The entitlement check for feature '{flagKey}' could not be completed.",
        };
        problem.Extensions["featureId"] = flagKey;

        return Results.Problem(problem).ExecuteAsync(http);
    }
}
