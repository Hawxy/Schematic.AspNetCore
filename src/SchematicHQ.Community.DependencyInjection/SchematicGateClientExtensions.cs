using Microsoft.Extensions.Logging;
using SchematicHQ.Client.RulesEngine;

namespace SchematicHQ.Community.DependencyInjection;

/// <summary>
/// The outcome of a flag check that was allowed to fail: either Schematic's <see cref="Response"/>, or the
/// <see cref="Failure"/> the check threw.
/// </summary>
public sealed record SchematicCheckOutcome(CheckFlagWithEntitlementResponse? Response, Exception? Failure)
{
    public bool Failed => Failure is not null;
}

public static class SchematicGateClientExtensions
{
    /// <summary>
    /// Checks a flag and turns a thrown check into an outcome instead of an exception, logging it at Error
    /// together with the <paramref name="policy"/> the caller is about to apply. Cancellation still propagates.
    /// Shared by every gating integration so a failed check is logged once, the same way, everywhere.
    /// </summary>
    public static async ValueTask<SchematicCheckOutcome> TryCheckFlagAsync(
        this ISchematicGateClient client,
        string flagKey,
        SchematicFlagContext context,
        SchematicFailurePolicy policy,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.CheckFlagWithEntitlementAsync(flagKey, context.Company, context.User, cancellationToken);
            return new SchematicCheckOutcome(response, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Schematic entitlement check for flag '{FlagKey}' failed; applying {FailurePolicy}.",
                flagKey, policy);
            return new SchematicCheckOutcome(null, ex);
        }
    }
}
