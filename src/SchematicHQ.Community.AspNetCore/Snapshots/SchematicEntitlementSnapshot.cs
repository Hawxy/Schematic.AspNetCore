namespace SchematicHQ.Community.AspNetCore.Snapshots;

/// <summary>
/// The evaluation of a set of flags for one identity, taken at one moment. Built by
/// <see cref="ISchematicEntitlementSnapshotProvider"/> for code that needs many answers at once rather than
/// one gate per request.
/// </summary>
public sealed record SchematicEntitlementSnapshot(IReadOnlyDictionary<string, SchematicEntitlement> Entitlements)
{
    /// <summary>At least one check threw and its value came from the failure policy.</summary>
    public bool AnyCheckFailed => Entitlements.Values.Any(static e => e.CheckFailed);

    /// <summary>Whether the identity is entitled to <paramref name="flagKey"/>.</summary>
    /// <exception cref="KeyNotFoundException">The flag was not part of the snapshot.</exception>
    public bool IsEntitled(string flagKey) => this[flagKey].Value;

    /// <exception cref="KeyNotFoundException">The flag was not part of the snapshot.</exception>
    public SchematicEntitlement this[string flagKey]
        => Entitlements.TryGetValue(flagKey, out var entitlement)
            ? entitlement
            : throw new KeyNotFoundException($"Flag '{flagKey}' is not part of this snapshot.");
}
