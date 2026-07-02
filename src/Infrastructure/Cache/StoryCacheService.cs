using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TechTest.Infrastructure.HackerNews;
using TechTest.Shared.Abstractions;
using TechTest.Shared.Configuration;

namespace TechTest.Infrastructure.Cache;

internal sealed class StoryCacheService : IStoryCacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly IDistributedCache _distributedCache;
    private readonly IHackerNewsClient _hackerNewsClient;
    private readonly IDistributedLock _distributedLock;
    private readonly CacheOptions _cacheOptions;
    private readonly RedisOptions _redisOptions;
    private readonly ILogger<StoryCacheService> _logger;

    public StoryCacheService(
        IMemoryCache memoryCache,
        IDistributedCache distributedCache,
        IHackerNewsClient hackerNewsClient,
        IDistributedLock distributedLock,
        CacheOptions cacheOptions,
        RedisOptions redisOptions,
        ILogger<StoryCacheService> logger)
    {
        _memoryCache = memoryCache;
        _distributedCache = distributedCache;
        _hackerNewsClient = hackerNewsClient;
        _distributedLock = distributedLock;
        _cacheOptions = cacheOptions;
        _redisOptions = redisOptions;
        _logger = logger;
    }

    public async Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken = default)
    {
        // L1 — in-process memory cache
        if (_memoryCache.TryGetValue(CacheKeys.BestStoryIds, out IReadOnlyList<int>? ids) && ids is not null)
        {
            _logger.LogDebug("Cache L1 hit for {CacheKey}.", CacheKeys.BestStoryIds);
            return ids;
        }

        // L2 — shared Redis cache
        var cachedBytes = await _distributedCache.GetAsync(CacheKeys.BestStoryIds, cancellationToken);
        if (cachedBytes is not null)
        {
            _logger.LogDebug("Cache L2 hit for {CacheKey}.", CacheKeys.BestStoryIds);
            ids = JsonSerializer.Deserialize<int[]>(cachedBytes) ?? [];
            SetMemoryCache(CacheKeys.BestStoryIds, ids, _cacheOptions.StoryIdsTtl);
            return ids;
        }

        _logger.LogDebug("Cache miss for {CacheKey}; acquiring lock to refresh.", CacheKeys.BestStoryIds);

        // Full cache miss — try to acquire the distributed lock so only one instance refreshes.
        var lockHandle = await _distributedLock.TryAcquireAsync(
            CacheKeys.BestStoryIdsLock, _redisOptions.LockTtl, cancellationToken);

        if (lockHandle is not null)
        {
            await using (lockHandle)
            {
                // Double-check: another instance may have populated the cache
                // between our L2 miss and acquiring the lock.
                var doubleCheckBytes = await _distributedCache.GetAsync(CacheKeys.BestStoryIds, cancellationToken);
                if (doubleCheckBytes is not null)
                {
                    _logger.LogDebug("Cache L2 hit for {CacheKey} after double-check inside lock.", CacheKeys.BestStoryIds);
                    ids = JsonSerializer.Deserialize<int[]>(doubleCheckBytes) ?? [];
                    SetMemoryCache(CacheKeys.BestStoryIds, ids, _cacheOptions.StoryIdsTtl);
                    return ids;
                }

                // We hold the lock and the cache is confirmed empty — fetch from Hacker News.
                return await FetchAndCacheStoryIdsAsync(cancellationToken);
            }
        }

        // Lock is held by another instance — wait and re-check Redis until it is populated.
        for (var attempt = 0; attempt < _redisOptions.LockRetryCount; attempt++)
        {
            await Task.Delay(_redisOptions.LockRetryDelay, cancellationToken);

            var retryBytes = await _distributedCache.GetAsync(CacheKeys.BestStoryIds, cancellationToken);
            if (retryBytes is not null)
            {
                _logger.LogDebug("Cache L2 hit for {CacheKey} while waiting for lock (attempt {Attempt}).",
                    CacheKeys.BestStoryIds, attempt + 1);
                ids = JsonSerializer.Deserialize<int[]>(retryBytes) ?? [];
                SetMemoryCache(CacheKeys.BestStoryIds, ids, _cacheOptions.StoryIdsTtl);
                return ids;
            }
        }

        // Retries exhausted and cache is still empty.
        // The lock holder may have crashed before writing. Fetch directly as a last resort
        // rather than failing the request.
        return await FetchAndCacheStoryIdsAsync(cancellationToken);
    }

    public async Task<HackerNewsStory?> GetStoryAsync(int id, CancellationToken cancellationToken = default)
    {
        var key = CacheKeys.Story(id);

        // L1 — in-process memory cache
        if (_memoryCache.TryGetValue(key, out HackerNewsStory? story) && story is not null)
        {
            _logger.LogDebug("Cache L1 hit for {CacheKey}.", key);
            return story;
        }

        // L2 — shared Redis cache
        var cachedBytes = await _distributedCache.GetAsync(key, cancellationToken);
        if (cachedBytes is not null)
        {
            story = JsonSerializer.Deserialize<HackerNewsStory>(cachedBytes);
            if (story is not null)
            {
                _logger.LogDebug("Cache L2 hit for {CacheKey}.", key);
                SetMemoryCache(key, story, _cacheOptions.StoryDetailsTtl);
                return story;
            }
        }

        _logger.LogDebug("Cache miss for {CacheKey}; fetching from Hacker News.", key);

        // Full cache miss — fetch from Hacker News.
        // Individual stories are not locked: concurrent fetches of the same story are
        // bounded by the handler's concurrency limit and self-resolve once cached.
        story = await _hackerNewsClient.GetStoryAsync(id, cancellationToken);

        if (story is not null)
        {
            await SetDistributedCacheAsync(key, story, _cacheOptions.StoryDetailsTtl, cancellationToken);
            SetMemoryCache(key, story, _cacheOptions.StoryDetailsTtl);
        }

        return story;
    }

    private async Task<IReadOnlyList<int>> FetchAndCacheStoryIdsAsync(CancellationToken cancellationToken)
    {
        // Stale cache strategy — availability over freshness:
        // If L1 or L2 contain valid data, they are served above before this method is ever reached.
        // If both caches are empty and this fetch fails (Hacker News unreachable, retries exhausted,
        // or circuit breaker open), there is no stale data to fall back to and the exception propagates.
        // A background refresh (out of scope) or an extended shadow TTL would be needed to serve stale
        // data beyond the point where both cache levels have expired simultaneously.
        var freshIds = await _hackerNewsClient.GetBestStoryIdsAsync(cancellationToken);
        await SetDistributedCacheAsync(CacheKeys.BestStoryIds, freshIds, _cacheOptions.StoryIdsTtl, cancellationToken);
        SetMemoryCache(CacheKeys.BestStoryIds, freshIds, _cacheOptions.StoryIdsTtl);
        _logger.LogDebug("Cache refreshed for {CacheKey}: {Count} story IDs stored.", CacheKeys.BestStoryIds, freshIds.Count);
        return freshIds;
    }

    private void SetMemoryCache<T>(string key, T value, TimeSpan ttl)
    {
        _memoryCache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        });
    }

    private async Task SetDistributedCacheAsync<T>(
        string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        await _distributedCache.SetAsync(key, bytes, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        }, cancellationToken);
    }
}
