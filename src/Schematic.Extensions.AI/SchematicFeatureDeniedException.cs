namespace Schematic.Extensions.AI;

/// <summary>
/// Thrown by <c>UseSchematicRequireFeature</c> when the caller is not entitled to the gated feature,
/// when no Schematic identity can be resolved, or when the check fails under
/// <c>SchematicFailurePolicy.FailClosed</c> (with the original error as <see cref="Exception.InnerException"/>).
/// </summary>
public sealed class SchematicFeatureDeniedException : Exception
{
    public SchematicFeatureDeniedException(string flagKey, string? reason, Exception? innerException = null)
        : base($"Access to feature '{flagKey}' was denied{(reason is null ? "." : $": {reason}")}", innerException)
    {
        FlagKey = flagKey;
        Reason = reason;
    }

    public string FlagKey { get; }

    public string? Reason { get; }
}
