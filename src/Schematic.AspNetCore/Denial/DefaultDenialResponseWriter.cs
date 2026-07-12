using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Schematic.AspNetCore.Denial;

/// <summary>
/// Default denial response: RFC 7807 ProblemDetails with status 403 and Schematic-specific extension fields
/// (<c>featureId</c>, <c>accessDeniedReason</c>, <c>requestedUsage</c>, <c>requestedValue</c>).
/// </summary>
internal static class DefaultDenialResponseWriter
{
    public static Task WriteAsync(HttpContext http, SchematicDenialContext denial)
    {
        const int statusCode = StatusCodes.Status403Forbidden;

        var problem = new ProblemDetails
        {
            Title = "Entitlement denied",
            Status = statusCode,
            Detail = $"Customer is not entitled to feature '{denial.FeatureId}'.",
        };

        problem.Extensions["featureId"] = denial.FeatureId;
        if (denial.Reason is { } reason)
            problem.Extensions["accessDeniedReason"] = reason;
        if (denial.RequestedUsage is { } ru)
            problem.Extensions["requestedUsage"] = ru;
        if (denial.RequestedValue is { } rv)
            problem.Extensions["requestedValue"] = rv;

        return Results.Problem(problem).ExecuteAsync(http);
    }
}
