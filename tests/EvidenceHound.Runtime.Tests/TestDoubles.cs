using EvidenceHound.Core;
using EvidenceHound.Core.Collecting;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Runtime.Tests;

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>A collector that returns a fixed result and records what it was given.</summary>
internal sealed class StubCollector(Func<CollectorContext, Result<CollectorOutput>> collect) : ICollector
{
    public List<CollectorContext> Calls { get; } = [];

    public string Version => "9.9.9";

    public Task<Result<CollectorOutput>> CollectAsync(CollectorContext context, CancellationToken cancellationToken)
    {
        Calls.Add(context);
        return Task.FromResult(collect(context));
    }
}

/// <summary>A collector that throws, like a bug in a vendor adapter.</summary>
internal sealed class ThrowingCollector : ICollector
{
    public string Version => "0.0.1";

    public Task<Result<CollectorOutput>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("collector bug");
}

internal static class Secrets
{
    public static ISecretProvider From(IReadOnlyDictionary<string, string> environment) =>
        Runtime.Secrets.CompositeSecretProvider.Standard(name => environment.GetValueOrDefault(name));
}
