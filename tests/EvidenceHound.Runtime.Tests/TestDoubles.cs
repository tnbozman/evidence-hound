using EvidenceHound.Collectors;
using EvidenceHound.Core;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Runtime.Tests;

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>A collector that returns a fixed result and records what it was given.</summary>
internal sealed class StubCollector(Func<CollectorContext, Result<CollectionResult>> collect) : ICollector
{
    public List<CollectorContext> Calls { get; } = [];

    public string Type => "stub";

    public string Version => "9.9.9";

    public CollectorDescription Describe() => new([], [], []);

    public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken)
    {
        Calls.Add(context);
        return Task.FromResult(collect(context));
    }
}

/// <summary>A collector that throws, like a bug in a vendor adapter.</summary>
internal sealed class ThrowingCollector : ICollector
{
    public string Type => "throwing";

    public string Version => "0.0.1";

    public CollectorDescription Describe() => new([], [], []);

    public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("collector bug");
}

/// <summary>HTTP clients for collectors under test; nothing here makes a request.</summary>
internal sealed class TestHttpClients : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}

internal static class TestCollectorServices
{
    public static CollectorServices None { get; } = new(NullLoggerFactory.Instance, new TestHttpClients());
}

internal static class Secrets
{
    public static ISecretProvider From(IReadOnlyDictionary<string, string> environment) =>
        Runtime.Secrets.CompositeSecretProvider.Standard(name => environment.GetValueOrDefault(name));
}
