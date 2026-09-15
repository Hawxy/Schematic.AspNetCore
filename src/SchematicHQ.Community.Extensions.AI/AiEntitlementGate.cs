using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SchematicHQ.Client.RulesEngine;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.AI;

/// <summary>
/// Outcome of a passed gate. <see cref="Response"/> is <c>null</c> when the check itself failed and
/// <see cref="SchematicFailurePolicy.FailOpen"/> let the call through.
/// </summary>
internal sealed record AiGateResult(SchematicFlagContext Context, CheckFlagWithEntitlementResponse? Response);

/// <summary>
/// The entitlement check shared by the gating middlewares: resolve the identity, check the flag, and
/// throw <see cref="SchematicFeatureDeniedException"/> when the call may not proceed.
/// </summary>
internal static class AiEntitlementGate
{
    public static async ValueTask<AiGateResult> CheckAsync(
        ISchematicGateClient schematic,
        string flagKey,
        SchematicAiOptions options,
        IHttpContextAccessor? httpContextAccessor,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var context = await AiFlagContextResolution.ResolveAsync(httpContextAccessor, options)
            ?? throw new SchematicFeatureDeniedException(flagKey, "no_schematic_context");

        CheckFlagWithEntitlementResponse response;
        try
        {
            response = await schematic.CheckFlagWithEntitlementAsync(flagKey, context.Company, context.User, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Schematic entitlement check for flag '{FlagKey}' failed; applying {FailurePolicy}.",
                flagKey, options.FailurePolicy);

            if (options.FailurePolicy == SchematicFailurePolicy.FailOpen)
                return new AiGateResult(context, null);

            throw new SchematicFeatureDeniedException(flagKey, "entitlement_check_failed", ex);
        }

        if (!response.Value)
            throw new SchematicFeatureDeniedException(flagKey, response.Reason);

        return new AiGateResult(context, response);
    }
}
