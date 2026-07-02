using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using TechTest.Infrastructure.Cache;
using TechTest.Infrastructure.HackerNews;

namespace TechTest.Tests.Integration;

/// <summary>
/// Integration tests that spin up the full ASP.NET Core pipeline via
/// <see cref="WebApplicationFactory{TEntryPoint}"/> and replace only
/// <see cref="IStoryCacheService"/> with an in-memory fake so no Redis or
/// Hacker News connections are required.
/// </summary>
public sealed class BestStoriesEndpointTests : IClassFixture<BestStoriesEndpointTests.TestFactory>
{
    private readonly HttpClient _client;

    public BestStoriesEndpointTests(TestFactory factory)
    {
        _client = factory.CreateClient();
    }

    // -----------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("n=0")]
    [InlineData("n=-1")]
    [InlineData("n=-100")]
    [InlineData("n=501")]     // exceeds default MaxN of 500
    public async Task GetBestStories_WithInvalidN_Returns400(string queryString)
    {
        var response = await _client.GetAsync($"/api/v1/stories/best?{queryString}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBestStories_WithMissingN_Returns400()
    {
        var response = await _client.GetAsync("/api/v1/stories/best");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBestStories_WithNonNumericN_Returns400()
    {
        var response = await _client.GetAsync("/api/v1/stories/best?n=abc");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Happy path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetBestStories_WithValidN_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/stories/best?n=3");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetBestStories_ResponseMatchesContract()
    {
        var response = await _client.GetAsync("/api/v1/stories/best?n=1");
        response.EnsureSuccessStatusCode();

        var stories = await response.Content.ReadFromJsonAsync<List<StoryDto>>();

        Assert.NotNull(stories);
        var story = Assert.Single(stories);
        Assert.Equal("Top Story", story.Title);
        Assert.Equal("https://example.com/top", story.Uri);
        Assert.Equal("author", story.PostedBy);
        Assert.Equal(1000, story.Score);
        Assert.Equal(50, story.CommentCount);
    }

    [Fact]
    public async Task GetBestStories_ReturnsSortedByScoreDescending()
    {
        var response = await _client.GetAsync("/api/v1/stories/best?n=3");
        response.EnsureSuccessStatusCode();

        var stories = await response.Content.ReadFromJsonAsync<List<StoryDto>>();
        Assert.NotNull(stories);
        Assert.Equal(3, stories!.Count);

        for (int i = 0; i < stories.Count - 1; i++)
            Assert.True(stories[i].Score >= stories[i + 1].Score,
                $"Stories not sorted: [{i}].Score={stories[i].Score} < [{i + 1}].Score={stories[i + 1].Score}");
    }

    [Fact]
    public async Task GetBestStories_ReturnsAtMostNStories()
    {
        var response = await _client.GetAsync("/api/v1/stories/best?n=2");
        response.EnsureSuccessStatusCode();

        var stories = await response.Content.ReadFromJsonAsync<List<StoryDto>>();
        Assert.NotNull(stories);
        Assert.Equal(2, stories!.Count);
    }

    // -----------------------------------------------------------------------
    // Test infrastructure
    // -----------------------------------------------------------------------

    /// <summary>DTO that matches the camelCase JSON contract returned by the endpoint.</summary>
    private sealed record StoryDto(
        string Title,
        string? Uri,
        string PostedBy,
        DateTimeOffset Time,
        int Score,
        int CommentCount);

    public sealed class TestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Replace the real IStoryCacheService with a fake so no Redis or
                // Hacker News connections are attempted during tests.
                services.RemoveAll<IStoryCacheService>();
                services.AddSingleton<IStoryCacheService>(CreateFakeCache());
            });
        }

        private static IStoryCacheService CreateFakeCache()
        {
            var stories = new[]
            {
                new HackerNewsStory
                {
                    Id = 1, Title = "Top Story", Url = "https://example.com/top",
                    By = "author", Time = 1_700_000_000L, Score = 1000, Descendants = 50
                },
                new HackerNewsStory
                {
                    Id = 2, Title = "Second Story", Url = "https://example.com/second",
                    By = "writer", Time = 1_700_000_100L, Score = 750, Descendants = 30
                },
                new HackerNewsStory
                {
                    Id = 3, Title = "Third Story", Url = "https://example.com/third",
                    By = "poster", Time = 1_700_000_200L, Score = 500, Descendants = 20
                },
            };

            var mock = new Mock<IStoryCacheService>();
            mock.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(stories.Select(s => s.Id).ToArray());
            foreach (var story in stories)
            {
                var captured = story;
                mock.Setup(c => c.GetStoryAsync(captured.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(captured);
            }
            return mock.Object;
        }
    }
}
