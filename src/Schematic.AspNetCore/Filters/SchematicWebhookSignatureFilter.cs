using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schematic.AspNetCore.Options;
using SchematicHQ.Client.Webhooks.WebhookUtils;

namespace Schematic.AspNetCore.Filters;

/// <summary>
/// Endpoint filter that verifies the Schematic webhook signature headers
/// (<c>X-Schematic-Webhook-Signature</c> / <c>X-Schematic-Webhook-Timestamp</c>) against the request body
/// before the endpoint runs. Responds 401 ProblemDetails when the headers are missing or the signature
/// does not match. Requires <see cref="SchematicAspNetCoreOptions.WebhookSecret"/> to be configured.
/// </summary>
public sealed class SchematicWebhookSignatureFilter : IEndpointFilter
{
    private readonly IOptions<SchematicAspNetCoreOptions> _options;
    private readonly ILogger<SchematicWebhookSignatureFilter> _logger;

    public SchematicWebhookSignatureFilter(
        IOptions<SchematicAspNetCoreOptions> options,
        ILogger<SchematicWebhookSignatureFilter> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var secret = _options.Value.WebhookSecret;
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException(
                $"{nameof(SchematicAspNetCoreOptions)}.{nameof(SchematicAspNetCoreOptions.WebhookSecret)} must be configured to verify Schematic webhook signatures.");

        var request = http.Request;
        string? signature = request.Headers[WebhookVerifier.WebhookSignatureHeader];
        string? timestamp = request.Headers[WebhookVerifier.WebhookTimestampHeader];
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp))
        {
            _logger.LogWarning("Rejected Schematic webhook request without signature headers.");
            return await WriteUnauthorizedAsync(http, "Missing webhook signature headers.");
        }

        // Buffer the body so the endpoint (and model binding) can still read it after verification.
        request.EnableBuffering();
        string body;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
            body = await reader.ReadToEndAsync(http.RequestAborted);
        request.Body.Position = 0;

        try
        {
            WebhookVerifier.VerifySignature(body, signature, timestamp, secret);
        }
        catch (WebhookSignatureException ex)
        {
            _logger.LogWarning(ex, "Rejected Schematic webhook request with invalid signature.");
            return await WriteUnauthorizedAsync(http, "Invalid webhook signature.");
        }

        return await next(context);
    }

    private static async Task<object?> WriteUnauthorizedAsync(HttpContext http, string detail)
    {
        var problem = new ProblemDetails
        {
            Title = "Webhook signature verification failed",
            Status = StatusCodes.Status401Unauthorized,
            Detail = detail,
        };

        await Results.Problem(problem).ExecuteAsync(http);
        return Results.Empty;
    }
}
