using Azure.Core;
using Azure.Identity;

namespace EvidenceHound.Collectors.M365.Tests;

/// <summary>Token credentials for tests: one that always hands out the same made-up token, and one that is always refused.</summary>
internal static class TestCredentials
{
    public static readonly string Token = "fake-graph-token-3f9a1c7e5b2d";

    public static TokenCredential Working { get; } = new Static();

    public static TokenCredential Refused { get; } = new Refusing();

    private sealed class Static : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(Token, DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class Refusing : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new AuthenticationFailedException("ClientSecretCredential authentication failed: AADSTS7000215: Invalid client secret provided.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}

/// <summary>Records each wait instead of sleeping.</summary>
internal sealed class RecordingWait : IGraphWait
{
    public List<TimeSpan> Delays { get; } = [];

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}
