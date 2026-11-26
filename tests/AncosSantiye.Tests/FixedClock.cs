namespace AncosSantiye.Tests;

/// <summary>A clock that says what the test says. Tests with fixed event times must never read the real clock.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
