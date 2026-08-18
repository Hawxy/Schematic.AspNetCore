using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SchematicHQ.Community.AspNetCore;

namespace SchematicHQ.Community.AspNetCore.TestApp;

public static class TestEndpoints
{
    public const string MinimalAllowedFlag = "test.minimal.gate";
    public const string MinimalAutoTrackFlag = "test.minimal.auto_track";
    public const string ControllerFlag = "test.controller.gate";
    public const string ExplicitTrackEvent = "test.minimal.event";
    public const string ControllerTrackEvent = "test.controller.event";
    public const string WebhookSecret = "test-webhook-secret";

    public static IEndpointRouteBuilder MapTestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/min/gate", () => Results.Ok(new { ok = true }))
           .RequireFeature(MinimalAllowedFlag);

        app.MapGet("/min/auto-track", () => Results.Ok(new { ok = true }))
           .RequireFeature(MinimalAutoTrackFlag, track: true);

        app.MapGet("/min/track", () => Results.Ok(new { ok = true }))
           .TrackFeature(ExplicitTrackEvent, quantity: 7);

        app.MapGet("/min/track-on-error", () => Results.StatusCode(StatusCodes.Status500InternalServerError))
           .TrackFeature(ExplicitTrackEvent);

        app.MapGet("/min/no-meta", () => Results.Ok(new { ok = true }));

        // Echoes the body back so tests can prove it is still readable after signature verification.
        app.MapPost("/webhook", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            return Results.Text(await reader.ReadToEndAsync());
        }).RequireSchematicWebhookSignature();

        // Maps GET *and* OPTIONS to the same handler so we can prove the filters skip OPTIONS preflight
        // even when the route would otherwise match it.
        app.MapMethods("/min/cors-preflight", new[] { "GET", "OPTIONS" }, () => Results.Ok(new { ok = true }))
           .RequireFeature(MinimalAllowedFlag, track: true)
           .TrackFeature(ExplicitTrackEvent);

        return app;
    }
}
