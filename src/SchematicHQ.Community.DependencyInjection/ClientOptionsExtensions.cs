using SchematicHQ.Client;

namespace SchematicHQ.Community.DependencyInjection;

public static class ClientOptionsExtensions
{
    /// <summary>
    /// Makes the given flags evaluate <c>true</c> when Schematic cannot be reached. The SDK never throws from a
    /// flag check: on a transport failure it answers the flag's entry in <see cref="ClientOptions.FlagDefaults"/>,
    /// which is <c>false</c> for any flag not listed there. So the <c>FailurePolicy</c> options on the filters
    /// and middlewares only ever see exceptions from a custom gate client, and this is the setting that
    /// decides what an outage does to a gated feature. A genuine deny is a successful evaluation and is not
    /// affected.
    /// </summary>
    public static ClientOptions FailOpenFor(this ClientOptions options, params IEnumerable<string> flagKeys)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(flagKeys);

        options.FlagDefaults ??= new Dictionary<string, bool>();
        foreach (var flagKey in flagKeys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(flagKey);
            options.FlagDefaults[flagKey] = true;
        }

        return options;
    }
}
