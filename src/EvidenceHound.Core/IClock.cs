namespace EvidenceHound.Core;

/// <summary>The current time, passed in so logic never reads a hidden clock.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
