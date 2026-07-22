# Schematic.AspNetCore

ASP.NET Core integration for [Schematic](https://schematichq.com) entitlement management. Gate endpoints behind feature flags and entitlements, track usage events, and identify customers — declaratively, on top of the official [SchematicHQ.Client](https://www.nuget.org/packages/SchematicHQ.Client) SDK.

Two packages:

| Package | Purpose |
| --- | --- |
| `Schematic.DependencyInjection` | Registers the `Schematic` SDK client in DI with `ILoggerFactory` wiring and shutdown event flushing, plus a [FusionCache](https://github.com/ZiggyCreatures/FusionCache)-backed `ICacheProvider`. |
| `Schematic.AspNetCore` | Feature gating, usage tracking, and identify middleware for ASP.NET Core (net8.0+). |
| `Schematic.Extensions.AI` | [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) middleware: meter chat token usage and gate model calls behind entitlements. |

## Quickstart

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSchematic(builder.Configuration["Schematic:ApiKey"]!);
builder.Services.AddSchematicAspNetCore();
builder.Services.AddSchematicFlagContextResolver<MyFlagContextResolver>();

var app = builder.Build();

app.MapGroup(string.Empty).AddSchematicFilters().MapMyEndpoints();
app.MapControllers().AddSchematicFilters();

app.Run();
```

Tell Schematic who is making the request by implementing a resolver:

```csharp
public sealed class MyFlagContextResolver : ISchematicFlagContextResolver
{
    public ValueTask<SchematicFlagContext?> ResolveAsync(HttpContext context, CancellationToken ct)
    {
        var companyId = context.User.FindFirstValue("company_id");
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (companyId is null || userId is null)
            return ValueTask.FromResult<SchematicFlagContext?>(null); // gate responds 401

        return ValueTask.FromResult<SchematicFlagContext?>(new SchematicFlagContext(
            Company: new() { ["id"] = companyId },
            User: new() { ["id"] = userId }));
    }
}
```

For simple cases, a delegate works instead of a resolver class: `AddSchematicAspNetCore(o => o.ResolveContext = http => ...)`.

The SDK buffers Track/Identify events and sends them periodically. `AddSchematic` registers a lifetime hook that calls `Schematic.Shutdown()` when the host's service provider is disposed, so events buffered at shutdown are flushed instead of lost (bounded at 10 seconds so a broken connection cannot hang shutdown).

## Gating endpoints

Minimal APIs:

```csharp
app.MapGet("/reports", GetReports)
   .RequireFeature("advanced-reports");              // 403 ProblemDetails when not entitled

app.MapPost("/exports", CreateExport)
   .RequireFeature("exports", track: true);          // also tracks an "exports" event on success
```

Controllers:

```csharp
[RequireFeature("advanced-reports")]
[HttpGet("reports")]
public IActionResult GetReports() => ...;
```

A denied check returns RFC 7807 ProblemDetails with status 403, plus `featureId` and `accessDeniedReason` extension fields. Customize with `options.OnDenied`.

## Tracking usage

```csharp
app.MapPost("/messages", SendMessage)
   .TrackFeature("messages-sent", quantity: 1);      // controllers: [TrackFeature("messages-sent")]
```

Events are emitted only for successful (status < 400) responses, and a tracking failure never fails the response. `RequireFeature(..., track: true)` reuses the entitlement check result, so the SDK is called once per request.

## Identifying customers

```csharp
builder.Services.AddSchematicIdentifyContextResolver<MyIdentifyResolver>();
...
app.UseSchematicIdentify();
```

Calls `Schematic.Identify` for each request whose resolver returns an identity. Set `options.IdentifyDeduplicationWindow` to send at most one Identify per identity per window.

## Receiving webhooks

Verify inbound [Schematic webhooks](https://docs.schematichq.com/integrations/webhooks) with the signing secret from the dashboard:

```csharp
builder.Services.AddSchematicAspNetCore(o => o.WebhookSecret = builder.Configuration["Schematic:WebhookSecret"]);
...
app.MapPost("/webhooks/schematic", (JsonElement payload) => Results.Ok())
   .RequireSchematicWebhookSignature();
```

The filter validates the `X-Schematic-Webhook-Signature` / `X-Schematic-Webhook-Timestamp` headers against the raw request body (via the SDK's `WebhookVerifier`) before the endpoint runs, responding 401 ProblemDetails when they are missing or invalid. The body remains readable by the endpoint afterwards.

## Options

```csharp
builder.Services.AddSchematicAspNetCore(options =>
{
    // How the gate responds when the entitlement check itself fails (network/SDK error).
    // FailClosed (default) => 503 ProblemDetails. FailOpen => request proceeds.
    options.FailurePolicy = SchematicFailurePolicy.FailClosed;

    // Custom denial response.
    options.OnDenied = (http, denial) => Results.Json(new { error = denial.Reason }, statusCode: 402).ExecuteAsync(http);

    // Send at most one Identify per identity in this window (default: every request).
    options.IdentifyDeduplicationWindow = TimeSpan.FromMinutes(5);
});
```

## Caching with FusionCache

The SDK accepts an `ICacheProvider` for its internal caching. `Schematic.DependencyInjection` supplies one backed by [FusionCache](https://github.com/ZiggyCreatures/FusionCache):

```csharp
builder.Services.AddFusionCache();
builder.Services.AddSchematicFusionCache();          // or AddSchematicFusionCache("cache-name")
builder.Services.AddSchematic(apiKey);               // picks up the registered ICacheProvider
```

`AddSchematic` wires any DI-registered `ICacheProvider` into `ClientOptions.CacheProvider` unless one was set explicitly, so custom providers plug in the same way. Entries use the SDK's built-in default cache TTL (5 seconds) unless the SDK passes a per-entry TTL; pass `AddSchematicFusionCache(defaultTtl: ...)` to change it. Note: FusionCache does not support key enumeration, so the provider's `DeleteMissing` is a no-op — stale entries age out via TTL. The SDK's datastream mode (`options.UseDatastream`) relies on `DeleteMissing` to sweep deleted flags during bulk sync, so prefer the SDK's built-in Redis/local cache configuration over this provider when enabling datastream.

## Metering AI usage

`Schematic.Extensions.AI` plugs into the Microsoft.Extensions.AI chat pipeline:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddChatClient(sp => /* provider client */)
    .UseSchematicRequireFeature("ai-chat")           // deny before the model is invoked
    .UseSchematicUsageTracking();                    // then meter what allowed calls consume
```

Tracking reads each response's `UsageDetails` (streaming included — usage is aggregated across updates and recorded even if the consumer abandons the stream) and emits Track events: by default `ai.input-tokens` and `ai.output-tokens` with the model id as a trait, fully remappable via `options.MapUsage`. Identity comes from the ambient HTTP request's flag-context resolver; set `options.FallbackContext` for background/non-HTTP calls. Denied gating throws `SchematicFeatureDeniedException` (with `FlagKey`/`Reason`); check failures follow `options.FailurePolicy`. Tracking failures never fail the AI call.

## Testing your app

The filters call Schematic through the `ISchematicGateClient` seam. Replace it in tests to run without a live Schematic backend, or register your own implementation to add caching or batching.

## License

Apache-2.0
