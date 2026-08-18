namespace Schematic.Extensions.Quartz;

/// <summary>
/// Emits a Schematic <c>Track</c> event after each successful (non-throwing, non-vetoed) execution of the
/// decorated Quartz job class. Use independently of <see cref="RequireFeatureAttribute"/> for jobs that
/// should emit usage without being gated.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class TrackFeatureAttribute : Attribute
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

    internal int? EffectiveQuantity => Quantity < 0 ? null : Quantity;
}
