using Moq;
using TechTest.Api.Features.BestStories;
using TechTest.Infrastructure.Cache;
using TechTest.Infrastructure.HackerNews;
using TechTest.Shared.Configuration;

namespace TechTest.Tests.Unit.Features;

public sealed class BestStoriesHandlerTests
{
    private const int DefaultMaxConcurrentRequests = 10;

    private static BestStoriesHandler CreateHandler(IStoryCacheService? cache = null)
    {
        cache ??= new Mock<IStoryCacheService>().Object;
        var options = new HackerNewsOptions
        {
            MaxConcurrentRequests = DefaultMaxConcurrentRequests
        };
        return new BestStoriesHandler(cache, options);
    }

    // -------------------------------------------------------------------------
    // Sorting
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Returns_StoriesSortedByScoreDescending()
    {
        var stories = new[]
        {
            MakeStory(1, "Low",    score: 10),
            MakeStory(2, "High",   score: 300),
            MakeStory(3, "Middle", score: 150),
        };
        var cache = SetupCache(stories);
        var handler = CreateHandler(cache.Object);

        var result = await handler.HandleAsync(3, CancellationToken.None);

        Assert.Equal(["High", "Middle", "Low"], result.Select(r => r.Title).ToArray());
    }

    [Fact]
    public async Task HandleAsync_WhenMoreStoriesThanN_ReturnsTopN()
    {
        var stories = Enumerable.Range(1, 10)
            .Select(i => MakeStory(i, $"Story {i}", score: i * 10))
            .ToArray();
        var cache = SetupCache(stories);
        var handler = CreateHandler(cache.Object);

        var result = await handler.HandleAsync(3, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal(100, result[0].Score);
        Assert.Equal(90, result[1].Score);
        Assert.Equal(80, result[2].Score);
    }

    // -------------------------------------------------------------------------
    // Null filtering
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_NullStories_AreFilteredOut()
    {
        var cache = new Mock<IStoryCacheService>();
        cache.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new[] { 1, 2, 3 });
        cache.Setup(c => c.GetStoryAsync(1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(MakeStory(1, "Present", score: 50));
        cache.Setup(c => c.GetStoryAsync(2, It.IsAny<CancellationToken>()))
             .ReturnsAsync((HackerNewsStory?)null);
        cache.Setup(c => c.GetStoryAsync(3, It.IsAny<CancellationToken>()))
             .ReturnsAsync(MakeStory(3, "Also present", score: 30));

        var result = await CreateHandler(cache.Object).HandleAsync(3, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    // -------------------------------------------------------------------------
    // Field mapping
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_MapsAllFieldsCorrectly()
    {
        var unixTime = 1_700_000_000L;
        var story = new HackerNewsStory
        {
            Id = 42,
            Title = "Amazing Article",
            Url = "https://example.com/article",
            By = "jdoe",
            Time = unixTime,
            Score = 1234,
            Descendants = 99
        };
        var cache = new Mock<IStoryCacheService>();
        cache.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new[] { 42 });
        cache.Setup(c => c.GetStoryAsync(42, It.IsAny<CancellationToken>()))
             .ReturnsAsync(story);

        var result = await CreateHandler(cache.Object).HandleAsync(1, CancellationToken.None);

        var response = Assert.Single(result);
        Assert.Equal("Amazing Article", response.Title);
        Assert.Equal("https://example.com/article", response.Uri);
        Assert.Equal("jdoe", response.PostedBy);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(unixTime), response.Time);
        Assert.Equal(1234, response.Score);
        Assert.Equal(99, response.CommentCount);
    }

    [Fact]
    public async Task HandleAsync_WhenUrlIsNull_UriIsNull()
    {
        var story = new HackerNewsStory { Id = 1, Title = "Ask HN", Url = null, Score = 50 };
        var cache = new Mock<IStoryCacheService>();
        cache.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new[] { 1 });
        cache.Setup(c => c.GetStoryAsync(1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(story);

        var result = await CreateHandler(cache.Object).HandleAsync(1, CancellationToken.None);

        Assert.Null(Assert.Single(result).Uri);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static HackerNewsStory MakeStory(int id, string title, int score) =>
        new() { Id = id, Title = title, Score = score, By = "user", Time = 0 };

    private static Mock<IStoryCacheService> SetupCache(IReadOnlyCollection<HackerNewsStory> stories)
    {
        var cache = new Mock<IStoryCacheService>();
        cache.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(stories.Select(s => s.Id).ToArray());
        foreach (var story in stories)
        {
            var captured = story;
            cache.Setup(c => c.GetStoryAsync(captured.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(captured);
        }
        return cache;
    }
}
