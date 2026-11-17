---
name: schematic-ai-usage-metering
description: Meter LLM usage into Schematic with inference pricing events and bill it as credit burndown at per-token rates. Use when adding AI usage tracking or billing to a product, setting up Schematic credits, inference features and entitlements for an AI feature, choosing per-token credit rates, verifying metered usage through the Schematic MCP, sending inference events from any stack over the REST API or OpenTelemetry, or wiring SchematicHQ.Community.Extensions.AI into a .NET Microsoft.Extensions.AI chat pipeline. Also covers the Track-event fallback (weighted tokens) for usage that is not token-priced.
---

# Metering AI usage with Schematic

This skill covers **metering** AI usage: recording what each model call consumed against the company that made it, and
letting Schematic convert that into credits the plan grants. It does not cover gating product modules.

**Use inference events.** One inference event per model call carries the raw token counts, provider and model, and
Schematic prices each token type at rates set on the plan. Use Track events only in the cases listed in
[1.9](#19-fallback-track-events).

It has three parts:

1. [Generic approach](#1-generic-approach): decisions, Schematic setup, implementation rules and the REST API, for any stack.
2. [Schematic MCP](#2-schematic-mcp): configuring and verifying the Schematic side from Claude Code.
3. [.NET implementation](#3-net-implementation): `SchematicHQ.Community.*` packages with Microsoft.Extensions.AI.

Schematic's own guide: <https://docs.schematichq.com/billing/inference-pricing>.

---

## 1. Generic approach

### 1.1 Decide these before writing code

**Event type.** Inference events, unless a row in the second column applies.

| | Inference events (default) | Track events (fallback) |
|---|---|---|
| Sent per model call | 1 event with every token count | 1 event per metered unit, or 1 weighted quantity |
| Where prices live | Schematic: a rate per token type, with per-model overrides | One rate per event name; any input/output weighting lives in app config |
| Prompt caching, reasoning | Priced separately by Schematic | The app has to fold them into the quantity |
| Provider cost and margin | Recorded per event from list prices or your rate sheet | Not recorded |
| Use when | The feature is priced by tokens | See [1.9](#19-fallback-track-events) |

**Metered or gated.** Choose one per AI feature and write it down.

- *Metered, never gated*: every call runs; usage is sent after the response; an exhausted balance is billed as overage
  or topped up. The code never reads the feature's flag value. Pick this when a customer who runs out should be sold
  more credits, not locked out of the feature.
- *Gated*: check the entitlement before the call and refuse when the balance is spent. A plain check-then-send is
  post-paid, so concurrent or long calls can all pass on the same balance. For a hard budget, reserve credits before the
  call (a credit *lease*) and settle after it with an inference event that carries the lease id.

**Identity.** Every event needs a company, and usually a user.

- Company: the tenant.
- User: the user the action is performed as. When your app supports impersonation, use the impersonated user.
- Calls outside a request (background jobs, queues) have no ambient identity. Pass one explicitly, or accept that the
  usage is not metered. Decide which; do not let it happen by accident.

**Token counts.** Schematic expects these, and subtracts the two cache counts from the input before pricing it:

| Field | Meaning |
|---|---|
| `input_tokens` | Every input token, **including** cache reads and cache writes |
| `cached_input_tokens` | Subset of input read from the prompt cache |
| `cache_creation_input_tokens` | Subset of input written to the prompt cache |
| `output_tokens` | Every output token, including reasoning |
| `reasoning_tokens` | Priced on top of output; leave its rate at 0 when the provider counts reasoning inside output |

Providers report these differently, so map per provider:

| Provider API | `input_tokens` to send |
|---|---|
| Anthropic Messages | `input_tokens + cache_read_input_tokens + cache_creation_input_tokens` (its own input count excludes both) |
| OpenAI Responses / Chat Completions | The reported input count (cached tokens are already inside it; there are no cache writes) |
| Gemini | `promptTokenCount`; add `thoughtsTokenCount` to the output |

A wrong mapping under-bills cached tokens or counts them twice. Confirm it against one real call that used the cache.

**Provider and model names.** Each event carries `provider`, `request_model` and `response_model`. Per-model rates
and cost lookups match on the provider and the response model exactly as the event reports them (a trailing snapshot
date is ignored). Send the model from the response, not the one requested. Look at one real event before configuring
per-model rates: gateways such as Bedrock report their own provider name and prefixed model ids.

### 1.2 The Schematic side

Credit burndown for an inference feature has three parts:

1. **Credit type**: the unit, e.g. "AI Credits". It has a `price_per_unit` because it is also a billing product
   customers can buy more of. Billing (Stripe) must be connected before a credit can be created.
2. **Grant**: how many credits the plan gives each period (`credit_grants` on the plan).
3. **Rates** on the feature's plan entitlement: a per-request `credit_consumption_rate` and per-token `token_rates`.

```
credits burned = credit_consumption_rate × requests
               + (input − cached − cache creation) × token_rates.input_tokens
               + cached × token_rates.cached_input_tokens
               + cache creation × token_rates.cache_creation_input_tokens
               + output × token_rates.output_tokens
               + reasoning × token_rates.reasoning_tokens
```

Setup, in order:

1. Create the credit type, or reuse an existing AI credit.
2. Create an **event-based** feature with `event_source: inference` and an `event_subtype` equal to the event name the
   code sends, character for character. An event whose name matches no feature is recorded but burns nothing, with no
   error. `event_source` cannot be changed once the feature exists.
3. On the **base plan**, add an entitlement for that feature with `price_behavior: credit_burndown`, the credit, the
   per-request rate (0 when only tokens are priced) and `token_rates`. A token type left out is priced at 0. Add
   `model_rates` for models priced differently; each entry replaces the default rates for that provider and model.
4. Add a credit grant to the same plan. Burndown with no grant leaves every customer at a zero balance. That is a
   denial for a gated feature, and overage from the first call for a metered one.
5. Publish the plan version. Plan edits go to a draft until published.

An inference feature can also be entitled as unlimited, which grants access without billing.

Grant and credit settings:

| Setting | On | Effect |
|---|---|---|
| `reset_cadence` | grant | How often the grant refills; `never` grants once. |
| `rollover_percentage` | grant | Percentage of unused credits carried into the next period (default 0). |
| `auto_topup_availability`, `auto_topup_threshold_credits`, `auto_topup_amount` | grant | `automatic` buys more when the balance drops below the threshold; `user_controlled` leaves it to the customer. |
| `default_expiry_unit_count`, `default_expiry_unit` | credit | Credits expire after N days or billing periods. |
| `burn_strategy` | credit | Which credits are spent first. The default spends the ones expiring soonest. |

### 1.3 Choosing the rates

```
token rate (credits per token) = provider price per token ÷ credit price_per_unit × margin
```

A token rate is in **credits per token, not dollars**. What the customer pays per token is the rate times the credit's
`price_per_unit`, so the rate is the dollar price divided by what one credit is worth. With credits at $1.00 the rate
equals the dollar price per token; with credits at $0.01 it would be 100 times that figure.

Worked example, credits at $1.00 and no margin (margin = 1), for a model priced at $3 per million input tokens and $15
per million output tokens, with cache reads at $0.30 and cache writes at $3.75 per million:

| Token type | Provider price per million | Provider price per token | `token_rates` value (credits per token) | Customer pays per million |
|---|---|---|---|---|
| `input_tokens` | $3.00 | $0.000003 | `0.000003` | 3 credits = $3.00 |
| `cached_input_tokens` | $0.30 | $0.0000003 | `0.0000003` | 0.3 credits = $0.30 |
| `cache_creation_input_tokens` | $3.75 | $0.00000375 | `0.00000375` | 3.75 credits = $3.75 |
| `output_tokens` | $15.00 | $0.000015 | `0.000015` | 15 credits = $15.00 |
| `reasoning_tokens` | counted in output | counted in output | `0` | charged as output |

Check a rate by working it back: `rate × 1,000,000 × price_per_unit` should equal the provider's price per million
times the margin. For the input row, `0.000003 × 1,000,000 × $1.00 = $3.00`.

To charge more than cost, multiply every rate by the margin. A 30% markup (margin = 1.3) makes the input rate
`0.0000039`, so a million input tokens burn 3.9 credits, or $3.90 against a cost of $3.00.

A call with 2,000 input tokens (1,500 of them read from the cache) and 400 output tokens, at the rates in the table:

```
uncached input   500 × 0.000003   = 0.0015 credits
cached input   1,500 × 0.0000003  = 0.00045 credits
output           400 × 0.000015   = 0.006 credits
total                               0.00795 credits = $0.00795
```

Check current provider prices before reusing these numbers; regional and cross-region pricing differ. When several
models share a feature, set the default rates for the main model and add a `model_rates` entry per other model.

Keep all pricing in Schematic. The code sends raw token counts, and rates change in Schematic without a deploy when
the provider changes its prices or the margin changes.

### 1.4 Cost tracking

Separately from credits, Schematic records what each call cost you, so margin can be checked per company. The cost
comes from the first of these that applies:

1. A `cost` the event sends.
2. Your inference rate sheet for the event's provider and response model.
3. Schematic's list price (Anthropic and OpenAI models only).

Otherwise the event is unpriced. Cost never changes the credits burned. For a provider with no list price, or
negotiated rates, add rate-sheet entries rather than computing cost in the app.

### 1.5 Implementation rules

- Send one inference event per model call, from the usage the provider reports on the response, not from an estimate.
- For streaming responses, sum usage across the updates, and send it even when the consumer stops reading early.
- Send nothing when the response reported no tokens. Some responses omit usage.
- Send the event only after the call succeeds, and off the request path. A send failure must never fail the AI call.
- Give every event an idempotency key, so a retried send is not billed twice.
- With tool or function calling, meter each model call, not the whole exchange. One event for several calls
  under-charges any per-request rate and reports only the last model.
- Send each call one way only: not as both an inference event and a Track event for the same feature, and not also
  through Schematic's OpenTelemetry ingestion.
- Inference events need a secret API key.
- Keep the event name in one constant and test it; it must equal the feature's `event_subtype`.
- Environments with no API key (local dev, CI) use a no-op client. Decide by whether a key is *configured*, not by
  whether one failed to load, so a missing production secret fails loudly.

### 1.6 Sending the event from any stack

Outside .NET there is no package for this, so write a small wrapper around the model client.
[Part 3](#3-net-implementation) is one implementation of the same outline.

1. **Wrap the model call in one place.** Use a client wrapper or middleware that every model call goes through, below
   any tool-calling loop, so each model call is seen separately.
2. **Resolve identity before the call.** Take the company and user from the request, or from what the job was given.
   With no company, run the call and send nothing.
3. **Read usage from the response.** For a stream, add up the usage chunks and take the total when the stream ends or
   is abandoned. Map the counts as in [1.1](#11-decide-these-before-writing-code).
4. **Build the event**: event name, provider, request and response model, token counts, a new idempotency key (a
   UUID) and the time of the call.
5. **Queue it and return.** Use a bounded in-memory queue and a background worker that posts batches on a short
   interval and flushes on shutdown. When the queue is full, or a send still fails after the HTTP client's retries,
   drop the events and log it. Never block or fail the model call. A retry reuses the event's idempotency key.
6. **No API key configured.** Replace the sender with one that discards events.

**REST API.** One event:

```
POST https://api.schematichq.com/events
X-Schematic-Api-Key: <secret key>
Content-Type: application/json
```

```json
{
  "event_type": "inference",
  "idempotency_key": "3f2b8c1e-5d0a-4e7b-9c46-1a2d3e4f5a6b",
  "sent_at": "2026-01-15T09:30:00Z",
  "body": {
    "event": "support-chat",
    "company": { "id": "acme" },
    "user": { "id": "user-42" },
    "provider": "anthropic",
    "request_model": "claude-sonnet-5-5",
    "response_model": "claude-sonnet-5-5",
    "input_tokens": 2667,
    "cached_input_tokens": 2259,
    "cache_creation_input_tokens": 404,
    "output_tokens": 104
  }
}
```

`company`, `provider`, `response_model`, `input_tokens` and `output_tokens` are required, and `event` is required for
the call to burn credits. Optional: `user`, `request_model`, the cache counts, `reasoning_tokens`, `requests` (default
1), `cost` (a decimal string, with `currency`), `lease_id` and `reservation_id`. `company` and `user` are key maps;
use the keys the company and user were identified with. Events with the same `idempotency_key` in one environment are
dropped for 24 hours.

For a batch, post `{ "events": [ ... ] }` to `https://api.schematichq.com/event-batch`. Each item has the shape above.

**SDK.** Schematic's SDKs (Node.js, Python, Go, Java, Ruby, C#) wrap the same two endpoints. Use the SDK's
event-creation call when its version has the `inference` event type; the `track` helper sends Track events.
Otherwise call the REST API with any HTTP client.

**OpenTelemetry.** When the app already emits GenAI spans (`gen_ai.*` attributes), Schematic can read the usage from
them and no event code is needed. Export traces over OTLP/HTTP to `https://c.schematichq.com/v1/traces` with the
`X-Schematic-Api-Key` header (a secret key), and set `schematic.company.<key>` and `schematic.event` on each
model-call span, plus `schematic.user.<key>` when there is a user. The token counts are whatever the instrumentation
reports, so check the cache counts on a real call, and enable only one instrumentation per provider or each call is
billed twice. Do not carry these attributes in W3C baggage, which is forwarded to model providers. Details:
<https://docs.schematichq.com/billing/inference-pricing-opentelemetry>.

### 1.7 Tests to write

- One AI call through the real pipeline records one inference event for the expected company and user.
- The event name equals the Schematic feature's event subtype.
- The token mapping for your provider: a response with cache reads and writes produces an input count that includes
  both.
- The provider and model values are the ones your rates are keyed on.
- Zero or missing usage produces no event.

### 1.8 Verifying in a live environment

After a real call, read the company's credit ledger in Schematic (see [MCP verification](#24-verifying-usage)). An
inference drawdown is broken down per token type with quantity, rate and credits. Check it against the formula in
[1.2](#12-the-schematic-side) and against the provider's own usage figures for the same call.

### 1.9 Fallback: Track events

Use Track events when one of these holds:

- The unit is not tokens: images, documents, minutes of audio, tool runs.
- The stack cannot send inference events (an SDK or package version without them).
- An existing Track-based feature is staying as it is. Moving it means a new feature, because `event_source` is fixed
  at creation.

The feature is event-based with the default `event_source: track`, and its entitlement has only a
`credit_consumption_rate`: `credits burned = event quantity × credit_consumption_rate`. For tokens there are two ways
to meter:

| Option | Events per call | Where the input/output price ratio lives |
|---|---|---|
| Weighted tokens | 1: `quantity = input + output × w` | App config (`w`) |
| Separate input and output events | 2, e.g. `ai.input-tokens`, `ai.output-tokens` | Schematic: one feature and rate per event |

For weighted tokens, set `w` to the provider's output:input price ratio and keep it in configuration. The rate is
`input token price ÷ credit price_per_unit × margin`. Add the raw counts and model id as event traits so billing can
be reconciled. Fold cache and reasoning counts into the quantity only after checking whether the provider already
counts them in the input. Check the SDK's quantity type; the C# SDK's Track quantity is an `int` and larger values
are clamped.

Worked example: with input tokens at $3 per million, credits at $1.00 and no margin, the rate is `0.000003`, and
`w = 5` for a model whose output tokens cost five times its input tokens. A .NET implementation is in
[3.6](#36-fallback-track-events).

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
| Feature | `list_features`, `create_feature` (`feature_type: "event"`, `event_source: "inference"`, `event_subtype`), `update_feature`, `list_flags`, `get_flag` |
| Entitlement and grant | `list_plans`, `get_plan_entitlements`, `add_entitlements_to_plan` |
| Publish | `check_publish_readiness`, `get_plan_version_diff`, `publish_plan_version`, `get_publish_plan_url`, `discard_plan_draft` |
| Verify credits | `get_credit_ledger`, `get_company` |
| Verify cost | `get_company_inference_cost`, `list_inference_cost_rates`, `create_inference_cost_rate` |
| Track features only | `get_feature_usage`, `check_companies_usage`, `find_companies_near_limit` |

### 2.3 Configuring burndown

Create the feature:

```json
{
  "name": "Support Chat",
  "feature_type": "event",
  "event_source": "inference",
  "event_subtype": "support-chat"
}
```

The flag key is the name kebab-cased unless `flag_key` is passed. The event subtype is what the code sends as the
event name; the flag key is what a gate checks.

One `add_entitlements_to_plan` call sets the rates and the grant:

```json
{
  "plan_id": "plan_...",
  "entitlements": [
    {
      "feature_id": "feat_...",
      "price_behavior": "credit_burndown",
      "credit_id": "bcr_...",
      "credit_consumption_rate": 0,
      "token_rates": {
        "input_tokens": 0.000003,
        "cached_input_tokens": 0.0000003,
        "cache_creation_input_tokens": 0.00000375,
        "output_tokens": 0.000015
      },
      "model_rates": [
        {
          "provider": "anthropic",
          "model": "claude-haiku-5-5",
          "credit_consumption_rate": 0,
          "token_rates": { "input_tokens": 0.000001, "output_tokens": 0.000005 }
        }
      ]
    }
  ],
  "credit_grants": [
    { "credit_id": "bcr_...", "credit_amount": 100, "reset_cadence": "monthly" }
  ]
}
```

The rates are the worked example from [1.3](#13-choosing-the-rates), with credits at $1.00.

Sending `token_rates` or `model_rates` replaces the whole schedule or list; omitting one keeps what is stored. A
`model_rates` entry replaces the default rates for its model rather than adding to them, so list every token type it
should price. An entitlement with `"action": "delete"` removes the feature from the plan, and a grant takes `action`
create, update or delete (one grant per credit per plan). Follow the call with `check_publish_readiness`, then
`publish_plan_version` or send the user the `get_publish_plan_url` link.

For a Track feature the entitlement has only `credit_consumption_rate`, and `create_feature` omits `event_source`.

Notes:

- Read before writing. `get_plan_entitlements` reports published values and draft changes separately; confirm the
  draft is what you intend before publishing.
- Tool results include `environment_id`. Confirm you are in the intended environment before any write.
- Claude Code's auto mode may block Schematic writes as feature-flag changes. When a write is blocked, give the user
  the exact values to enter in the Schematic app, or ask them to approve the call. Do not look for another way around
  the block.
- A plan change affects every company on the plan as soon as it is published. Check `count_companies_on_plan` first.

### 2.4 Verifying usage

Inference features meter through credits, not usage counters, so `get_feature_usage` does not show them.

- `get_credit_ledger` with the company and `feature_id` lists each drawdown, newest first, with the requests and each
  token type's quantity, rate and credits.
- `get_company` returns the company's plan, credit grants and credit balances.
- `get_company_inference_cost` returns what the company's calls cost you, by provider and model, and how many events
  have no known cost. Compare it with the credits charged to check margin.
- `list_inference_cost_rates` shows your rate sheet. `create_inference_cost_rate` adds a per-token cost for a provider
  and response model; stored events in its window are repriced within about an hour.

If the ledger shows no drawdown after a real call, check in this order: a secret API key is set in that environment;
the event name equals the `event_subtype`; the feature's `event_source` is inference; the call had an identity; the
plan version with the entitlement is published. If credits burn at the wrong rate, compare the event's provider and
response model with the `model_rates` entries.

---

## 3. .NET implementation

Packages (`SchematicHQ.Community.*` 0.5.0 or later, which depend on `SchematicHQ.Client` 1.7.2; `Microsoft.Extensions.AI`
10.10.0 or later):

| Package | Use |
|---|---|
| `SchematicHQ.Community.DependencyInjection` | `AddSchematic`, `AddSchematicNoOp`, `AddSchematicFusionCache` |
| `SchematicHQ.Community.AspNetCore` | `AddSchematicAspNetCore`, `ISchematicFlagContextResolver` (request identity) |
| `SchematicHQ.Community.Extensions.AI` | `UseSchematicUsageTracking`, `UseSchematicCreditLease`, `UseSchematicRequireFeature` |
| `SchematicHQ.Community.Testing` | `FakeSchematicGateClient`, `AddSchematicFake` (test projects only) |

The metering middlewares send inference events when `options.Inference` is set, and Track events when it is not.

### 3.1 Client registration

```csharp
if (!string.IsNullOrWhiteSpace(configuration["Schematic:ApiKey"]))
    services.AddSchematic(configuration["Schematic:ApiKey"]!);
else
    // Discards every event, so the pipeline below runs unchanged in dev and tests.
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
            return ValueTask.FromResult<SchematicFlagContext?>(null); // usage is not metered

        var userId = context.User.FindFirstValue("profile_id");
        var user = string.IsNullOrWhiteSpace(userId) ? new Dictionary<string, string>() : new() { ["id"] = userId };

        return ValueTask.FromResult<SchematicFlagContext?>(
            new SchematicFlagContext(new Dictionary<string, string> { ["id"] = tenantId }, user));
    }
}
```

Read claims only, so the resolver has no dependencies and works at any lifetime. When the app swaps the principal for
impersonation, read the claim the swap sets.

### 3.3 Chat pipeline (metered, never gated)

```csharp
public static class AiUsageMetering
{
    // Must equal the Schematic inference feature's event subtype.
    public const string EventName = "my-product-ai-usage";
}

services
    .AddChatClient(sp => /* provider client */)
    .UseOpenTelemetry()
    .UseFunctionInvocation()
    .UseSchematicUsageTracking(o => o.Inference = new()
    {
        EventName = AiUsageMetering.EventName,
        // Provider = "anthropic",
        // MapUsage = SchematicInferenceOptions.BucketedCacheUsageMapping,
    });
```

Behaviour with `Inference` set:

- One inference event per response, with the company and user, provider, request model (`ChatOptions.ModelId`, else
  the client's default), response model (`ChatResponse.ModelId`), the token counts and a fresh idempotency key.
- For streaming, usage is summed across updates and sent even if the consumer abandons the stream.
- Events are queued and sent in batches in the background, and flushed when the service provider is disposed. A send
  failure is logged and never fails the call. A response that reported no tokens sends nothing.
- Identity comes from the current request through the resolver. Outside a request it uses `o.FallbackContext`; when
  that is null too, the call is not metered.
- `Inference.EventName` is required here; building the pipeline throws without it.

**Placement.** Add the Schematic middleware after `UseFunctionInvocation()`, so it sits closer to the model and
sends one event per model call. Placed before it, a whole tool-calling exchange becomes one event.

**Provider.** Defaults to the inner client's `ChatClientMetadata.ProviderName`. Set `Provider` when that differs from
what the rates and cost lookups are keyed on. When neither is available the event is sent with provider `unknown`
and a warning is logged once; the same applies to a response with no model id and none requested.

**Token mapping.** Pick the one that matches what the adapter reports, and confirm it on a call that used the cache:

| `MapUsage` | For adapters where |
|---|---|
| `SchematicInferenceOptions.DefaultUsageMapping` (default) | `InputTokenCount` already includes `CachedInputTokenCount`, and `OutputTokenCount` includes `ReasoningTokenCount` (the Microsoft.Extensions.AI convention) |
| `SchematicInferenceOptions.BucketedCacheUsageMapping` | `InputTokenCount` excludes cache reads and writes, which arrive in `CachedInputTokenCount` or `AdditionalCounts` (`cache_read_input_tokens`, `cache_creation_input_tokens`, `cache_write_input_tokens`); it adds them to the input |

A custom mapping returns a `SchematicInferenceUsage`, or `null` to send nothing. Set its `Cost` only when the cost
cannot come from the rate sheet or list prices:

```csharp
o.Inference = new()
{
    EventName = AiUsageMetering.EventName,
    MapUsage = (usage, modelId) => new SchematicInferenceUsage(usage.InputTokenCount ?? 0, usage.OutputTokenCount ?? 0)
    {
        CachedInputTokens = usage.CachedInputTokenCount,
        Cost = costs.For(modelId, usage),
    },
};
```

Background jobs: either set `FallbackContext` on a separate pipeline built for that job's tenant, or accept that the
usage is not metered. A single shared `FallbackContext` attributes every background call to one company, which is
rarely correct in a multi-tenant app.

### 3.4 When the feature must be gated

Replace `UseSchematicUsageTracking` with one of these. The first argument is the feature's **flag key**:

```csharp
// Reserve credits before the call; settle with one inference event against the lease; release the remainder.
.UseSchematicCreditLease("my-product-ai-usage", o =>
{
    o.Inference = new();     // event name defaults to the entitlement's event subtype
    o.AllowOverdraft = true; // a spent balance or an unfunded hold runs the call and bills it as overage
    o.OnDenied = denial => { /* metric, or read denial.Response?.Entitlement?.CreditSettled */ return default; };
})

// Or check before, send after (post-paid; concurrent calls can overspend).
.UseSchematicRequireFeature("my-product-ai-usage", o => o.DenialBehavior = SchematicDenialBehavior.Allow)
.UseSchematicUsageTracking(o => o.Inference = new() { EventName = AiUsageMetering.EventName })
```

- The lease hold is sized from `EstimateUsage` and `Inference.CreditCost`, which prices the estimate with the
  entitlement's per-request rate and token rates. `model_rates` are not visible to it; a shortfall extends the lease
  before the event is sent.
- A denied call throws `SchematicFeatureDeniedException`. Add `app.UseSchematicFeatureDeniedResponses()` before
  routing to turn it into a 403 ProblemDetails; otherwise a Minimal API handler returns a 500.
- The SDK never throws from a flag check. When Schematic is unreachable it returns the flag's entry in
  `ClientOptions.FlagDefaults`, which is `false` unless set. To fail open during an outage, register with
  `AddSchematic(apiKey, o => o.FailOpenFor("my-product-ai-usage"))`. `FailurePolicy` only applies to a gate client
  that throws, and to lease calls.

### 3.5 Tests

Register the fake in the test host and assert on what it recorded:

```csharp
var fake = new FakeSchematicGateClient();
services.AddSchematicFake(fake); // replaces whatever gate client the app registered

// ... make a request that calls the model ...

var inference = fake.InferenceCalls.ShouldHaveSingleItem().Body;
inference.Event.ShouldBe(AiUsageMetering.EventName);
inference.Company!["id"].ShouldBe(orgId);
inference.Provider.ShouldBe("anthropic");
inference.InputTokens.ShouldBeGreaterThan(0);
fake.TrackCalls.ShouldBeEmpty();
fake.Reset(); // between tests on a shared host
```

Events settled against a lease are in `fake.LeaseInferenceCalls`. The fake allows every check until told otherwise
(`Deny`, `DenyForUser`), records checks, inference events, tracks, identifies and lease calls, and grants leases in
full unless `RejectLease` or `ThrowOnAcquireLease` is set. `RespondToCheck` scripts an entitlement with `QuantityRates`
for lease tests.

### 3.6 Fallback: Track events

Leave `Inference` unset. The default mapping sends `ai.input-tokens` and `ai.output-tokens` (plus `ai.{key}` for each
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

// Bound once: the pipeline is built at startup and the middleware captures its options.
var metering = new AiUsageMeteringOptions();
configuration.GetSection(AiUsageMeteringOptions.SectionName).Bind(metering);

services
    .AddChatClient(sp => /* provider client */)
    .UseSchematicUsageTracking(o =>
        o.MapUsage = (usage, modelId) => AiUsageMetering.MapUsage(usage, modelId, metering));
```

`UseSchematicCreditLease` takes the same `MapUsage`, and sizes its hold with `CreditCost` (event quantities times the
entitlement's consumption rate). Tests assert on `fake.TrackCalls`. Unit-test the weighting: `1000 in + 200 out × 5 =
2000`, and `5000 in + 0 out` equals `2500 in + 500 out` when `w = 5`.

### 3.7 Configuration keys

| Key | Default | Purpose |
|---|---|---|
| `Schematic:ApiKey` | none | A secret key. With no key the no-op client is used and nothing is sent. |
| `Schematic:AiUsage:OutputTokenWeight` | `5` | Track fallback only: output:input price ratio for weighted tokens. |

Add the API key to every deployed environment's secrets. When it is missing, the no-op client is used, so nothing is
metered and no error is raised.
