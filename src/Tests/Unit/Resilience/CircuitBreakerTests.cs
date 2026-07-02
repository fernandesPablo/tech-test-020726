using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechTest.Infrastructure.HackerNews;
using TechTest.Infrastructure.Resilience;
using TechTest.Shared.Exceptions;
using TechTest.Tests.Helpers;

namespace TechTest.Tests.Unit.Resilience;

public sealed class CircuitBreakerTests
{
    private static CircuitBreaker CreateBreaker(
        int threshold = 3,
        TimeSpan? coolDown = null,
        FakeTimeProvider? clock = null)
        => new(threshold, coolDown ?? TimeSpan.FromMinutes(1),
               NullLogger<CircuitBreaker>.Instance, clock);

    // -------------------------------------------------------------------------
    // State machine — Closed state
    // -------------------------------------------------------------------------

    [Fact]
    public void AllowRequest_WhenClosed_ReturnsTrue()
    {
        var cb = CreateBreaker();
        Assert.True(cb.AllowRequest());
    }

    [Fact]
    public void RecordFailure_BelowThreshold_CircuitStaysClosed()
    {
        var cb = CreateBreaker(threshold: 3);

        cb.RecordFailure();
        cb.RecordFailure();

        Assert.True(cb.AllowRequest());
    }

    [Fact]
    public void RecordSuccess_WhenClosed_ResetsFailureCount()
    {
        var cb = CreateBreaker(threshold: 3);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordSuccess();  // resets count
        cb.RecordFailure();
        cb.RecordFailure();  // only 2 failures — should still be closed

        Assert.True(cb.AllowRequest());
    }

    // -------------------------------------------------------------------------
    // State machine — Closed → Open transition
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordFailure_WhenThresholdReached_OpensCircuit()
    {
        var cb = CreateBreaker(threshold: 3);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordFailure();

        Assert.False(cb.AllowRequest());
    }

    [Fact]
    public void AllowRequest_WhenOpenAndCoolDownNotElapsed_ReturnsFalse()
    {
        var clock = new FakeTimeProvider();
        var cb = CreateBreaker(threshold: 1, coolDown: TimeSpan.FromSeconds(30), clock: clock);

        cb.RecordFailure();  // opens circuit

        clock.Advance(TimeSpan.FromSeconds(10));  // well before cool-down
        Assert.False(cb.AllowRequest());
    }

    // -------------------------------------------------------------------------
    // State machine — Open → HalfOpen transition
    // -------------------------------------------------------------------------

    [Fact]
    public void AllowRequest_WhenOpenAndCoolDownElapsed_TransitionsToHalfOpen()
    {
        var clock = new FakeTimeProvider();
        var cb = CreateBreaker(threshold: 1, coolDown: TimeSpan.FromSeconds(30), clock: clock);

        cb.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(31));

        Assert.True(cb.AllowRequest());  // probe allowed
    }

    [Fact]
    public void AllowRequest_WhenHalfOpen_BlocksSubsequentRequests()
    {
        var clock = new FakeTimeProvider();
        var cb = CreateBreaker(threshold: 1, coolDown: TimeSpan.FromSeconds(30), clock: clock);

        cb.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(31));

        cb.AllowRequest();           // first call transitions to HalfOpen and allows probe
        Assert.False(cb.AllowRequest()); // subsequent calls blocked while probe is in flight
    }

    // -------------------------------------------------------------------------
    // State machine — HalfOpen → Closed (probe success)
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordSuccess_WhenHalfOpen_ClosesCircuit()
    {
        var clock = new FakeTimeProvider();
        var cb = CreateBreaker(threshold: 1, coolDown: TimeSpan.FromSeconds(30), clock: clock);

        cb.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(31));
        cb.AllowRequest();   // probe slot taken (HalfOpen)

        cb.RecordSuccess();  // probe succeeded

        Assert.True(cb.AllowRequest());  // circuit is Closed again
    }

    // -------------------------------------------------------------------------
    // State machine — HalfOpen → Open (probe failure)
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordFailure_WhenHalfOpen_ReturnsToOpen()
    {
        var clock = new FakeTimeProvider();
        var cb = CreateBreaker(threshold: 1, coolDown: TimeSpan.FromSeconds(30), clock: clock);

        cb.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(31));
        cb.AllowRequest();   // probe slot taken (HalfOpen)

        cb.RecordFailure();  // probe failed → reopen

        Assert.False(cb.AllowRequest());  // still open
    }

    [Fact]
    public void RecordFailure_WhenHalfOpen_ResetsTheCoolDownTimer()
    {
        var clock = new FakeTimeProvider();
        var cb = CreateBreaker(threshold: 1, coolDown: TimeSpan.FromSeconds(30), clock: clock);

        cb.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(31));
        cb.AllowRequest();   // probe
        cb.RecordFailure();  // probe failed — reopened with fresh timer

        // Advance only 20 s; original cool-down would have expired 51 s in, but it reset.
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.False(cb.AllowRequest());  // new cool-down not yet elapsed
    }
}

// -------------------------------------------------------------------------
// Decorator — CircuitBreakerHackerNewsClient
// -------------------------------------------------------------------------

public sealed class CircuitBreakerHackerNewsClientTests
{
    private static (CircuitBreakerHackerNewsClient client, Mock<IHackerNewsClient> inner)
        CreateSut(int threshold = 3, FakeTimeProvider? clock = null)
    {
        var inner = new Mock<IHackerNewsClient>();
        var cb = new CircuitBreaker(threshold, TimeSpan.FromMinutes(1),
            NullLogger<CircuitBreaker>.Instance, clock);
        var client = new CircuitBreakerHackerNewsClient(inner.Object, cb,
            NullLogger<CircuitBreakerHackerNewsClient>.Instance);
        return (client, inner);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_WhenClosed_DelegatesToInner()
    {
        var (client, inner) = CreateSut();
        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new[] { 1, 2, 3 });

        var result = await client.GetBestStoryIdsAsync();

        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_AfterThresholdFailures_ThrowsCircuitBreakerOpenException()
    {
        var (client, inner) = CreateSut(threshold: 3);
        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new HttpRequestException("server error"));

        for (int i = 0; i < 3; i++)
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetBestStoryIdsAsync());

        await Assert.ThrowsAsync<CircuitBreakerOpenException>(() => client.GetBestStoryIdsAsync());
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_CallerCancellation_DoesNotCountAsFailure()
    {
        var (client, inner) = CreateSut(threshold: 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new OperationCanceledException(cts.Token));

        // Cancellation should propagate but not count as a circuit failure.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.GetBestStoryIdsAsync(cts.Token));

        // Circuit should still be closed — another (uncancelled) call should succeed.
        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(Array.Empty<int>());
        var result = await client.GetBestStoryIdsAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_ProbeSuccess_ClosesCircuit()
    {
        var clock = new FakeTimeProvider();
        var (client, inner) = CreateSut(threshold: 1, clock: clock);
        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new HttpRequestException());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetBestStoryIdsAsync());

        clock.Advance(TimeSpan.FromMinutes(2));

        inner.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new[] { 42 });

        var result = await client.GetBestStoryIdsAsync();
        Assert.Equal([42], result);

        // Circuit should be closed — second call also succeeds.
        var result2 = await client.GetBestStoryIdsAsync();
        Assert.Equal([42], result2);
    }
}
