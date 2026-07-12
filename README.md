# Schematic.AspNetCore

ASP.NET Core integration for [Schematic](https://schematichq.com) entitlement management. Gate endpoints behind feature flags and entitlements, track usage events, and identify customers — declaratively, on top of the official [SchematicHQ.Client](https://www.nuget.org/packages/SchematicHQ.Client) SDK.

Two packages:

| Package | Purpose |
| --- | --- |
| `Schematic.DependencyInjection` | Registers the `Schematic` SDK client in DI with `ILoggerFactory` wiring. |
| `Schematic.AspNetCore` | Feature gating, usage tracking, and identify middleware for ASP.NET Core (net8.0+). |

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

## Testing your app

The filters call Schematic through the `ISchematicGateClient` seam. Replace it in tests to run without a live Schematic backend, or register your own implementation to add caching or batching.

## License

Apache-2.0
