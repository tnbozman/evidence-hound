using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Core;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>A clock that only moves when a test waits on it: every wait is recorded and moves the clock on, so nothing really sleeps.</summary>
internal sealed class TestClock(DateTimeOffset start) : IClock, IDelay
{
    private readonly List<TimeSpan> _waits = [];

    public TestClock()
        : this(new DateTimeOffset(2026, 11, 22, 2, 0, 0, TimeSpan.Zero))
    {
    }

    public DateTimeOffset UtcNow { get; private set; } = start;

    public IReadOnlyList<TimeSpan> Waits => _waits;

    public void Advance(TimeSpan by) => UtcNow += by;

    public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _waits.Add(duration);
        UtcNow += duration;
        return Task.CompletedTask;
    }
}
