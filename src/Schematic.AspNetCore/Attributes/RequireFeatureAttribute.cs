namespace Schematic.AspNetCore.Attributes;

/// <summary>
/// Endpoint metadata declaring that a route is gated behind a Schematic feature/flag. Read by
/// <c>RequireFeatureFilter</c>.
/// </summary>
public interface IRequireFeatureMetadata
{
    string FlagKey { get; }
    bool Track { get; }
    double? RequestedUsage { get; }
    string? RequestedValue { get; }
}

/// <summary>
/// Gates a controller class or action behind a Schematic flag/entitlement.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireFeatureAttribute : Attribute, IRequireFeatureMetadata
{
    public RequireFeatureAttribute(string flagKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flagKey);
        FlagKey = flagKey;
    }

    public string FlagKey { get; }

    /// <summary>
    /// When <c>true</c>, the gate also tracks an event named after <see cref="FlagKey"/> on a successful
    /// (status &lt; 400) response. Re-uses the <c>CheckFlagWithEntitlement</c> result so the SDK is called once.
    /// </summary>
    public bool Track { get; set; }

    /// <summary>
    /// Optional requested usage quantity passed to the denial context for client-facing diagnostics.
    /// Use <see cref="double.NaN"/> (the default) to leave unset.
    /// </summary>
    public double RequestedUsage { get; set; } = double.NaN;

    /// <summary>
    /// Optional requested enum value passed to the denial context for client-facing diagnostics.
    /// </summary>
    public string? RequestedValue { get; set; }

    double? IRequireFeatureMetadata.RequestedUsage =>
        double.IsNaN(RequestedUsage) ? null : RequestedUsage;
}
