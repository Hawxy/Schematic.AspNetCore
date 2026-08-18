using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Extensions.Quartz;

public sealed class SchematicQuartzOptions
{
    /// <summary>
    /// How gated jobs behave when the entitlement check throws (network/SDK error). Defaults to
    /// <see cref="SchematicFailurePolicy.FailClosed"/> (the execution is vetoed).
    /// </summary>
    public SchematicFailurePolicy FailurePolicy { get; set; } = SchematicFailurePolicy.FailClosed;
}
