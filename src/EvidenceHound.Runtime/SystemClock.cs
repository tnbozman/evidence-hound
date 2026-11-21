using EvidenceHound.Core;

namespace EvidenceHound.Runtime;

/// <summary>The real clock, for the composition root.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
