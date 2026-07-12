using Microsoft.AspNetCore.Http;
using Schematic.AspNetCore.Resolvers;

namespace Schematic.AspNetCore.IdentifyMiddleware;

public class IdentifyMiddleware
{
    private readonly RequestDelegate _next;

    public IdentifyMiddleware(RequestDelegate next)
        => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ISchematicIdentifyContextResolver resolver,
        SchematicHQ.Client.Schematic client)
    {
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var result = await resolver.ResolveAsync(context, context.RequestAborted);

        if (result is not null)
        {
            client.Identify(result.Keys, result.Company, result.Name, result.Traits);
        }

        await _next(context);
    }
}
