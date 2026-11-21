using Xunit;

namespace EvidenceHound.Collectors.Testing;

/// <summary>
/// The tests every collector must pass. Derive a public test class, return the collector from <see cref="CreateCollector"/> and,
/// if it talks to a system, describe a working run in <see cref="Working"/> and a refused login in <see cref="RefusedCredentials"/>.
/// </summary>
public abstract class CollectorContractTests<TCollector>
    where TCollector : ICollector
{
    /// <summary>The collector under test.</summary>
    protected abstract TCollector CreateCollector();

    /// <summary>A run in which the target system answers normally. Default: no settings, no secrets, no network.</summary>
    protected virtual CollectorScenario Working { get; } = new();

    /// <summary>A run in which the target system refuses the credentials. Required when the collector declares secrets.</summary>
    protected virtual CollectorScenario? RefusedCredentials => null;

    [Fact]
    public void DescribesAtLeastOneCheck() => throw new NotImplementedException();

    [Fact]
    public Task EmitsOnlyDeclaredCheckIds() => throw new NotImplementedException();

    [Fact]
    public Task EvidenceFilesCanBeHashed() => throw new NotImplementedException();

    [Fact]
    public Task HonoursCancellation() => throw new NotImplementedException();

    [Fact]
    public Task ReturnsFailNotThrowsWhenCredentialsAreRefused() => throw new NotImplementedException();

    [Fact]
    public Task NeverLogsSecrets() => throw new NotImplementedException();
}
