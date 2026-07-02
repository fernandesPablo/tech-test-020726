using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TechTest.Infrastructure.Cache;
using TechTest.Infrastructure.HackerNews;
using TechTest.Shared.Abstractions;
using TechTest.Shared.Configuration;

namespace TechTest.Tests.Unit.Cache;

public sealed class StoryCacheServiceTests
{
    // -----------------------------------------------------------------------
    // Fixture helpers
    // -----------------------------------------------------------------------

    private sealed class Fixture
    {
        public MemoryCache L1 { get; } = new(Options.Create(new MemoryCacheOptions()));
        public Mock<IDistributedCache> L2 { get; } = new();
        public Mock<IHackerNewsClient> HackerNews { get; } = new();
        public Mock<IDistributedLock> Lock { get; } = new();

        public StoryCacheService Build()
        {
            var cacheOpts = new CacheOptions
            {
                StoryIdsTtl = TimeSpan.FromMinutes(5),
                StoryDetailsTtl = TimeSpan.FromMinutes(5)
            };
            var redisOpts = new RedisOptions
            {
                LockTtl = TimeSpan.FromSeconds(30),
                LockRetryCount = 2,
                LockRetryDelay = TimeSpan.FromMilliseconds(1)
            };
            return new StoryCacheService(
                L1, L2.Object, HackerNews.Object, Lock.Object,
                cacheOpts, redisOpts,
                NullLogger<StoryCacheService>.Instance);
        }
    }

    private static Mock<IAsyncDisposable> AcquiredLockHandle()
    {
        var handle = new Mock<IAsyncDisposable>();
        handle.Setup(h => h.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return handle;
    }

    private static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);

    // -----------------------------------------------------------------------
    // GetBestStoryIdsAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenL1Hit_DoesNotTouchL2OrHackerNews()
    {
        var fixture = new Fixture();
        var ids = new[] { 1, 2, 3 };

        // Pre-populate L1
        fixture.L1.Set(CacheKeys.BestStoryIds, (IReadOnlyList<int>)ids);

        var sut = fixture.Build();
        var result = await sut.GetBestStoryIdsAsync();

        Assert.Equal(ids, result);
        fixture.L2.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.HackerNews.Verify(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenL2Hit_PopulatesL1AndReturns()
    {
        var fixture = new Fixture();
        var ids = new[] { 10, 20 };
        fixture.L2.Setup(d => d.GetAsync(CacheKeys.BestStoryIds, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Serialize(ids));

        var sut = fixture.Build();
        var result = await sut.GetBestStoryIdsAsync();

        Assert.Equal(ids, result);

        // Subsequent call should hit L1 (which was just populated)
        fixture.L2.Invocations.Clear();
        var result2 = await sut.GetBestStoryIdsAsync();
        Assert.Equal(ids, result2);
        fixture.L2.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenFullCacheMiss_FetchesFromHackerNewsAndPopulatesBothCaches()
    {
        var fixture = new Fixture();
        var freshIds = new int[] { 5, 6, 7 };

        fixture.L2.Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((byte[]?)null);
        fixture.HackerNews.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(freshIds);
        fixture.Lock.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(AcquiredLockHandle().Object);

        var sut = fixture.Build();
        var result = await sut.GetBestStoryIdsAsync();

        Assert.Equal(freshIds, result);
        fixture.HackerNews.Verify(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.L2.Verify(d => d.SetAsync(CacheKeys.BestStoryIds, It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenFullCacheMiss_PopulatesL1()
    {
        var fixture = new Fixture();
        var freshIds = new int[] { 11, 22 };

        fixture.L2.Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((byte[]?)null);
        fixture.HackerNews.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(freshIds);
        fixture.Lock.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(AcquiredLockHandle().Object);

        var sut = fixture.Build();
        await sut.GetBestStoryIdsAsync();

        // L1 should now contain the data
        Assert.True(fixture.L1.TryGetValue(CacheKeys.BestStoryIds, out IReadOnlyList<int>? cached));
        Assert.Equal(freshIds, cached);
    }

    // -----------------------------------------------------------------------
    // GetStoryAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStoryAsync_WhenL1Hit_DoesNotTouchL2OrHackerNews()
    {
        var fixture = new Fixture();
        var story = new HackerNewsStory { Id = 42, Title = "Cached" };
        fixture.L1.Set(CacheKeys.Story(42), story);

        var sut = fixture.Build();
        var result = await sut.GetStoryAsync(42);

        Assert.Equal(story, result);
        fixture.L2.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.HackerNews.Verify(c => c.GetStoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStoryAsync_WhenL2Hit_PopulatesL1AndReturns()
    {
        var fixture = new Fixture();
        var story = new HackerNewsStory { Id = 7, Title = "Redis Hit", Score = 200 };
        fixture.L2.Setup(d => d.GetAsync(CacheKeys.Story(7), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Serialize(story));

        var sut = fixture.Build();
        var result = await sut.GetStoryAsync(7);

        Assert.NotNull(result);
        Assert.Equal("Redis Hit", result!.Title);

        // L1 should now be populated
        Assert.True(fixture.L1.TryGetValue(CacheKeys.Story(7), out HackerNewsStory? cached));
        Assert.Equal("Redis Hit", cached!.Title);
    }

    [Fact]
    public async Task GetStoryAsync_WhenFullCacheMiss_FetchesFromHackerNewsAndCaches()
    {
        var fixture = new Fixture();
        var story = new HackerNewsStory { Id = 3, Title = "Fresh", Score = 50 };

        fixture.L2.Setup(d => d.GetAsync(CacheKeys.Story(3), It.IsAny<CancellationToken>()))
               .ReturnsAsync((byte[]?)null);
        fixture.HackerNews.Setup(c => c.GetStoryAsync(3, It.IsAny<CancellationToken>()))
               .ReturnsAsync(story);

        var sut = fixture.Build();
        var result = await sut.GetStoryAsync(3);

        Assert.NotNull(result);
        Assert.Equal("Fresh", result!.Title);
        fixture.L2.Verify(d => d.SetAsync(CacheKeys.Story(3), It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStoryAsync_WhenHackerNewsReturnsNull_DoesNotCacheNullResult()
    {
        var fixture = new Fixture();
        fixture.L2.Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((byte[]?)null);
        fixture.HackerNews.Setup(c => c.GetStoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((HackerNewsStory?)null);

        var sut = fixture.Build();
        var result = await sut.GetStoryAsync(999);

        Assert.Null(result);
        fixture.L2.Verify(d => d.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
