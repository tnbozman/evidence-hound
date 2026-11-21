namespace EvidenceHound.Collectors.M365;

/// <summary>Waits before retrying a throttled Graph request. Tests substitute one that records the delay instead of sleeping.</summary>
public interface IGraphWait
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>Waits for real.</summary>
public sealed class SystemGraphWait : IGraphWait
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
