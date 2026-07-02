using Microsoft.Extensions.Logging;

namespace TechTest.Infrastructure.Resilience;

/// <summary>
/// Thread-safe state machine implementing the Closed → Open → Half-Open → Closed circuit breaker pattern.
/// </summary>
/// <remarks>
/// Registered as a singleton so the shared state is visible to all callers within the process.
/// All state transitions are logged. Log calls are made <em>after</em> releasing the lock to avoid
/// holding it during I/O.
/// <para>
/// Accepts an optional <see cref="TimeProvider"/> for deterministic testing; defaults to
/// <see cref="TimeProvider.System"/> in production.
/// </para>
/// </remarks>
internal sealed class CircuitBreaker
{
    private enum State { Closed, Open, HalfOpen }

    private readonly int _failureThreshold;
    private readonly TimeSpan _coolDown;
    private readonly ILogger<CircuitBreaker> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();

    private State _state = State.Closed;
    private int _consecutiveFailures;
    private DateTimeOffset _openedAt;

    public CircuitBreaker(
        int failureThreshold,
        TimeSpan coolDown,
        ILogger<CircuitBreaker> logger,
        TimeProvider? timeProvider = null)
    {
        _failureThreshold = failureThreshold;
        _coolDown = coolDown;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns <c>true</c> when the circuit allows a call to proceed.
    /// </summary>
    /// <remarks>
    /// Closed     → always allows calls.
    /// Open       → blocks calls until the cool-down elapses, then transitions to HalfOpen and allows one probe.
    /// HalfOpen   → a probe is already in flight; all other requests are blocked until it completes.
    /// </remarks>
    public bool AllowRequest()
    {
        bool allowed;
        bool transitionedToHalfOpen = false;

        lock (_lock)
        {
            switch (_state)
            {
                case State.Open when _timeProvider.GetUtcNow() - _openedAt >= _coolDown:
                    // Cool-down elapsed — allow exactly one probe (transition to HalfOpen).
                    _state = State.HalfOpen;
                    transitionedToHalfOpen = true;
                    allowed = true;
                    break;

                case State.Closed:
                    allowed = true;
                    break;

                default:
                    // Open (cooldown not elapsed) or HalfOpen (probe in flight).
                    allowed = false;
                    break;
            }
        }

        if (transitionedToHalfOpen)
            _logger.LogInformation("Circuit breaker cool-down elapsed; one probe allowed (Open → HalfOpen).");

        return allowed;
    }

    /// <summary>
    /// Records a successful call. Resets the failure count and closes the circuit.
    /// </summary>
    public void RecordSuccess()
    {
        bool closedFromHalfOpen;

        lock (_lock)
        {
            closedFromHalfOpen = _state == State.HalfOpen;
            _consecutiveFailures = 0;
            _state = State.Closed;
        }

        if (closedFromHalfOpen)
            _logger.LogInformation("Circuit breaker probe succeeded; circuit closed (HalfOpen → Closed).");
    }

    /// <summary>
    /// Records a failed call. Increments the consecutive failure count and opens the circuit
    /// when the threshold is reached, or immediately if the probe in HalfOpen state failed.
    /// </summary>
    public void RecordFailure()
    {
        bool tripFromHalfOpen;
        bool trippedFromClosed;
        int failureCount;

        lock (_lock)
        {
            tripFromHalfOpen = _state == State.HalfOpen;

            if (tripFromHalfOpen)
            {
                Trip();
                trippedFromClosed = false;
                failureCount = 0;
            }
            else
            {
                _consecutiveFailures++;
                failureCount = _consecutiveFailures;
                trippedFromClosed = _consecutiveFailures >= _failureThreshold;
                if (trippedFromClosed)
                    Trip();
            }
        }

        if (tripFromHalfOpen)
            _logger.LogWarning("Circuit breaker probe failed; circuit reopened (HalfOpen → Open).");
        else if (trippedFromClosed)
            _logger.LogWarning(
                "Circuit breaker opened after {ConsecutiveFailures} consecutive failures (Closed → Open).",
                failureCount);
    }

    private void Trip()
    {
        _state = State.Open;
        _openedAt = _timeProvider.GetUtcNow();
    }
}
