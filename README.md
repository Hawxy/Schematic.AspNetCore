# SchematicHQ.AspNetCore

ASP.NET Core integration for [Schematic](https://schematichq.com) entitlement management. 

This repo includes a number of packages that expand the official [SchematicHQ.Client](https://www.nuget.org/packages/SchematicHQ.Client) SDK with:
- Automated entitlement checks & tracking for ASP.NET Core routes
- Integration with `Microsoft.Extensions.AI` for usage reporting
- Time-based trait reporting with `Quartz.NET`
- DI extensions with `ILogger` wire-up
- `FusionCache` distributed caching support
- A test double for the gate client

Packages:

| Package | Purpose |
| --- | --- |
| `SchematicHQ.Community.DependencyInjection` | Registers the `Schematic` SDK client in DI with `ILoggerFactory` wiring, plus a [FusionCache](https://github.com/ZiggyCreatures/FusionCache)-backed `ICacheProvider`, fail-open defaults and shadow mode. |
| `SchematicHQ.Community.AspNetCore` | Feature gating, usage tracking, entitlement snapshots and identify middleware for ASP.NET Core (net8.0+). |
| `SchematicHQ.Community.Extensions.AI` | [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) middleware: meter chat token usage and gate model calls behind entitlements. |
| `SchematicHQ.Community.Extensions.Quartz` | [Quartz.NET](https://www.quartz-scheduler.net/) integration (Quartz 4, net10.0+): gate and track scheduled jobs, and run trait reports on a cron schedule. |
| `SchematicHQ.Community.Testing` | `FakeSchematicGateClient`: canned entitlement answers, recorded calls, and a one-line swap into a test host. |

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

The SDK buffers Track/Identify events and sends them periodically. 

`AddSchematic` registers a lifetime hook that calls `Schematic.Shutdown()` when the host's service provider is disposed, so events buffered at shutdown are flushed instead of lost (bounded at 10 seconds so a broken connection cannot hang shutdown).

### Gating endpoints

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

### Tracking usage

```csharp
app.MapPost("/messages", SendMessage)
   .TrackFeature("messages-sent", quantity: 1);      // controllers: [TrackFeature("messages-sent")]
```

Events are emitted only for successful (status < 400) responses, and a tracking failure will never fail the response. `RequireFeature(..., track: true)` reuses the entitlement check result, so the SDK is called once per request.

### Evaluating several flags at once

A gate answers one flag per request. When an endpoint needs many answers in one go — to return the set of features a caller may use, or to branch on several entitlements inside a handler — `ISchematicEntitlementSnapshotProvider` evaluates a set of flags for an identity through the same client and resolver the gates use:

```csharp
builder.Services.AddSchematicEntitlementSnapshots(o => o.FailurePolicy = SchematicFailurePolicy.FailOpen);

app.MapGet("/features", async (HttpContext http, ISchematicEntitlementSnapshotProvider snapshots) =>
{
    var snapshot = await snapshots.GetAsync(http, ["advanced-reports", "exports", "ai-chat"]);
    if (snapshot is null)
        return Results.Unauthorized();                                  // no identity, as the gate would answer

    return Results.Ok(new
    {
        advancedReports = snapshot.IsEntitled("advanced-reports"),
        exports = snapshot.IsEntitled("exports"),
        aiCredits = snapshot["ai-chat"].Entitlement?.CreditSettled,
    });
});
```

`GetAsync(SchematicFlagContext, flagKeys)` evaluates an identity you supply yourself. Each entry carries the value, Schematic's reason and the backing entitlement (allocation, usage, credit balance). A check that throws does not fail the snapshot: its entry takes the failure policy's value and is marked `CheckFailed`. The request overload returns `null` when no identity resolves, which is when the gates would not run either. The snapshot has no cache of its own: each check goes through the SDK, whose flag cache (see [Caching with FusionCache](#caching-with-fusioncache)) serves repeat checks for the same flag and identity.

### Identifying customers

```csharp
builder.Services.AddSchematicIdentifyContextResolver<MyIdentifyResolver>();
...
app.UseSchematicIdentify();
```

Calls `Schematic.Identify` for each request whose resolver returns an identity. Set `options.IdentifyDeduplicationWindow` to send at most one Identify per identity per window.

### Receiving webhooks

Verify inbound [Schematic webhooks](https://docs.schematichq.com/integrations/webhooks) with the signing secret from the dashboard:

```csharp
builder.Services.AddSchematicAspNetCore(o => o.WebhookSecret = builder.Configuration["Schematic:WebhookSecret"]);
...
app.MapPost("/webhooks/schematic", (JsonElement payload) => Results.Ok())
   .RequireSchematicWebhookSignature();
```

The filter validates the `X-Schematic-Webhook-Signature` / `X-Schematic-Webhook-Timestamp` headers against the raw request body (via the SDK's `WebhookVerifier`) before the endpoint runs, responding 401 ProblemDetails when they are missing or invalid. The body remains readable by the endpoint afterwards.

### Options

```csharp
builder.Services.AddSchematicAspNetCore(options =>
{
    // How the gate responds when the entitlement check *throws*. FailClosed (default) => 503
    // ProblemDetails. FailOpen => request proceeds. See "Failing open" below for what an outage
    // actually does, because the SDK client does not throw.
    options.FailurePolicy = SchematicFailurePolicy.FailClosed;

    // Custom denial response.
    options.OnDenied = (http, denial) => Results.Json(new { error = denial.Reason }, statusCode: 402).ExecuteAsync(http);

    // Send at most one Identify per identity in this window (default: every request).
    options.IdentifyDeduplicationWindow = TimeSpan.FromMinutes(5);
});
```

### Failing open

`FailurePolicy` only governs exceptions, and the SDK's flag check never throws: when Schematic cannot be reached it answers the flag's entry in `ClientOptions.FlagDefaults`, which is `false` for any flag not listed. So with the SDK client an outage denies every gated feature whatever `FailurePolicy` says, and the setting that decides what an outage does is the flag default:

```csharp
builder.Services.AddSchematic(apiKey, o => o.FailOpenFor("advanced-reports", "exports", "ai-chat"));
```

`FailOpenFor` sets those flags' defaults to `true`. A genuine deny is a successful evaluation and is unaffected. `FailurePolicy` still matters for a custom `ISchematicGateClient` that does throw, and for the credit-lease middleware, whose lease calls go through the API and can fail.

### Shadow mode

Between "gating is wired" and "gating is enforced", run with every denial logged and allowed:

```csharp
builder.Services.AddSchematic(apiKey);
builder.Services.AddSchematicShadowMode(builder.Configuration.GetValue<bool>("Schematic:ShadowMode"));
```

`AddSchematicShadowMode` wraps the registered gate client (call it after `AddSchematic`, `AddSchematicNoOp` or your own registration). A denied check is logged at Warning as `Schematic shadow mode: would deny flag '...' for company ... : reason` and answered as allowed; tracking, identify and leases pass straight through. Ship a gated build with it on, read the logs, fix the plans that would have blocked a company that should have access, then turn it off. It is a rollout control, not a failure policy: a real deny is a successful evaluation, so no fail-open setting covers a plan that is missing a feature.

## Caching with FusionCache

The SDK accepts an `ICacheProvider` for its internal caching. `SchematicHQ.Community.DependencyInjection` supplies one backed by [FusionCache](https://github.com/ZiggyCreatures/FusionCache):

```csharp
builder.Services.AddFusionCache();
builder.Services.AddSchematicFusionCache();          // or AddSchematicFusionCache("cache-name")
builder.Services.AddSchematic(apiKey);               // picks up the registered ICacheProvider
```

`AddSchematic` wires any DI-registered `ICacheProvider` into `ClientOptions.CacheProvider` unless one was set explicitly, so custom providers plug in the same way. Entries use the SDK's built-in default cache TTL (5 seconds) unless the SDK passes a per-entry TTL, pass `AddSchematicFusionCache(defaultTtl: ...)` to change it. 

Note: FusionCache does not support key enumeration, so the provider's `DeleteMissing` is a no-op. The SDK's Datastream functionality (`options.UseDatastream`), where a sidecar is used to cache entitlement state, should not be used when utilizing FusionCache or a custom distributed cache as it's redundant and may cause issues.

## Metering AI usage

`SchematicHQ.Community.Extensions.AI` plugs into the Microsoft.Extensions.AI chat pipeline:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddChatClient(sp => /* provider client */)
    .UseSchematicRequireFeature("ai-chat")           // deny before the model is invoked
    .UseSchematicUsageTracking();                    // then meter what allowed calls consume
```

Tracking reads each response's `UsageDetails` (streaming is included as usage is aggregated across updates and recorded even if the consumer abandons the stream). 

By default, track events take  `ai.input-tokens` and `ai.output-tokens` as an input and pass the the model id as a trait, fully remappable via `options.MapUsage`. Identity comes from the ambient HTTP request's flag-context resolver, set `options.FallbackContext` for background/non-HTTP calls. Denied gating throws `SchematicFeatureDeniedException` (with `FlagKey`/`Reason`). Check failures follow `options.FailurePolicy`. Tracking failures will never fail the AI call.

Anything the provider reports in `UsageDetails.AdditionalCounts` (such as cache reads and writes or reasoning tokens) is passed through as `ai.{key}` with the key normalised to kebab-case, so a Bedrock response carrying `cache_read_input_tokens` also emits `ai.cache-read-input-tokens`. Check your provider's docs before adding them to the input count, because whether they are already inside `InputTokenCount` differs by provider (Anthropic reports cache buckets alongside it, OpenAI counts cached tokens within it).

Implementation note: Metering token counts directly works when a feature's price is per token. To meter against **credits** instead, set the entitlement's `priceBehavior` to `credit_burndown` and give each event its own `creditConsumptionRate`. Input and output tokens can burn the same credit at different rates, which keeps the price ratio between them in Schematic rather than hard-coded in a custom `MapUsage`.

### Reserving credits with a lease

The gate/track pair is post-paid: the balance is checked before the call and debited after it, so a long generation or several concurrent calls can all pass on the same balance and overspend. For credit-burndown entitlements, `UseSchematicCreditLease` replaces the pair and reserves the estimated credits first:

```csharp
builder.Services.AddChatClient(sp => /* provider client */)
    .UseSchematicCreditLease("ai-chat", o =>
    {
        o.LeaseDuration = TimeSpan.FromMinutes(5);         // hold expiry if the app dies mid-call
        // o.EstimateUsage = (messages, options) => ...;     // default: ~4 chars/token in, MaxOutputTokens (or 1024) out
        // o.CreditCost = (events, entitlement) => ...;      // default: every event's quantity * entitlement.ConsumptionRate
    });
```

Per call it checks the flag, sizes a hold from `EstimateUsage` and `CreditCost`, and acquires a lease against the company's credit; the model runs; the actual usage is tracked against the lease (extending it first if the estimate fell short) and the unspent remainder is released. A rejected hold denies the call with reason `insufficient_credits` (see `AllowOverdraft` below). If the model call throws, the whole hold is released. When the flag's entitlement is not credit-based the middleware behaves like `UseSchematicRequireFeature` + `UseSchematicUsageTracking`.

Schematic funds what it can: an acquire for more than the balance covers returns a smaller lease rather than failing, and the middleware works from the granted amount. Lease-backed events are sent immediately rather than through the SDK's buffered path, which cannot carry a lease id. If that send fails the event falls back to the buffered path, so usage is never lost. The lease members of `ISchematicGateClient` have default implementations that throw `NotSupportedException`; a custom gate client must implement them to use this middleware.

### Letting denied calls through

Sometimes a denial should be billed rather than enforced: a credit balance the customer is charged past rather than cut off at. (For observing gating before enforcing it across the whole app, use shadow mode above; it covers these middlewares too, since they gate through the same client.) Both gating middlewares take the same two options:

```csharp
.UseSchematicCreditLease("ai-chat", o =>
{
    o.AllowOverdraft = true;                          // a spent balance is overage, not a refusal
    o.OnDenied = denial =>                            // every would-be denial, allowed or not
    {
        metrics.Overdraft(denial.FlagKey, denial.Response?.Entitlement?.CreditSettled);
        return ValueTask.CompletedTask;
    };
})
```

- `DenialBehavior = SchematicDenialBehavior.Allow` invokes the model on any denial, including a missing identity. The denial is still logged and reported to `OnDenied`, and usage is still tracked. It does not cover a check that *fails*; that remains `FailurePolicy`. Unlike shadow mode it applies to one pipeline and passes the entitlement to `OnDenied`, so it fits a call that should proceed for billing reasons rather than a rollout.
- `AllowOverdraft` (lease middleware only) allows just the denials that are about credit: a check denied for a spent balance, or a hold Schematic will not fund. The model runs without a hold and its usage goes through the buffered path, which debits the grant past zero, the overage the customer is then billed or topped up for. A denial that is not about credit, such as no entitlement at all, still denies.
- `OnDenied` is invoked for every would-be denial before the middleware throws or lets it through; `SchematicAiDenial.Allowed` says which, and `Response` carries the entitlement with its balance. An exception thrown from the callback is logged and changes nothing.

### Answering a denied call

A denial inside a request handler surfaces as `SchematicFeatureDeniedException`, which would otherwise become a 500. One middleware, placed before routing, turns it into the gate's response (`OnDenied`, or 403 ProblemDetails with `featureId` and `accessDeniedReason`):

```csharp
app.UseSchematicFeatureDeniedResponses();
```

It is a middleware rather than an `IExceptionHandler` because `UseExceptionHandler` logs every exception at Error before consulting its handlers, so a denial answered with a 403 would still be logged as an error. `SchematicDenialResponse.WriteAsync` is public for apps that want to write their own handler.

## Gating and tracking Quartz jobs

`SchematicHQ.Community.Extensions.Quartz` applies the same gate/track model to scheduled jobs:

```csharp
builder.Services.AddSchematicQuartz();                 // options, resolver, listeners
builder.Services.AddQuartz(q =>
{
    q.AddSchematic();                                  // wires the listeners and trait report schedules into the scheduler
});
```

Decorate job classes:

```csharp
[RequireFeature("nightly-sync")]                       // execution vetoed when not entitled
[TrackFeature("nightly-sync-runs")]                    // tracked after each successful run
public sealed class NightlySyncJob : IJob { ... }
```

The company/user identity comes from `schematic.company.*` / `schematic.user.*` entries in the merged job data map, declare them with `.UsingSchematicCompany("id", tenantId)` on the job or trigger builder (or the configurator inside `AddQuartz`), or register a custom `ISchematicJobContextResolver`. 

Check failures follow `AddSchematicQuartz(o => o.FailurePolicy = ...)`, and tracking failures never fail the job. 

### Reporting traits on a schedule

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

The source receives each tenant id and handles tenancy itself, return `null` to skip a tenant. Tenants are processed with bounded parallelism, so you're safe to acquire per-tenant resources inside the call. With `AddSchematicQuartz` and `q.AddSchematic()`, every report that sets a cron runs on that schedule (missed runs fire once on startup; trait upserts are last-write-wins, so re-runs are safe). One failing tenant is logged and retried on the next run without sinking the rest. Reports without a cron — or apps not using Quartz — run on demand via `ISchematicTraitReportRunner.RunReportAsync("seats")`.

Set `o.ScheduleEnabled = false` to keep a report registered and on-demand runnable without anything firing it on a schedule:

```csharp
builder.Services.AddSchematicTraitReport<TenantCatalog, SeatSource>("seats", o =>
{
    o.Cron = "0 0 3 * * ?";
    o.ScheduleEnabled = !builder.Environment.IsEnvironment("IntegrationTest");
});
```

An integration test host that boots your real application would otherwise fan out over every tenant and write to Schematic partway through a suite. It also covers nominating one instance to own reporting when several run the same configuration.

## Testing your app

The filters, AI middlewares and Quartz listeners call Schematic through the `ISchematicGateClient` seam. `SchematicHQ.Community.Testing` supplies a fake for it:

```csharp
var fake = new FakeSchematicGateClient();

await using var host = await AlbaHost.For<Program>(webHost =>
    webHost.ConfigureServices(services => services.AddSchematicFake(fake)));

fake.Deny("advanced-reports");                       // every identity
fake.DenyForUser("user_42", "exports");              // only checks made as that user
fake.ThrowOnCheck = new HttpRequestException();      // Schematic unreachable; every Throw* switch takes an exception
fake.RespondToCheck(flag => new CheckFlagWithEntitlementResponse { ... });   // script the full response

fake.CheckCalls.Single().FlagKey.ShouldBe("advanced-reports");
fake.TrackCalls.ShouldBeEmpty();
fake.Reset();                                        // between tests
```

Every check is allowed until a test says otherwise. Checks, tracks, identifies and lease calls are recorded; credit leases are granted in full unless `RejectLease` or `ThrowOnAcquireLease` is set. `AddSchematicFake` replaces whatever gate client the application registered and also exposes the fake itself from DI. The fake is safe to share across the parallel tests of one host.

For environments with no API key at all — local development, CI, preview deployments — `AddSchematicNoOp()` registers a client that talks to nothing. `AddSchematic` rejects a missing key, and everything that takes `ISchematicGateClient` fails at resolve time without a stand-in. Branch on the key so that wiring stays unconditional and those code paths still execute off a key:

```csharp
var apiKey = builder.Configuration["Schematic:ApiKey"];

if (!string.IsNullOrWhiteSpace(apiKey))
    builder.Services.AddSchematic(apiKey);
else
    builder.Services.AddSchematicNoOp();       // Track/Identify discarded, checks allow

builder.Services.AddSchematicAspNetCore();     // unchanged either way
```

Track and Identify are discarded, and entitlement checks resolve `true` so gated features stay reachable — pass `AddSchematicNoOp(allowAll: false)` to assert denial paths instead. Because it opens every gate, branch on whether a key is *configured*, not on whether one failed to load: reaching this in production would entitle everybody. Call it instead of `AddSchematic`, never as well as — whichever registers first wins.

## License

Apache-2.0
