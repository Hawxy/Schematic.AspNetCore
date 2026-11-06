---
name: schematic-ai-usage-metering
description: Meter LLM token usage into Schematic and bill it as credit burndown. Use when adding AI usage tracking or billing to a product, setting up Schematic credits, features and entitlements for an AI feature, choosing a credit consumption rate, verifying metered usage through the Schematic MCP, or wiring SchematicHQ.Community.Extensions.AI into a .NET Microsoft.Extensions.AI chat pipeline.
---

# Metering AI usage with Schematic

This skill covers **metering** AI usage: recording what each model call consumed against the company that made it, and
letting Schematic convert that into credits the plan grants. It does not cover gating product modules.

It has three parts:

1. [Generic approach](#1-generic-approach): decisions, Schematic setup and implementation rules for any stack.
2. [Schematic MCP](#2-schematic-mcp): configuring and verifying the Schematic side from Claude Code.
3. [.NET implementation](#3-net-implementation): `SchematicHQ.Community.*` packages with Microsoft.Extensions.AI.

Reference implementation: Q Safety (`Safety.Api`, `Safety.Infrastructure/Entitlements/AiUsageMetering.cs` and
`Safety.Infrastructure/Ai/IServiceCollectionExtensions.cs`).

---

## 1. Generic approach

### 1.1 Decide these before writing code

**Metered or gated.** Choose one per AI feature and write it down.

- *Metered, never gated* (Q Safety's choice): every call runs; usage is tracked after the response; an exhausted balance
  is billed as overage or topped up. The code never reads the feature's flag value. Pick this when a customer who runs
  out should be sold more credits, not locked out of the feature.
- *Gated*: check the entitlement before the call and refuse when the balance is spent. A plain check-then-track is
  post-paid, so concurrent or long calls can all pass on the same balance. For a hard budget, reserve credits before the
  call (a credit *lease*) and settle after it.

**The metered unit.** Two options:

| Option | Events per call | Where the input/output price ratio lives | Trade-off |
|---|---|---|---|
| Weighted tokens (Q Safety) | 1: `quantity = input + output × w` | App config (`w`) | One feature and one rate to manage. Changing `w` needs a config change. |
| Separate input and output events | 2: `ai.input-tokens`, `ai.output-tokens` | Schematic (one `creditConsumptionRate` per event) | Ratio is edited in Schematic, but there are two features and two entitlements to keep in step. |

For weighted tokens, set `w` to the provider's output:input price ratio (5 for current Anthropic Sonnet-class models).
One weighted token then costs the same as one input token, so a single rate prices every call correctly whatever its
input/output mix.

**Identity.** Every event needs a company, and usually a user.

- Company: the tenant (Q Safety uses the org id, which is also the tenant id).
- User: the user the action is performed as. When your app supports impersonation, use the impersonated user.
- Calls outside a request (background jobs, queues) have no ambient identity. Pass one explicitly, or accept that the
  usage is not tracked. Decide which; do not let it happen by accident.

**Extra token counts.** Providers report cache reads and writes, reasoning tokens and similar counts separately.
Whether they are already inside the input count differs by provider (Anthropic reports cache buckets alongside the
input count; OpenAI counts cached tokens within it). Read the provider's docs before adding them to the quantity, or
you will double-count on some providers.

### 1.2 The Schematic side

Credit burndown has three parts:

1. **Credit type**: the unit, e.g. "AI Credits". It has a USD `pricePerUnit` because it is also a billing product
   customers can buy more of. Billing (Stripe) must be connected before a credit can be created.
2. **Grant**: how many credits the plan gives each period (`creditGrants` on the plan).
3. **Burn rate**: `creditConsumptionRate` on the feature's entitlement, the credits one event unit costs.

```
credits burned = event quantity × creditConsumptionRate
```

Setup, in order:

1. Create the credit type, or reuse an existing AI credit.
2. Create an **event-based** feature whose `eventSubtype` equals the event name the code sends, character for
   character (Q Safety: `safety-ai-usage`). Schematic drops events that match no feature, with no error.
3. On the **base plan**, add an entitlement for that feature with `priceBehavior: credit_burndown`, the credit id and
   the rate. The entitlement value defaults to unlimited; the balance is the limit. Event-based features cannot be
   add-on entitlements.
4. Add a credit grant to the same plan. Burndown with no grant leaves every customer at a zero balance. That is a
   403 for a gated feature, and overage from the first call for a metered one.
5. Publish the plan version. Plan edits go to a draft until published.

Grant and credit settings:

| Setting | On | Effect |
|---|---|---|
| `resetCadence` | grant | How often the grant refills; `never` grants once. |
| `rolloverPercentage` | grant | Percentage of unused credits carried into the next period (default 0). |
| `autoTopup` / `autoTopupThreshold` | grant | `automatic` buys a bundle when the balance drops below the threshold; `user_controlled` leaves it to the customer. |
| `expiresAfter` | credit | Credits expire after N days or billing periods. |
| `burnStrategy` | credit | Which credits are spent first. The default spends plan-granted credits before purchased ones. |

### 1.3 Choosing the consumption rate

```
cost per unit    = provider price for one unit (for weighted tokens: the input token price)
pass-through     = cost per unit ÷ credit pricePerUnit
creditConsumptionRate = pass-through × margin
```

Q Safety example: Sonnet 5 on Bedrock (AU region), credits at $1.00, rate `0.00001` (100,000 weighted tokens per
credit), 100 credits granted per month on the base plan. Check current provider prices before reusing these numbers,
because regional and cross-region pricing differ.

Keep credit conversion in Schematic. The code sends model-level quantities (tokens or weighted tokens), and the rate
is changed in Schematic without a deploy when the provider changes its prices or the margin changes.

### 1.4 Implementation rules

- Meter from the usage the provider reports on the response, not from an estimate.
- For streaming responses, sum usage across the updates, and record it even when the consumer stops reading early.
- Send nothing when the quantity is zero. Some responses omit usage.
- Add the raw counts as event traits (`model`, `input_tokens`, `output_tokens`) so billing can be reconciled against
  the provider's figures.
- A tracking failure must never fail the AI call. Tracks are buffered and sent in the background.
- Send the event only after the call succeeds.
- Keep the event name in one constant and test it; it must equal the feature's `eventSubtype`.
- Keep `w` in configuration, with the provider's current ratio as the default.
- Environments with no API key (local dev, CI) use a no-op client. Decide by whether a key is *configured*, not by
  whether one failed to load, so a missing production secret fails loudly.
- Check the SDK's quantity type. The C# SDK's quantity is an `int`, so very large quantities are clamped.

### 1.5 Tests to write

- The weighting arithmetic, e.g. `1000 in + 200 out × 5 = 2000`.
- Equal cost regardless of split: `5000 in + 0 out` equals `2500 in + 500 out` when `w = 5`.
- The event name equals the Schematic feature's event subtype.
- Zero or missing usage produces no event.
- The raw counts and model id are present as traits.
- An end-to-end test that one AI call through the real pipeline records one track for the expected company.

### 1.6 Verifying in a live environment

After a real call, check the company's usage and credit balance in Schematic (see
[MCP verification](#24-verifying-usage)). Also check the balance change against the arithmetic above:
`weighted tokens × rate` credits should have come off the grant.

---

## 2. Schematic MCP

Schematic runs an MCP server that reads and edits the account (plans, features, credits, companies, usage) from
Claude Code.

### 2.1 Install

The server ships in the `schematic-ai-tools` plugin, from the marketplace at
`https://github.com/SchematicHQ/schematic-ai-tools`. The plugin also includes Schematic's own backend, frontend and
components skills.

```
/plugin marketplace add SchematicHQ/schematic-ai-tools
/plugin install schematic-ai-tools@schematichq
```

It connects to `https://api.schematichq.com/mcp` over HTTP and signs in with OAuth. Run `/mcp` in an interactive
session to authorize it. Until then its tools are unavailable, and non-interactive sessions report that it needs
authentication.

Tools are named `mcp__plugin_schematic-ai-tools_schematic__<tool>`.

### 2.2 Tools for this workflow

| Step | Tools |
|---|---|
| Orientation | `get_onboarding_state`, `list_skills` / `get_skill` (Schematic's own guides) |
| Billing prerequisite | `connect_stripe_account`, `link_stripe_to_schematic`, `create_stripe_sandbox` |
| Credit type | `list_credits`, `create_credit`, `create_credit_bundle`, `update_credit_bundle`, `set_credit_bundle_live` |
| Feature | `list_features`, `create_feature` (`featureType: "event"`, `eventSubtype`), `update_feature`, `list_flags`, `get_flag` |
| Entitlement and grant | `list_plans`, `get_plan_entitlements`, `add_entitlements_to_plan` |
| Publish | `check_publish_readiness`, `get_plan_version_diff`, `publish_plan_version`, `get_publish_plan_url`, `discard_plan_draft` |
| Verify | `get_company`, `get_feature_usage`, `check_companies_usage`, `find_companies_near_limit` |

### 2.3 Configuring burndown

One `add_entitlements_to_plan` call sets the burn rate. Q Safety's call:

```json
{
  "planId": "plan_...",
  "entitlements": [
    {
      "featureId": "feat_...",
      "priceBehavior": "credit_burndown",
      "creditId": "bilcr_...",
      "creditConsumptionRate": 0.00001
    }
  ]
}
```

The same tool takes `creditGrants` (with `action` create, update or delete; one grant per credit per plan), and an
entitlement with `"action": "delete"` removes one. Follow it with `check_publish_readiness`, then `publish_plan_version`
or send the user the `get_publish_plan_url` link.

Notes:

- Read before writing. `get_plan_entitlements` reports published values and draft changes separately; confirm the
  draft is what you intend before publishing.
- Tool results include `environment_id`. Confirm you are in the intended environment before any write.
- Claude Code's auto mode may block Schematic writes as feature-flag changes. When a write is blocked, give the user
  the exact values to enter in the Schematic app, or ask them to approve the call. Do not look for another way around
  the block.
- A plan change affects every company on the plan as soon as it is published. Check `count_companies_on_plan` first.

### 2.4 Verifying usage

- `get_feature_usage` with `companyId` and `featureId` returns the recorded usage per period for one company. Each
  entry carries the `event_subtype`, which confirms the events matched the feature.
- `get_company` returns the company's plan, credit grants and credit balances.
- `check_companies_usage` and `find_companies_near_limit` list usage across companies.

If usage stays at zero after a real call, check in this order: the API key is set in that environment; the event name
equals the `eventSubtype`; the call had an identity; the plan version with the entitlement is published.

---

## 3. .NET implementation

Packages (`SchematicHQ.Community.*`, 0.4.0 or later; `Microsoft.Extensions.AI` 10.10.0 or later):

| Package | Use |
|---|---|
| `SchematicHQ.Community.DependencyInjection` | `AddSchematic`, `AddSchematicNoOp`, `AddSchematicFusionCache` |
| `SchematicHQ.Community.AspNetCore` | `AddSchematicAspNetCore`, `ISchematicFlagContextResolver` (request identity) |
| `SchematicHQ.Community.Extensions.AI` | `UseSchematicUsageTracking`, `UseSchematicCreditLease`, `UseSchematicRequireFeature` |
| `SchematicHQ.Community.Testing` | `FakeSchematicGateClient`, `AddSchematicFake` (test projects only) |

### 3.1 Client registration

```csharp
if (!string.IsNullOrWhiteSpace(configuration["Schematic:ApiKey"]))
    services.AddSchematic(configuration["Schematic:ApiKey"]!);
else
    // Discards Track and Identify, so the pipeline below runs unchanged in dev and tests.
    services.AddSchematicNoOp();

// Required even when no endpoint is gated: the AI middleware resolves identity through it.
services.AddSchematicAspNetCore();
services.AddSchematicFlagContextResolver<AppFlagContextResolver>();
```

### 3.2 Request identity

```csharp
public sealed class AppFlagContextResolver : ISchematicFlagContextResolver
{
    public ValueTask<SchematicFlagContext?> ResolveAsync(HttpContext context, CancellationToken ct)
    {
        var tenantId = context.User.FindFirstValue("org_id");
        if (string.IsNullOrWhiteSpace(tenantId))
            return ValueTask.FromResult<SchematicFlagContext?>(null); // usage is not tracked

        var userId = context.User.FindFirstValue("profile_id");
        var user = string.IsNullOrWhiteSpace(userId) ? new Dictionary<string, string>() : new() { ["id"] = userId };

        return ValueTask.FromResult<SchematicFlagContext?>(
            new SchematicFlagContext(new Dictionary<string, string> { ["id"] = tenantId }, user));
    }
}
```

Read claims only, so the resolver has no dependencies and works at any lifetime. When the app swaps the principal for
impersonation, read the claim the swap sets.

### 3.3 Usage mapping

The library's default mapping sends `ai.input-tokens` and `ai.output-tokens` (plus `ai.{key}` for each
`UsageDetails.AdditionalCounts` entry), each with the model id as a trait. For weighted tokens, replace it:

```csharp
public sealed class AiUsageMeteringOptions
{
    public const string SectionName = "Schematic:AiUsage";

    // Must equal the Schematic feature's event subtype.
    public const string EventName = "my-product-ai-usage";

    // Output:input price ratio of the model in use.
    public double OutputTokenWeight { get; set; } = 5;
}

public static class AiUsageMetering
{
    public static IEnumerable<SchematicAiUsageEvent> MapUsage(
        UsageDetails usage, string? modelId, AiUsageMeteringOptions options)
    {
        var input = usage.InputTokenCount ?? 0;
        var output = usage.OutputTokenCount ?? 0;
        var weighted = (long)Math.Round(input + output * options.OutputTokenWeight, MidpointRounding.AwayFromZero);

        if (weighted <= 0)
            return [];

        return
        [
            new SchematicAiUsageEvent(
                AiUsageMeteringOptions.EventName,
                weighted,
                new Dictionary<string, object?>
                {
                    ["model"] = modelId,
                    ["input_tokens"] = input,
                    ["output_tokens"] = output,
                }),
        ];
    }
}
```

### 3.4 Chat pipeline (metered, never gated)

```csharp
// Bound once: the pipeline is built at startup and the middleware captures its options.
var metering = new AiUsageMeteringOptions();
configuration.GetSection(AiUsageMeteringOptions.SectionName).Bind(metering);

services
    .AddChatClient(sp => sp.GetRequiredService<IAmazonBedrockRuntime>().AsIChatClient())
    .UseOpenTelemetry()
    .UseSchematicUsageTracking(o =>
        o.MapUsage = (usage, modelId) => AiUsageMetering.MapUsage(usage, modelId, metering));
```

Behaviour of `UseSchematicUsageTracking`:

- It reads `UsageDetails` from each response. For streaming, it sums usage across updates and records it even if the
  consumer abandons the stream.
- It only tracks successful calls, and a tracking failure never fails the call.
- Identity comes from the current request through the resolver. Outside a request it uses `o.FallbackContext`; when
  that is null too, the call is not tracked.

Background jobs: either set `FallbackContext` on a separate pipeline built for that job's tenant, or accept that the
usage is not metered. A single shared `FallbackContext` attributes every background call to one company, which is
rarely correct in a multi-tenant app.

### 3.5 When the feature must be gated

Replace `UseSchematicUsageTracking` with one of these:

```csharp
// Reserve credits before the call; settle actual usage after it; release the remainder.
.UseSchematicCreditLease("my-product-ai-usage", o =>
{
    o.MapUsage = (usage, modelId) => AiUsageMetering.MapUsage(usage, modelId, metering);
    o.AllowOverdraft = true; // a spent balance or an unfunded hold runs the call and bills it as overage
    o.OnDenied = denial => { /* metric, or read denial.Response?.Entitlement?.CreditSettled */ return default; };
})

// Or check before, track after (post-paid; concurrent calls can overspend).
.UseSchematicRequireFeature("my-product-ai-usage", o => o.DenialBehavior = SchematicDenialBehavior.Allow)
.UseSchematicUsageTracking(...)
```

- A denied call throws `SchematicFeatureDeniedException`. Add `app.UseSchematicFeatureDeniedResponses()` before
  routing to turn it into a 403 ProblemDetails; otherwise a Minimal API handler returns a 500.
- The SDK never throws from a flag check. When Schematic is unreachable it returns the flag's entry in
  `ClientOptions.FlagDefaults`, which is `false` unless set. To fail open during an outage, register with
  `AddSchematic(apiKey, o => o.FailOpenFor("my-product-ai-usage"))`. `FailurePolicy` only applies to a gate client
  that throws, and to lease calls.

### 3.6 Tests

Unit-test the mapper directly (see [1.5](#15-tests-to-write)). For the pipeline, register the fake in the test host
and assert on what it recorded:

```csharp
var fake = new FakeSchematicGateClient();
services.AddSchematicFake(fake); // replaces whatever gate client the app registered

// ... make a request that calls the model ...

var track = fake.TrackCalls.ShouldHaveSingleItem();
track.EventName.ShouldBe(AiUsageMeteringOptions.EventName);
track.Company["id"].ShouldBe(orgId);
fake.Reset(); // between tests on a shared host
```

The fake allows every check until told otherwise (`Deny`, `DenyForUser`), records checks, tracks, identifies and lease
calls, and grants leases in full unless `RejectLease` or `ThrowOnAcquireLease` is set.

### 3.7 Configuration keys

| Key | Default | Purpose |
|---|---|---|
| `Schematic:ApiKey` | none | With no key the no-op client is used and nothing is sent. |
| `Schematic:AiUsage:OutputTokenWeight` | `5` | Output:input price ratio for weighted tokens. |

Add the API key to every deployed environment's secrets. When it is missing, the no-op client is used, so nothing is
metered and no error is raised.
