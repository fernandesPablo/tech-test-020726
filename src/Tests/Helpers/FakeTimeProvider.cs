namespace TechTest.Tests.Helpers;

/// <summary>
/// A <see cref="TimeProvider"/> that exposes a manually-controllable clock.
/// Use <see cref="Advance"/> to move time forward without relying on wall-clock delays.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset? start = null)
    {
        _utcNow = start ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
