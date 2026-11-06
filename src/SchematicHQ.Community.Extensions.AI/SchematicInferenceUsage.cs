namespace SchematicHQ.Community.Extensions.AI;

/// <summary>
/// The token counts of one model call, as sent in a Schematic inference event.
/// </summary>
/// <param name="InputTokens">
/// Every input token, including those read from and written to a prompt cache. Schematic subtracts the two
/// cache counts and prices the remainder at the input rate.
/// </param>
/// <param name="OutputTokens">Every output token, including reasoning tokens.</param>
public sealed record SchematicInferenceUsage(long InputTokens, long OutputTokens)
{
    /// <summary>Input tokens read from a prompt cache; a subset of <see cref="InputTokens"/>.</summary>
    public long? CachedInputTokens { get; init; }

    /// <summary>Input tokens written to a prompt cache; a subset of <see cref="InputTokens"/>.</summary>
    public long? CacheCreationInputTokens { get; init; }

    /// <summary>
    /// Reasoning tokens. Schematic prices these on top of <see cref="OutputTokens"/>, so leave the plan's
    /// reasoning rate at zero for providers that count reasoning inside output.
    /// </summary>
    public long? ReasoningTokens { get; init; }

    /// <summary>
    /// What the call cost you, in USD. When <c>null</c>, Schematic uses its list price for the provider and
    /// model, or records the call as unpriced. Does not change the credits burned.
    /// </summary>
    public decimal? Cost { get; init; }
}
