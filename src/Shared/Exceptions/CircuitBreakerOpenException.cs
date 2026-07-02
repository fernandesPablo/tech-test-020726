namespace TechTest.Shared.Exceptions;

/// <summary>
/// Thrown by <c>CircuitBreakerHackerNewsClient</c> when the circuit is open and a call
/// to the Hacker News API is suppressed to protect the downstream service.
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException()
        : base("The circuit breaker is open. Calls to Hacker News are suspended until the cool-down elapses.") { }
}
