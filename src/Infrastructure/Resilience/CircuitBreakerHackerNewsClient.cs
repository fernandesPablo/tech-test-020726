using Microsoft.Extensions.Logging;
using TechTest.Infrastructure.HackerNews;
using TechTest.Shared.Exceptions;

namespace TechTest.Infrastructure.Resilience;

/// <summary>
/// Decorator that wraps <see cref="IHackerNewsClient"/> with circuit breaker protection.
/// When the circuit is open, calls are rejected immediately without reaching the inner client.
/// </summary>
/// <remarks>
/// Decorator chain: RetryingHackerNewsClient → CircuitBreakerHackerNewsClient → HackerNewsClient.
/// The circuit breaker sits between the retry decorator and the raw HTTP client so that every
/// individual attempt (including retries) is counted. This causes the circuit to open after
/// <c>FailureThreshold</c> consecutive raw failures, rather than after that many exhausted
/// retry sequences, which would delay detection and amplify traffic to a failing downstream.
/// </remarks>
internal sealed class CircuitBreakerHackerNewsClient : IHackerNewsClient
{
    private readonly IHackerNewsClient _inner;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly ILogger<CircuitBreakerHackerNewsClient> _logger;

    public CircuitBreakerHackerNewsClient(
        IHackerNewsClient inner,
        CircuitBreaker circuitBreaker,
        ILogger<CircuitBreakerHackerNewsClient> logger)
    {
        _inner = inner;
        _circuitBreaker = circuitBreaker;
        _logger = logger;
    }

    public async Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken = default)
        => await ExecuteAsync(() => _inner.GetBestStoryIdsAsync(cancellationToken), cancellationToken);

    public async Task<HackerNewsStory?> GetStoryAsync(int id, CancellationToken cancellationToken = default)
        => await ExecuteAsync(() => _inner.GetStoryAsync(id, cancellationToken), cancellationToken);

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        if (!_circuitBreaker.AllowRequest())
        {
            _logger.LogDebug("Hacker News call suppressed; circuit breaker is open.");
            throw new CircuitBreakerOpenException();
        }

        try
        {
            var result = await operation();
            _circuitBreaker.RecordSuccess();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Hacker News call cancelled by caller; not recording as circuit breaker failure.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Hacker News call failed; recording circuit breaker failure.");
            _circuitBreaker.RecordFailure();
            throw;
        }
    }
}
