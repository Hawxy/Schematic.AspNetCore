using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchematicHQ.Community.AspNetCore.Denial;
using SchematicHQ.Community.AspNetCore.Options;

namespace SchematicHQ.Community.Extensions.AI;

public static class SchematicFeatureDeniedApplicationBuilderExtensions
{
    /// <summary>
    /// Middleware that catches a <see cref="SchematicFeatureDeniedException"/> from the rest of the pipeline and
    /// writes the gate's denial response: <see cref="SchematicAspNetCoreOptions.OnDenied"/> when set, otherwise
    /// 403 ProblemDetails with <c>featureId</c> and <c>accessDeniedReason</c>. Without it a denied AI call inside
    /// a handler surfaces as a 500. Place it early, before routing. If the response has already started the
    /// exception is rethrown, since nothing can be written any more.
    /// </summary>
    public static IApplicationBuilder UseSchematicFeatureDeniedResponses(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices.GetRequiredService<IOptions<SchematicAspNetCoreOptions>>().Value;
        return app.Use(async (http, next) =>
        {
            try
            {
                await next(http);
            }
            catch (SchematicFeatureDeniedException denied) when (!http.Response.HasStarted)
            {
                http.Response.Clear();
                await SchematicDenialResponse.WriteAsync(http, new SchematicDenialContext(denied.FlagKey, denied.Reason), options);
            }
        });
    }
}
