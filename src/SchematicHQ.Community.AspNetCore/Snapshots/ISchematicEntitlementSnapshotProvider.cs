using Microsoft.AspNetCore.Http;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.AspNetCore.Snapshots;

/// <summary>
/// Evaluates a set of flags for one identity in one go, through the same <see cref="ISchematicGateClient"/>
/// seam and identity resolution the endpoint gates use, so the answers agree with what the gates enforce.
/// Registered by <c>AddSchematicEntitlementSnapshots</c>.
/// </summary>
public interface ISchematicEntitlementSnapshotProvider
{
    /// <summary>
    /// Evaluates <paramref name="flagKeys"/> for <paramref name="context"/>. A check that throws does not
    /// fail the snapshot: its entry takes the failure policy's value and is marked
    /// <see cref="SchematicEntitlement.CheckFailed"/>.
    /// </summary>
    Task<SchematicEntitlementSnapshot> GetAsync(
        SchematicFlagContext context,
        IReadOnlyCollection<string> flagKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates <paramref name="flagKeys"/> for the request's identity, resolved the way the gate filter
    /// resolves it. Returns <c>null</c> when no identity resolves — the gates would not run on such a
    /// request either.
    /// </summary>
    Task<SchematicEntitlementSnapshot?> GetAsync(
        HttpContext http,
        IReadOnlyCollection<string> flagKeys,
        CancellationToken cancellationToken = default);
}
