using Microsoft.Extensions.DependencyInjection;
using SchematicHQ.Community.AspNetCore.Tests.Infrastructure;
using SchematicHQ.Community.DependencyInjection;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

/// <summary>
/// AddSchematicNoOp exists so an app with no API key still builds its full graph — the filters and
/// middlewares all take ISchematicGateClient, so without a stand-in they fail at resolve time and those
/// code paths only ever run in deployed environments.
/// </summary>
internal sealed class NoOpGateClientTests
{
    private static ISchematicGateClient Resolve(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider().GetRequiredService<ISchematicGateClient>();
    }

    [Test]
    public async Task Registers_a_gate_client_without_an_api_key()
    {
        var client = Resolve(s => s.AddSchematicNoOp());

        client.ShouldNotBeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Allows_gated_features_by_default()
    {
        var client = Resolve(s => s.AddSchematicNoOp());

        var response = await client.CheckFlagWithEntitlementAsync("ai-chat", new(), new(), CancellationToken.None);

        response.Value.ShouldBeTrue();
        response.FlagKey.ShouldBe("ai-chat");
    }

    [Test]
    public async Task Denies_gated_features_when_asked_to()
    {
        var client = Resolve(s => s.AddSchematicNoOp(allowAll: false));

        var response = await client.CheckFlagWithEntitlementAsync("ai-chat", new(), new(), CancellationToken.None);

        response.Value.ShouldBeFalse();
    }

    [Test]
    public async Task Discards_track_and_identify_without_throwing()
    {
        var client = Resolve(s => s.AddSchematicNoOp());

        client.Track("ai.input-tokens", new(), new(), new(), 120);
        client.Identify(new() { ["id"] = "acme" }, null, "Acme", null);

        await Task.CompletedTask;
    }

    /// <summary>
    /// TryAdd semantics: a test's own fake still wins, so opting into the no-op in a shared host builder
    /// does not stop an individual test from asserting against calls.
    /// </summary>
    [Test]
    public async Task Leaves_an_already_registered_client_in_place()
    {
        var fake = new FakeGateClient();

        var client = Resolve(s =>
        {
            s.AddSingleton<ISchematicGateClient>(fake);
            s.AddSchematicNoOp();
        });

        client.ShouldBeSameAs(fake);
        await Task.CompletedTask;
    }
}
