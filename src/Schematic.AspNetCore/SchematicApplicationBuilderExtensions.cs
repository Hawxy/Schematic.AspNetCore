using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schematic.AspNetCore.IdentifyMiddleware;
using Schematic.AspNetCore.Resolvers;

namespace Schematic.AspNetCore;

public static class SchematicApplicationBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="IdentifyMiddleware"/> in the request pipeline. Requires an
    /// <see cref="ISchematicIdentifyContextResolver"/> to be registered in DI.
    /// </summary>
    public static IApplicationBuilder UseSchematicIdentify(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.ApplicationServices.GetService<ISchematicIdentifyContextResolver>() is null)
        {
            throw new InvalidOperationException(
                $"{nameof(UseSchematicIdentify)} requires an {nameof(ISchematicIdentifyContextResolver)} " +
                $"to be registered. Call AddSchematicIdentifyContextResolver<T>() during service configuration.");
        }

        return app.UseMiddleware<IdentifyMiddleware.IdentifyMiddleware>();
    }
}
