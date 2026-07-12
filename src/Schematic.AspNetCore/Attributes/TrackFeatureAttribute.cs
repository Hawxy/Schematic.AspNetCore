namespace Schematic.AspNetCore.Attributes;

/// <summary>
/// Endpoint metadata declaring that a route emits a Schematic <c>Track</c> event on success. Read by
/// <c>TrackFeatureFilter</c>
/// </summary>
public interface ITrackFeatureMetadata
{
    string EventName { get; }
    int? Quantity { get; }
}

/// <summary>
/// Tracks a Schematic event on successful (status &lt; 400) responses for the decorated controller class
/// or action. Use independently of <see cref="RequireFeatureAttribute"/> for routes that should emit usage
/// without being gated.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class TrackFeatureAttribute : Attribute, ITrackFeatureMetadata
{
    public TrackFeatureAttribute(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        EventName = eventName;
    }

    public string EventName { get; }

    /// <summary>
    /// Quantity to send with the Track event. A negative value (the default of <c>-1</c>) means "no
    /// quantity" and is omitted from the SDK call.
    /// </summary>
    public int Quantity { get; set; } = -1;

    int? ITrackFeatureMetadata.Quantity => Quantity < 0 ? null : Quantity;
}
