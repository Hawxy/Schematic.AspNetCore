using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Schematic.AspNetCore.Attributes;
using Schematic.AspNetCore.Filters;

namespace Schematic.AspNetCore;

/// <summary>
/// Endpoint convention extensions for declaring Schematic gating and tracking on minimal API endpoints,
/// route groups, or controller route mappings.
/// </summary>
public static class SchematicEndpointExtensions
{
    /// <summary>
    /// Gates the endpoint behind a Schematic flag. The pipeline must include
    /// <see cref="RequireFeatureFilter"/> (registered via <c>AddSchematicFilters</c> or by calling
    /// <c>AddEndpointFilter&lt;RequireFeatureFilter&gt;</c> upstream).
    /// </summary>
    public static TBuilder RequireFeature<TBuilder>(
        this TBuilder builder,
        string flagKey,
        bool track = false)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new RequireFeatureAttribute(flagKey) { Track = track });
        return builder;
    }

    /// <summary>
    /// Tracks a Schematic event on successful (status &lt; 400) responses for this endpoint. The pipeline
    /// must include <see cref="TrackFeatureFilter"/>.
    /// </summary>
    public static TBuilder TrackFeature<TBuilder>(
        this TBuilder builder,
        string eventName,
        int? quantity = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new TrackFeatureAttribute(eventName) { Quantity = quantity ?? -1 });
        return builder;
    }

    /// <summary>
    /// Adds <see cref="RequireFeatureFilter"/> and <see cref="TrackFeatureFilter"/> to the endpoint
    /// pipeline in the correct order. Use on minimal API groups or after <c>MapControllers()</c> to wire
    /// gating + tracking everywhere with one call.
    /// </summary>
    public static TBuilder AddSchematicFilters<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddEndpointFilter<TBuilder, RequireFeatureFilter>();
        builder.AddEndpointFilter<TBuilder, TrackFeatureFilter>();
        return builder;
    }

    /// <summary>
    /// Verifies the Schematic webhook signature before the endpoint runs; responds 401 when the signature
    /// headers are missing or invalid. Requires <c>SchematicAspNetCoreOptions.WebhookSecret</c>.
    /// </summary>
    public static TBuilder RequireSchematicWebhookSignature<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddEndpointFilter<TBuilder, SchematicWebhookSignatureFilter>();
        return builder;
    }
}
