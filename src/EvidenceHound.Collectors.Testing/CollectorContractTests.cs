using EvidenceHound.Core;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Sdk;

namespace EvidenceHound.Collectors.Testing;

/// <summary>
/// The tests every collector must pass. Derive a public test class, return the collector from <see cref="CreateCollector"/> and,
/// if it talks to a system, describe a working run in <see cref="Working"/> and a refused login in <see cref="RefusedCredentials"/>.
/// </summary>
public abstract class CollectorContractTests<TCollector>
    where TCollector : ICollector
{
    private static readonly string InstanceName = "contract";

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The collector under test.</summary>
    protected abstract TCollector CreateCollector();

    /// <summary>A run in which the target system answers normally. Default: no settings, no secrets, no network.</summary>
    protected virtual CollectorScenario Working { get; } = new();

    /// <summary>A run in which the target system refuses the credentials. Required when the collector declares secrets.</summary>
    protected virtual CollectorScenario? RefusedCredentials => null;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void DescribesAtLeastOneCheck()
    {
        var collector = CreateCollector();
        Assert.True(collector.Describe().Checks.Count > 0, $"Collector '{collector.Type}' describes no checks. Describe() must list every check ID it can emit.");
    }

    [Fact]
    public async Task EmitsOnlyDeclaredCheckIds()
    {
        var collector = CreateCollector();
        var declared = collector.Describe().Checks.ToHashSet(StringComparer.Ordinal);

        var result = await CollectWorkingAsync(collector);

        var undeclared = result.Checks.Select(c => c.CheckId.Value).Where(id => !declared.Contains(id)).Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(undeclared.Length == 0, $"Collector '{collector.Type}' emitted check IDs that Describe() does not declare: {string.Join(", ", undeclared)}.");
    }

    [Fact]
    public async Task EvidenceFilesCanBeHashed()
    {
        var collector = CreateCollector();
        var result = await CollectWorkingAsync(collector);

        var first = await ReadAllAsync(result.Files);
        var second = await ReadAllAsync(result.Files);
        var manifest = Manifests.Build(InstanceName, collector.Version, Now, first)
            .Match(m => m, problems => throw FailException.ForFailure($"Collector '{collector.Type}' emitted evidence the runner cannot hash: {Describe(problems)}"));

        var unstable = first.Zip(second).Where(pair => !pair.First.Content.Span.SequenceEqual(pair.Second.Content.Span)).Select(pair => pair.First.FileName).ToArray();
        Assert.True(unstable.Length == 0, $"Collector '{collector.Type}' gave different content each time these evidence files were read, so their hash would not match what is stored: {string.Join(", ", unstable)}.");

        var hashed = manifest.Files.Select(f => f.FileName).ToHashSet(StringComparer.Ordinal);
        var uncited = result.Checks.SelectMany(c => c.EvidenceRefs).Where(name => !hashed.Contains(name)).Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(uncited.Length == 0, $"Collector '{collector.Type}' cites evidence files it did not emit, so they have no hash: {string.Join(", ", uncited)}.");
    }

    [Fact]
    public async Task HonoursCancellation()
    {
        var collector = CreateCollector();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var thrown = await Record.ExceptionAsync(() => collector.CollectAsync(Context(Working, collector, NullLogger.Instance), cancelled.Token));

        Assert.True(thrown is OperationCanceledException, thrown is null
            ? $"Collector '{collector.Type}' finished a run whose token was already cancelled. Pass the token to every call and check it before collecting."
            : $"Collector '{collector.Type}' threw {thrown.GetType().Name} instead of OperationCanceledException when cancelled: {thrown.Message}");
    }

    [Fact]
    public async Task ReturnsFailNotThrowsWhenCredentialsAreRefused()
    {
        var collector = CreateCollector();
        if (collector.Describe().Secrets.Count == 0)
        {
            return; // No secrets, so no credentials to refuse.
        }

        var refused = RefusedCredentials
            ?? throw FailException.ForFailure($"Collector '{collector.Type}' declares secrets. Override RefusedCredentials with a scenario in which the target system refuses them.");

        Result<CollectionResult>? result = null;
        var thrown = await Record.ExceptionAsync(async () => result = await collector.CollectAsync(Context(refused, collector, NullLogger.Instance), Token));

        Assert.True(thrown is null, $"Collector '{collector.Type}' threw {thrown?.GetType().Name} when its credentials were refused. Return Result.Fail with a problem instead: {thrown?.Message}");
        Assert.True(result is Result<CollectionResult>.Fail, $"Collector '{collector.Type}' succeeded although the target system refused its credentials. Return Result.Fail with a problem.");
    }

    [Fact]
    public async Task NeverLogsSecrets()
    {
        var collector = CreateCollector();
        CollectorScenario[] scenarios = RefusedCredentials is { } refused ? [Working, refused] : [Working];

        foreach (var scenario in scenarios)
        {
            var logs = new CapturingLogger();
            var thrown = await Record.ExceptionAsync(() => collector.CollectAsync(Context(scenario, collector, logs), Token));
            var written = logs.Entries.Append(thrown?.ToString() ?? string.Empty).ToArray();

            var leaked = SecretValues(scenario, collector.Describe())
                .Where(secret => secret.Value.Length > 0 && written.Any(text => text.Contains(secret.Value, StringComparison.Ordinal)))
                .Select(secret => secret.Key)
                .ToArray();
            Assert.True(leaked.Length == 0, $"Collector '{collector.Type}' logged the value of secret {string.Join(", ", leaked)}. Log that a secret was used, never its value.");
        }
    }

    private async Task<CollectionResult> CollectWorkingAsync(TCollector collector) =>
        (await collector.CollectAsync(Context(Working, collector, NullLogger.Instance), Token))
            .Match(result => result, problems => throw FailException.ForFailure($"Collector '{collector.Type}' failed its Working scenario: {Describe(problems)}"));

    private static CollectorContext Context(CollectorScenario scenario, ICollector collector, ILogger logger) => new(
        InstanceName,
        new CollectorSettings(scenario.Settings),
        SecretValues(scenario, collector.Describe()).ToDictionary(secret => secret.Key, secret => new Secret(secret.Value), StringComparer.Ordinal),
        new FixedClock(Now),
        logger,
        scenario.HttpClients);

    /// <summary>The scenario's secret values, plus a made-up one for each declared secret the scenario leaves out.</summary>
    private static Dictionary<string, string> SecretValues(CollectorScenario scenario, CollectorDescription description) =>
        description.Secrets
            .Where(secret => !scenario.Secrets.ContainsKey(secret.Name))
            .Select(secret => KeyValuePair.Create(secret.Name, $"contract-kit-secret-{secret.Name}-7c1e9b42d05a"))
            .Concat(scenario.Secrets)
            .ToDictionary(StringComparer.Ordinal);

    private static async Task<IReadOnlyList<CollectedFile>> ReadAllAsync(IReadOnlyList<CollectedEvidence> files)
    {
        var read = new List<CollectedFile>(files.Count);
        foreach (var file in files)
        {
            read.Add(await file.ReadAsync(Token));
        }

        return read;
    }

    private static string Describe(IEnumerable<Problem> problems) => string.Join("; ", problems.Select(p => $"{p.Code}: {p.Message}"));

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
