namespace TechTest.Shared.Configuration;

public sealed class CircuitBreakerOptions
{
    public const string SectionName = "CircuitBreaker";

    /// <summary>Number of consecutive failures required to open the circuit.</summary>
    public int FailureThreshold { get; init; } = 5;

    /// <summary>Duration in seconds the circuit stays open before transitioning to half-open.</summary>
    public int CoolDownSeconds { get; init; } = 60;
}
