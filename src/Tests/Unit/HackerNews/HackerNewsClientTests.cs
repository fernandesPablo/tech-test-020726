using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TechTest.Infrastructure.HackerNews;
using TechTest.Tests.Helpers;

namespace TechTest.Tests.Unit.HackerNews;

public sealed class HackerNewsClientTests
{
    private static HackerNewsClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://hacker-news.firebaseio.com")
        };
        return new HackerNewsClient(httpClient, NullLogger<HackerNewsClient>.Instance);
    }

    // -------------------------------------------------------------------------
    // GetBestStoryIdsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBestStoryIdsAsync_ValidResponse_ReturnsDeserializedIds()
    {
        var ids = new[] { 100, 200, 300 };
        var json = JsonSerializer.Serialize(ids);
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler);
        var result = await client.GetBestStoryIdsAsync();

        Assert.Equal(ids, result);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_UsesCorrectEndpoint()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);
        await client.GetBestStoryIdsAsync();

        Assert.NotNull(captured);
        Assert.Equal("/v0/beststories.json", captured!.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new FakeHttpMessageHandler((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var client = CreateClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetBestStoryIdsAsync(cts.Token));
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenServerReturns500_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetBestStoryIdsAsync());
    }

    // -------------------------------------------------------------------------
    // GetStoryAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStoryAsync_ValidResponse_ReturnsDeserializedStory()
    {
        var story = new
        {
            id = 42,
            title = "Test Story",
            url = "https://example.com",
            by = "author",
            time = 1_700_000_000L,
            score = 500,
            descendants = 42
        };
        var json = JsonSerializer.Serialize(story);
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler);
        var result = await client.GetStoryAsync(42);

        Assert.NotNull(result);
        Assert.Equal(42, result!.Id);
        Assert.Equal("Test Story", result.Title);
        Assert.Equal("https://example.com", result.Url);
        Assert.Equal("author", result.By);
        Assert.Equal(1_700_000_000L, result.Time);
        Assert.Equal(500, result.Score);
        Assert.Equal(42, result.Descendants);
    }

    [Fact]
    public async Task GetStoryAsync_UsesCorrectEndpoint()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);
        await client.GetStoryAsync(99);

        Assert.NotNull(captured);
        Assert.Equal("/v0/item/99.json", captured!.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task GetStoryAsync_WhenServerReturns503_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStoryAsync(1));
    }
}
