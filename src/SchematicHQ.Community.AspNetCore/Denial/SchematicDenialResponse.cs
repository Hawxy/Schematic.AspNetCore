using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchematicHQ.Community.AspNetCore.Options;

namespace SchematicHQ.Community.AspNetCore.Denial;

/// <summary>
/// Writes the response for a denied entitlement the way the gate filter does: through
/// <see cref="SchematicAspNetCoreOptions.OnDenied"/> when set, otherwise the default 403 ProblemDetails.
/// Public so a denial raised outside the filter (an AI middleware, a handler) is answered with the same body
/// an endpoint gate would have produced.
/// </summary>
public static class SchematicDenialResponse
{
    /// <param name="http">The request to answer.</param>
    /// <param name="denial">What was denied and why.</param>
    /// <param name="options">
    /// The options to honour. <c>null</c> resolves them from the request's services, which requires
    /// <c>AddSchematicAspNetCore()</c>.
    /// </param>
    public static Task WriteAsync(HttpContext http, SchematicDenialContext denial, SchematicAspNetCoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(denial);

        options ??= http.RequestServices.GetRequiredService<IOptions<SchematicAspNetCoreOptions>>().Value;
        return options.OnDenied is { } onDenied
            ? onDenied(http, denial)
            : DefaultDenialResponseWriter.WriteAsync(http, denial);
    }
}
