using SchematicHQ.Client.Cache;
using ZiggyCreatures.Caching.Fusion;

namespace Schematic.DependencyInjection.FusionCache;

/// <summary>
/// <see cref="ICacheProvider"/> backed by a FusionCache instance. Every entry gets an explicit duration:
/// the per-call TTL override when given, otherwise <see cref="DefaultTtl"/> — which itself defaults to
/// the SDK's <see cref="LocalCache.DEFAULT_CACHE_TTL"/> so caching behaves like the SDK's built-in cache.
/// </summary>
public sealed class FusionCacheProvider : ICacheProvider
{
    private readonly IFusionCache _cache;

    public FusionCacheProvider(IFusionCache cache, TimeSpan? defaultTtl = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
        DefaultTtl = defaultTtl ?? LocalCache.DEFAULT_CACHE_TTL;
    }

    public TimeSpan DefaultTtl { get; }

    public async ValueTask<T?> Get<T>(string key, CancellationToken token = default)
        where T : notnull
    {
        var maybe = await _cache.TryGetAsync<T>(key, token: token);
        return maybe.HasValue ? maybe.Value : default;
    }

    public ValueTask Set<T>(string key, T val, TimeSpan? ttlOverride = null, CancellationToken token = default)
        where T : notnull
        => _cache.SetAsync(key, val, ToEntryOptions(ttlOverride), token);

    public ValueTask<T> GetOrSet<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttlOverride = null,
        CancellationToken token = default)
        where T : notnull
        => _cache.GetOrSetAsync<T>(key, (_, ct) => factory(ct), ToEntryOptions(ttlOverride), token);

    private FusionCacheEntryOptions ToEntryOptions(TimeSpan? ttlOverride)
        => new(ttlOverride ?? DefaultTtl);

    // FusionCache does not report whether the key existed; deletion itself always succeeds.
    public async ValueTask<bool> Delete(string key, CancellationToken token = default)
    {
        await _cache.RemoveAsync(key, token: token);
        return true;
    }

    // FusionCache deliberately does not support key enumeration, so unmatched keys cannot be swept.
    // This is only used by the Datastream client and thus will never be called.
    public ValueTask DeleteMissing(IEnumerable<string> keys, string? scanPattern = null)
        => ValueTask.CompletedTask;
}
