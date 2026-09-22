using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SchematicHQ.Client.RulesEngine;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.AI;

/// <summary>
/// Outcome of a passed gate. <see cref="Context"/> is <c>null</c> when no identity resolved and the call was
/// allowed through anyway. <see cref="Response"/> is <c>null</c> when there was no identity to check, or when
/// the check failed and <see cref="SchematicFailurePolicy.FailOpen"/> let the call through.
/// </summary>
internal sealed record AiGateResult(SchematicFlagContext? Context, CheckFlagWithEntitlementResponse? Response)
{
    /// <summary>The check said no (or could not be made) and the call was allowed through regardless.</summary>
    public bool Denied => Context is null || Response is { Value: false };
}

/// <summary>
/// The entitlement check shared by the gating middlewares: resolve the identity, check the flag, and
/// throw <see cref="SchematicFeatureDeniedException"/> when the call may not proceed.
/// </summary>
internal static class AiEntitlementGate
{
    /// <summary>
    /// Resolves the identity and checks the flag. <paramref name="tolerateDenial"/> lets a middleware allow a
    /// specific kind of denial on top of <see cref="SchematicAiOptions.DenialBehavior"/>; the lease client
    /// passes its overdraft rule here.
    /// </summary>
    public static async ValueTask<AiGateResult> CheckAsync(
        ISchematicGateClient schematic,
        string flagKey,
        SchematicAiOptions options,
        IHttpContextAccessor? httpContextAccessor,
        ILogger logger,
        CancellationToken cancellationToken,
        Func<CheckFlagWithEntitlementResponse, bool>? tolerateDenial = null)
    {
        var context = await AiFlagContextResolution.ResolveAsync(httpContextAccessor, options);
        if (context is null)
        {
            await DenyAsync(options, logger, flagKey, "no_schematic_context", null, null, tolerated: false);
            return new AiGateResult(null, null);
        }

        var outcome = await schematic.TryCheckFlagAsync(flagKey, context, options.FailurePolicy, logger, cancellationToken);
        if (outcome.Response is not { } response)
        {
            if (options.FailurePolicy == SchematicFailurePolicy.FailOpen)
                return new AiGateResult(context, null);

            throw new SchematicFeatureDeniedException(flagKey, "entitlement_check_failed", outcome.Failure);
        }

        if (!response.Value)
            await DenyAsync(options, logger, flagKey, response.Reason, context, response, tolerateDenial?.Invoke(response) ?? false);

        return new AiGateResult(context, response);
    }

    /// <summary>
    /// Reports a denial to <see cref="SchematicAiOptions.OnDenied"/>, then throws unless the caller tolerates it
    /// or <see cref="SchematicAiOptions.DenialBehavior"/> is <see cref="SchematicDenialBehavior.Allow"/>.
    /// </summary>
    public static async ValueTask DenyAsync(
        SchematicAiOptions options,
        ILogger logger,
        string flagKey,
        string? reason,
        SchematicFlagContext? context,
        CheckFlagWithEntitlementResponse? response,
        bool tolerated)
    {
        var allowed = tolerated || options.DenialBehavior == SchematicDenialBehavior.Allow;

        if (options.OnDenied is { } onDenied)
        {
            try
            {
                await onDenied(new SchematicAiDenial(flagKey, reason, context, response, allowed));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Schematic OnDenied callback for flag '{FlagKey}' threw; continuing.", flagKey);
            }
        }

        if (!allowed)
            throw new SchematicFeatureDeniedException(flagKey, reason);

        logger.LogInformation("Schematic would deny flag '{FlagKey}' ({Reason}); the call is allowed to proceed.", flagKey, reason);
    }
}
