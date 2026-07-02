using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechTest.Infrastructure.HackerNews;
using TechTest.Infrastructure.Resilience;
using TechTest.Shared.Configuration;

namespace TechTest.Tests.Unit.Resilience;

public sealed class RetryingHackerNewsClientTests
{
    private static RetryingHackerNewsClient CreateSut(
        IHackerNewsClient inner,
        int maxRetryCount = 3,
        int baseDelayMs = 0)
    {
        var options = new RetryOptions
        {
            MaxRetryCount = maxRetryCount,
            BaseDelayMs = baseDelayMs
        };
        return new RetryingHackerNewsClient(inner, options, NullLogger<RetryingHackerNewsClient>.Instance);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenSucceedsOnFirstAttempt_ReturnsResultWithoutRetry()
    {
        var inner = new Mock<IHackerNewsClient>();
        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new[] { 1, 2, 3 });

        var sut = CreateSut(inner.Object);

        var result = await sut.GetBestStoryIdsAsync();

        Assert.Equal([1, 2, 3], result);
        inner.Verify(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenTransientNetworkFailure_RetriesAndReturnsResult()
    {
        var callCount = 0;
        var inner = new Mock<IHackerNewsClient>();
        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .Returns(() =>
             {
                 callCount++;
                 if (callCount < 3)
                     throw new HttpRequestException("network error");  // no status code — transient
                 return Task.FromResult<IReadOnlyList<int>>(new[] { 99 });
             });

        var sut = CreateSut(inner.Object, maxRetryCount: 3);

        var result = await sut.GetBestStoryIdsAsync();

        Assert.Equal([99], result);
        Assert.Equal(3, callCount);  // 2 failures + 1 success
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenTransientServerError_RetriesAndReturnsResult()
    {
        var callCount = 0;
        var inner = new Mock<IHackerNewsClient>();
        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .Returns(() =>
             {
                 callCount++;
                 if (callCount == 1)
                     throw new HttpRequestException("server error", null,
                         System.Net.HttpStatusCode.ServiceUnavailable);
                 return Task.FromResult<IReadOnlyList<int>>(new[] { 7 });
             });

        var sut = CreateSut(inner.Object, maxRetryCount: 2);

        var result = await sut.GetBestStoryIdsAsync();

        Assert.Equal([7], result);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenPermanentClientError_DoesNotRetry()
    {
        var inner = new Mock<IHackerNewsClient>();
        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new HttpRequestException("not found", null,
                 System.Net.HttpStatusCode.NotFound));

        var sut = CreateSut(inner.Object, maxRetryCount: 3);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetBestStoryIdsAsync());

        inner.Verify(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenCallerCancels_DoesNotRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var inner = new Mock<IHackerNewsClient>();
        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new OperationCanceledException(cts.Token));

        var sut = CreateSut(inner.Object, maxRetryCount: 3);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.GetBestStoryIdsAsync(cts.Token));

        inner.Verify(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenClientTimeout_IsRetriedAsTransient()
    {
        // HttpClient timeout fires as TaskCanceledException with a different token than the caller's.
        var callCount = 0;
        var inner = new Mock<IHackerNewsClient>();
        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .Returns(() =>
             {
                 callCount++;
                 if (callCount == 1)
                 {
                     // Simulate HttpClient.Timeout — uses a SEPARATE internal CTS, not the caller's.
                     using var internalCts = new CancellationTokenSource();
                     internalCts.Cancel();
                     throw new TaskCanceledException("timeout", null, internalCts.Token);
                 }
                 return Task.FromResult<IReadOnlyList<int>>(new[] { 5 });
             });

        var sut = CreateSut(inner.Object, maxRetryCount: 3);

        var result = await sut.GetBestStoryIdsAsync(CancellationToken.None);

        Assert.Equal([5], result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenAllRetriesExhausted_PropagatesLastException()
    {
        var inner = new Mock<IHackerNewsClient>();
        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new HttpRequestException("transient"));

        var sut = CreateSut(inner.Object, maxRetryCount: 2);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetBestStoryIdsAsync());

        // 1 original attempt + 2 retries = 3 total calls
        inner.Verify(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task GetStoryAsync_WhenTransientFailure_RetriesAndReturnsStory()
    {
        var story = new HackerNewsStory { Id = 42, Title = "Test Story", Score = 100 };
        var callCount = 0;
        var inner = new Mock<IHackerNewsClient>();
        inner.Setup(c => c.GetStoryAsync(42, It.IsAny<CancellationToken>()))
             .Returns(() =>
             {
                 callCount++;
                 if (callCount == 1)
                     throw new HttpRequestException("transient");
                 return Task.FromResult<HackerNewsStory?>(story);
             });

        var sut = CreateSut(inner.Object, maxRetryCount: 2);

        var result = await sut.GetStoryAsync(42);

        Assert.Equal(story, result);
    }
}
