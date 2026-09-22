using SchematicHQ.Client.RulesEngine;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.AI;

/// <summary>What a gating middleware does with a call the entitlement check would deny.</summary>
public enum SchematicDenialBehavior
{
    /// <summary>Throw <see cref="SchematicFeatureDeniedException"/> before the model is invoked. The default.</summary>
    Deny,

    /// <summary>
    /// Invoke the model anyway. The denial is still reported to <see cref="SchematicAiOptions.OnDenied"/> and
    /// logged, and usage is still tracked, so the app can record what was allowed through: an exhausted credit
    /// balance the customer is billed overage for, or a rollout in which gating is observed before it is
    /// enforced.
    /// </summary>
    Allow,
}

/// <summary>
/// A call the entitlement check would deny, as reported to <see cref="SchematicAiOptions.OnDenied"/>.
/// </summary>
/// <param name="FlagKey">The gated flag.</param>
/// <param name="Reason">
/// Schematic's reason, or one of the middleware's own: <c>no_schematic_context</c> when no identity resolved,
/// <c>insufficient_credits</c> when a credit hold could not be funded.
/// </param>
/// <param name="Context">The identity that was evaluated; <c>null</c> when none resolved.</param>
/// <param name="Response">
/// The check response, when the denial came from one. Its entitlement carries the credit balance for a
/// credit-backed flag.
/// </param>
/// <param name="Allowed">Whether the middleware went on to invoke the model regardless.</param>
public sealed record SchematicAiDenial(
    string FlagKey,
    string? Reason,
    SchematicFlagContext? Context,
    CheckFlagWithEntitlementResponse? Response,
    bool Allowed);
