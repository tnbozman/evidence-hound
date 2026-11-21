namespace EvidenceHound.Functional.Tests;

/// <summary>A disposable, seeded CISO Assistant for functional tests. Not built yet.</summary>
public sealed class CisoAssistantFixture : IAsyncLifetime
{
    public Uri ApiUrl => throw new NotImplementedException();

    public string Token => throw new NotImplementedException();

    public ValueTask InitializeAsync() => throw new NotImplementedException("The seeded CISO Assistant container is not built yet.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
