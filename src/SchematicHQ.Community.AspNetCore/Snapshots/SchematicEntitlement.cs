using SchematicHQ.Client;

namespace SchematicHQ.Community.AspNetCore.Snapshots;

/// <summary>
/// One flag's evaluation inside a <see cref="SchematicEntitlementSnapshot"/>.
/// </summary>
/// <param name="FlagKey">The flag that was checked.</param>
/// <param name="Value">
/// Whether the identity is entitled. When <paramref name="CheckFailed"/> is <c>true</c> this is the
/// <see cref="SchematicEntitlementSnapshotOptions.FailurePolicy"/> answer, not Schematic's.
/// </param>
/// <param name="Reason">Schematic's explanation of the result, or the failure message.</param>
/// <param name="Entitlement">
/// The entitlement backing the flag when Schematic reported one: allocation, usage, credit balance.
/// </param>
/// <param name="CheckFailed">The check threw and <paramref name="Value"/> came from the failure policy.</param>
public sealed record SchematicEntitlement(
    string FlagKey,
    bool Value,
    string? Reason,
    RulesengineFeatureEntitlement? Entitlement,
    bool CheckFailed);
