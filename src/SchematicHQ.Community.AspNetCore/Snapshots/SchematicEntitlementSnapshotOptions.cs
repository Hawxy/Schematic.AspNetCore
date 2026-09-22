using SchematicHQ.Community.AspNetCore.Options;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.AspNetCore.Snapshots;

public sealed class SchematicEntitlementSnapshotOptions
{
    /// <summary>
    /// What a flag reads as when its check throws. <see cref="SchematicFailurePolicy.FailClosed"/> reports
    /// <c>false</c>; <see cref="SchematicFailurePolicy.FailOpen"/> reports <c>true</c>. <c>null</c> (the default)
    /// follows <see cref="SchematicAspNetCoreOptions.FailurePolicy"/>, so the snapshot answers the way the gates
    /// do. Either way the entry carries <see cref="SchematicEntitlement.CheckFailed"/> so the caller can tell.
    /// </summary>
    public SchematicFailurePolicy? FailurePolicy { get; set; }
}
