namespace Schematic.Extensions.Quartz;

/// <summary>
/// Gates a Quartz job class behind a Schematic flag/entitlement. When the check denies (or the company
/// context cannot be resolved), the execution is vetoed and the trigger continues its schedule.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireFeatureAttribute : Attribute
{
    public RequireFeatureAttribute(string flagKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flagKey);
        FlagKey = flagKey;
    }

    public string FlagKey { get; }
}
