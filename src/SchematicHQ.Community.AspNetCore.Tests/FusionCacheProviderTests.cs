using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Client;
using SchematicHQ.Client.Cache;
using Shouldly;
using ZiggyCreatures.Caching.Fusion;

namespace SchematicHQ.Community.AspNetCore.Tests;

internal sealed class FusionCacheProviderTests
{
    private static FusionCacheProvider CreateProvider()
        => new(new ZiggyCreatures.Caching.Fusion.FusionCache(new FusionCacheOptions()));

    [Test]
    public async Task Set_then_Get_roundtrips()
    {
        var provider = CreateProvider();

        await provider.Set("flag", true);

        (await provider.Get<bool>("flag")).ShouldBeTrue();
    }

    [Test]
    public async Task Get_returns_default_for_missing_key()
    {
        var provider = CreateProvider();

        (await provider.Get<string>("missing")).ShouldBeNull();
        (await provider.Get<bool>("missing")).ShouldBeFalse();
    }

    [Test]
    public async Task GetOrSet_invokes_factory_once_and_caches()
    {
        var provider = CreateProvider();
        var factoryCalls = 0;

        for (var i = 0; i < 3; i++)
        {
            var value = await provider.GetOrSet("key", _ =>
            {
                factoryCalls++;
                return Task.FromResult("value");
            });
            value.ShouldBe("value");
        }

        factoryCalls.ShouldBe(1);
    }

    [Test]
    public async Task Ttl_override_expires_the_entry()
    {
        var provider = CreateProvider();

        await provider.Set("short-lived", "value", ttlOverride: TimeSpan.FromMilliseconds(50));
        (await provider.Get<string>("short-lived")).ShouldBe("value");

        await Task.Delay(250);

        (await provider.Get<string>("short-lived")).ShouldBeNull();
    }

    [Test]
    public void Default_ttl_matches_the_sdk_local_cache_default()
    {
        CreateProvider().DefaultTtl.ShouldBe(LocalCache.DEFAULT_CACHE_TTL);
    }

    [Test]
    public async Task Entries_without_ttl_override_expire_after_the_default_ttl()
    {
        var provider = new FusionCacheProvider(
            new ZiggyCreatures.Caching.Fusion.FusionCache(new FusionCacheOptions()),
            defaultTtl: TimeSpan.FromMilliseconds(50));

        await provider.Set("key", "value");
        (await provider.Get<string>("key")).ShouldBe("value");

        await Task.Delay(250);

        (await provider.Get<string>("key")).ShouldBeNull();
    }

    [Test]
    public async Task Delete_removes_the_entry()
    {
        var provider = CreateProvider();
        await provider.Set("key", "value");

        (await provider.Delete("key")).ShouldBeTrue();

        (await provider.Get<string>("key")).ShouldBeNull();
    }

    [Test]
    public async Task AddSchematic_wires_registered_cache_provider_into_client_options()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache();
        services.AddSchematicFusionCache();
        services.AddSchematic("test-api-key");

        await using var sp = services.BuildServiceProvider();
        _ = sp.GetRequiredService<SchematicHQ.Client.Schematic>();

        sp.GetRequiredService<IOptions<ClientOptions>>().Value.CacheProvider
            .ShouldBeOfType<FusionCacheProvider>();
    }

    [Test]
    public async Task AddSchematic_respects_explicitly_configured_cache_provider()
    {
        var explicitProvider = CreateProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache();
        services.AddSchematicFusionCache();
        services.AddSchematic("test-api-key", o => o.CacheProvider = explicitProvider);

        await using var sp = services.BuildServiceProvider();
        _ = sp.GetRequiredService<SchematicHQ.Client.Schematic>();

        sp.GetRequiredService<IOptions<ClientOptions>>().Value.CacheProvider
            .ShouldBeSameAs(explicitProvider);
    }
}
