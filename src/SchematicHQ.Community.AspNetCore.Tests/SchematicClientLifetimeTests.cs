using Microsoft.Extensions.DependencyInjection;
using SchematicHQ.Community.DependencyInjection;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

internal sealed class SchematicClientLifetimeTests
{
    [Test]
    public async Task Resolved_client_is_registered_for_shutdown_flush_and_provider_disposal_completes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchematic("test-api-key");

        var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<SchematicHQ.Client.Schematic>();

        sp.GetRequiredService<SchematicClientLifetime>().Client.ShouldBeSameAs(client);

        await sp.DisposeAsync();
    }

    [Test]
    public async Task Disposal_without_resolving_the_client_skips_shutdown()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchematic("test-api-key");

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<SchematicClientLifetime>().Client.ShouldBeNull();

        await sp.DisposeAsync();
    }
}
