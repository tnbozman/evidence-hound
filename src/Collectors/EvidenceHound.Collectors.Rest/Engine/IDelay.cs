namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>Waiting, passed in so tests never sleep: between requests for the rate limit, before a retry, and between polls.</summary>
public interface IDelay
{
    Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken);
}

/// <summary>Really waits.</summary>
public sealed class SystemDelay : IDelay
{
    public static SystemDelay Instance { get; } = new();

    public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.Delay(duration, cancellationToken);
}
