using System.Text;

namespace SchematicHQ.Community.DependencyInjection;

/// <summary>
/// Formats identity keys as text for cache keys, dedup keys and log lines: <c>key=value</c> pairs in ordinal
/// key order, so two dictionaries with the same content produce the same string.
/// </summary>
public static class SchematicKeyString
{
    public const char DefaultSeparator = ';';

    public static void Append(StringBuilder builder, IReadOnlyDictionary<string, string> keys, char separator = DefaultSeparator)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
            return;

        // The common identity is a single "id" key; skip the sort for it.
        if (keys.Count == 1)
        {
            foreach (var (key, value) in keys)
                builder.Append(key).Append('=').Append(value);
            return;
        }

        var first = true;
        foreach (var (key, value) in keys.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            if (!first)
                builder.Append(separator);
            builder.Append(key).Append('=').Append(value);
            first = false;
        }
    }

    public static string Format(IReadOnlyDictionary<string, string> keys, char separator = DefaultSeparator)
    {
        var builder = new StringBuilder();
        Append(builder, keys, separator);
        return builder.ToString();
    }

    /// <summary>Both dimensions of a flag context: <c>c:&lt;company keys&gt;|u:&lt;user keys&gt;</c>.</summary>
    public static string Format(SchematicFlagContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var builder = new StringBuilder("c:");
        Append(builder, context.Company);
        builder.Append("|u:");
        Append(builder, context.User);
        return builder.ToString();
    }
}
