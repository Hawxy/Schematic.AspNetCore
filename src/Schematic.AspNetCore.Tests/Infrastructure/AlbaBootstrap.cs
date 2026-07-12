using Alba;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schematic.AspNetCore.Internal;
using Schematic.AspNetCore.Resolvers;
using TUnit.Core.Interfaces;

namespace Schematic.AspNetCore.Tests.Infrastructure;

internal sealed class AlbaBootstrap : IAsyncInitializer, IAsyncDisposable
{
    public IAlbaHost Host { get; private set; } = null!;
    public FakeGateClient FakeClient { get; } = new();
    public StubFlagContextResolver Resolver { get; } = new();

    public async Task InitializeAsync()
    {
        Host = await AlbaHost.For<Program>(webHost =>
        {
            webHost.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ISchematicGateClient>(FakeClient));
                services.AddSingleton<ISchematicFlagContextResolver>(Resolver);
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Host is not null)
            await Host.DisposeAsync();
    }
}
