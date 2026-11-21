using System.Text;
using EvidenceHound.Core;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Collectors.Manual.Tests;

/// <summary>A temporary drop folder with fictional export files, and a context for a manual source that reads it.</summary>
internal sealed class DropFolder : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 11, 21, 2, 0, 0, TimeSpan.Zero);

    public static readonly string Reviewed = "network.firewall.rules_reviewed";

    public static readonly string Exported = "network.firewall.config_exported";

    public string Path { get; } = Directory.CreateTempSubdirectory("hound-manual-tests-").FullName;

    public static Dictionary<string, string> Settings(string folder, string patterns = "*.txt") => new(StringComparer.Ordinal)
    {
        [ManualSettings.DropFolder] = folder,
        [ManualSettings.Patterns] = patterns,
        [ManualSettings.Checks] = $"{Reviewed}, {Exported}",
        [ManualSettings.DueDays] = "30",
        [ManualSettings.Owner] = "Alex Example",
    };

    public void Add(string fileName, string content, DateTimeOffset modified)
    {
        var path = System.IO.Path.Combine(Path, fileName);
        File.WriteAllText(path, content, Encoding.UTF8);
        File.SetLastWriteTimeUtc(path, modified.UtcDateTime);
    }

    public void Attest(string fileName, string yaml) => File.WriteAllText(System.IO.Path.Combine(Path, Attestation.SidecarFor(fileName)), yaml, Encoding.UTF8);

    public Task<Result<CollectionResult>> CollectAsync(IReadOnlyDictionary<string, string>? settings = null) =>
        new ManualCollector().CollectAsync(Context(settings ?? Settings(Path)), TestContext.Current.CancellationToken);

    public static CollectorContext Context(IReadOnlyDictionary<string, string> settings) => new(
        "example-firewall-config",
        new CollectorSettings(settings),
        new Dictionary<string, Secret>(),
        new FixedClock(Now),
        NullLogger.Instance,
        new NoHttpClients());

    public void Dispose() => Directory.Delete(Path, recursive: true);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class NoHttpClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("The manual collector makes no HTTP requests.");
    }
}

internal static class Results
{
    public static T Value<T>(this Result<T> result)
        where T : notnull =>
        result.Match(value => value, problems => throw new InvalidOperationException(string.Join("; ", problems.Select(p => p.Message))));

    public static IReadOnlyList<Problem> Problems<T>(this Result<T> result)
        where T : notnull =>
        result.Match(_ => [], problems => problems);
}
