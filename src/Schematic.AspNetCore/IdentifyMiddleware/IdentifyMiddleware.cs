using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schematic.AspNetCore.Options;
using Schematic.AspNetCore.Resolvers;
using Schematic.DependencyInjection;

namespace Schematic.AspNetCore.IdentifyMiddleware;

/// <summary>
/// Calls Schematic <c>Identify</c> for each request whose <see cref="ISchematicIdentifyContextResolver"/>
/// resolves an identity. Skips OPTIONS preflight. An Identify failure is logged and never fails the
/// request. When <see cref="SchematicAspNetCoreOptions.IdentifyDeduplicationWindow"/> is set, identical
/// identities within the window are sent only once.
/// </summary>
public class IdentifyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ISchematicGateClient _client;
    private readonly IOptions<SchematicAspNetCoreOptions> _options;
    private readonly ILogger<IdentifyMiddleware> _logger;
    private readonly MemoryCache _recentIdentities = new(new MemoryCacheOptions());

    public IdentifyMiddleware(
        RequestDelegate next,
        ISchematicGateClient client,
        IOptions<SchematicAspNetCoreOptions> options,
        ILogger<IdentifyMiddleware> logger)
    {
        _next = next;
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ISchematicIdentifyContextResolver resolver)
    {
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var result = await resolver.ResolveAsync(context, context.RequestAborted);

        if (result is not null && ShouldIdentify(result))
        {
            try
            {
                _client.Identify(result.Keys, result.Company, result.Name, result.Traits);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Schematic identify failed; request is unaffected.");
            }
        }

        await _next(context);
    }

    private bool ShouldIdentify(SchematicIdentifyContext context)
    {
        if (_options.Value.IdentifyDeduplicationWindow is not { } window || window <= TimeSpan.Zero)
            return true;

        var key = BuildDedupKey(context);
        if (_recentIdentities.TryGetValue(key, out _))
            return false;

        _recentIdentities.Set(key, true, window);
        return true;
    }

    private static string BuildDedupKey(SchematicIdentifyContext context)
    {
        var sb = new StringBuilder();
        AppendKeys(sb, context.Keys);
        sb.Append('#').Append(context.Name);

        if (context.Company is { } company)
        {
            sb.Append('#');
            AppendKeys(sb, company.Keys);
            sb.Append('#').Append(company.Name);
        }

        return sb.ToString();
    }

    private static void AppendKeys(StringBuilder sb, Dictionary<string, string>? keys)
    {
        if (keys is null)
            return;

        foreach (var (key, value) in keys.OrderBy(p => p.Key, StringComparer.Ordinal))
            sb.Append(key).Append('=').Append(value).Append(';');
    }
}
