# SchematicHQ.Community.AspNetCore

ASP.NET Core integration for [Schematic](https://schematichq.com) entitlement management. 

This repo includes a number of packages that expand the official [SchematicHQ.Client](https://www.nuget.org/packages/SchematicHQ.Client) SDK with:
- Automated entitlement checks & tracking for ASP.NET Core routes
- Integration with `Microsoft.Extensions.AI` for usage reporting
- Time-based trait reporting with `Quartz.NET`
- DI extensions with `ILogger` wire-up
- `FusionCache` distributed caching support

Packages:

| Package | Purpose |
| --- | --- |
| `SchematicHQ.Community.DependencyInjection` | Registers the `Schematic` SDK client in DI with `ILoggerFactory` wiring, plus a [FusionCache](https://github.com/ZiggyCreatures/FusionCache)-backed `ICacheProvider` and scheduled trait reporting. |
| `SchematicHQ.Community.AspNetCore` | Feature gating, usage tracking, and identify middleware for ASP.NET Core (net8.0+). |
| `SchematicHQ.Community.Extensions.AI` | [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) middleware: meter chat token usage and gate model calls behind entitlements. |
| `SchematicHQ.Community.Extensions.Quartz` | [Quartz.NET](https://www.quartz-scheduler.net/) integration: gate and track scheduled jobs, and run trait reports on a cron schedule. |

## Quickstart

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSchematic(builder.Configuration["Schematic:ApiKey"]!);
builder.Services.AddSchematicAspNetCore();
builder.Services.AddSchematicFlagContextResolver<MyFlagContextResolver>();

var app = builder.Build();

app.MapGroup("api").AddSchematicFilters().MapMyEndpoints();
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

The SDK accepts an `ICacheProvider` for its internal caching. `SchematicHQ.Community.DependencyInjection` supplies one backed by [FusionCache](https://github.com/ZiggyCreatures/FusionCache):

```csharp
builder.Services.AddFusionCache();
builder.Services.AddSchematicFusionCache();          // or AddSchematicFusionCache("cache-name")
builder.Services.AddSchematic(apiKey);               // picks up the registered ICacheProvider
```

`AddSchematic` wires any DI-registered `ICacheProvider` into `ClientOptions.CacheProvider` unless one was set explicitly, so custom providers plug in the same way. Entries use the SDK's built-in default cache TTL (5 seconds) unless the SDK passes a per-entry TTL; pass `AddSchematicFusionCache(defaultTtl: ...)` to change it. Note: FusionCache does not support key enumeration, so the provider's `DeleteMissing` is a no-op — stale entries age out via TTL. The SDK's datastream mode (`options.UseDatastream`) relies on `DeleteMissing` to sweep deleted flags during bulk sync, so prefer the SDK's built-in Redis/local cache configuration over this provider when enabling datastream.

## Metering AI usage

`SchematicHQ.Community.Extensions.AI` plugs into the Microsoft.Extensions.AI chat pipeline:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddChatClient(sp => /* provider client */)
    .UseSchematicRequireFeature("ai-chat")           // deny before the model is invoked
    .UseSchematicUsageTracking();                    // then meter what allowed calls consume
```

Tracking reads each response's `UsageDetails` (streaming included — usage is aggregated across updates and recorded even if the consumer abandons the stream) and emits Track events: by default `ai.input-tokens` and `ai.output-tokens` with the model id as a trait, fully remappable via `options.MapUsage`. Identity comes from the ambient HTTP request's flag-context resolver; set `options.FallbackContext` for background/non-HTTP calls. Denied gating throws `SchematicFeatureDeniedException` (with `FlagKey`/`Reason`); check failures follow `options.FailurePolicy`. Tracking failures never fail the AI call.

## Gating and tracking Quartz jobs

`SchematicHQ.Community.Extensions.Quartz` applies the same gate/track model to scheduled jobs:

```csharp
builder.Services.AddSchematicQuartz();                 // options, resolver, listeners
builder.Services.AddQuartz(q =>
{
    q.AddSchematic();                                  // wires the listeners into the scheduler
});
```

Decorate job classes:

```csharp
[RequireFeature("nightly-sync")]                       // execution vetoed when not entitled
[TrackFeature("nightly-sync-runs")]                    // tracked after each successful run
public sealed class NightlySyncJob : IJob { ... }
```

The company/user identity comes from `schematic.company.*` / `schematic.user.*` entries in the merged job data map — declare them with `.UsingSchematicCompany("id", tenantId)` on the job or trigger builder, or register a custom `ISchematicJobContextResolver` (e.g. reading a tenant accessor). A vetoed execution skips that one firing; the trigger keeps its schedule. Check failures follow `AddSchematicQuartz(o => o.FailurePolicy = ...)`, and tracking failures never fail the job. A job that fans out over many tenants internally cannot be vetoed per-tenant — call `ISchematicGateClient` inside the loop instead.

## Reporting traits on a schedule

Traits hold stateful facts (seat counts, storage used) that entitlements compare against, and are usually computed from your own database. A report is a catalog (which tenants?) plus a source (what are this tenant's traits?):

```csharp
public sealed class TenantCatalog(CatalogDbContext db) : ISchematicTenantCatalog
{
    public IAsyncEnumerable<string> GetTenantIdsAsync(TraitReportContext context, CancellationToken ct)
        => db.Tenants.Select(t => t.Id).AsAsyncEnumerable();
}

public sealed class SeatSource(ITenantDbContextFactory dbFactory) : ISchematicTraitReportSource
{
    public async Task<CompanyTraitReport?> GetReportAsync(string tenantId, TraitReportContext context, CancellationToken ct)
    {
        await using var db = dbFactory.CreateForTenant(tenantId);
        return new(Keys: new() { ["id"] = tenantId },
                   Traits: new() { ["seats"] = await db.Users.CountAsync(ct) });
    }
}

builder.Services.AddSchematicTraitReport<TenantCatalog, SeatSource>("seats", o => o.Cron = "0 0 3 * * ?");
```

The source receives each tenant id and handles tenancy itself (a context factory, its own scope — whatever your app uses); return `null` to skip a tenant. Tenants are processed with bounded parallelism, so acquire per-tenant resources inside the call. With `AddSchematicQuartz`, every report that sets a cron runs on that schedule (missed runs fire once on startup; trait upserts are last-write-wins, so re-runs are safe). One failing tenant is logged and retried on the next run without sinking the rest. Reports without a cron — or apps not using Quartz — run on demand via `ISchematicTraitReportRunner.RunReportAsync("seats")`.

## Testing your app

The filters call Schematic through the `ISchematicGateClient` seam. Replace it in tests to run without a live Schematic backend, or register your own implementation to add caching or batching.

## License

Apache-2.0
