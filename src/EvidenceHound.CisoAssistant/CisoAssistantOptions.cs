namespace EvidenceHound.CisoAssistant;

/// <summary>Where CISO Assistant is and how to authenticate. <paramref name="ApiUrl"/> ends in <c>/api/</c>.</summary>
public sealed record CisoAssistantOptions(Uri ApiUrl, string Token)
{
    /// <summary>The base delay between retries; each retry waits longer, with jitter.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);
}
