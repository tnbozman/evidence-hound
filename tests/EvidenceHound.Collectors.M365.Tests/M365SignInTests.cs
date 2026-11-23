using EvidenceHound.Core;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.M365DevServer;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Collectors.M365.Tests;

/// <summary>s09-52: <c>sign_in: none</c> reads the dev server with a placeholder token, and is refused for any address that is not loopback.</summary>
public sealed class M365SignInTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 2, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_sign_in_the_collector_reads_the_dev_server_and_asks_for_no_credential()
    {
        using var server = DevGraphServer.Start(DevTenant.Compliant);
        var credentials = new Refusing();

        var result = await CollectAsync(credentials, server.HttpClients, ("graph_url", server.Url.ToString()), ("sign_in", "none"));

        result.Value().Checks.Count.ShouldBe(11);
        credentials.Asked.ShouldBeFalse();
        server.Requests.ShouldAllBe(r => r.Status == 200);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://graph.microsoft.com/")]
    [InlineData("http://graph.example.test/")]
    [InlineData("http://localhost.example.com/")]
    public async Task Without_sign_in_is_refused_for_an_address_that_is_not_loopback(string? graphUrl)
    {
        (string, string)[] settings = graphUrl is null ? [("sign_in", "none")] : [("graph_url", graphUrl), ("sign_in", "none")];
        var credentials = new Refusing();

        var problem = (await CollectAsync(credentials, new Unused(), settings)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("m365.sign_in_not_loopback");
        problem.Message.ShouldContain("localhost");
        credentials.Asked.ShouldBeFalse();
    }

    [Theory]
    [InlineData("http://127.0.0.1:5365/")]
    [InlineData("http://[::1]:5365/")]
    [InlineData("https://localhost:5365/")]
    public void Loopback_addresses_are_accepted(string graphUrl) =>
        M365Options.FromSettings(Settings(("graph_url", graphUrl), ("sign_in", "none"))).ShouldBeOfType<Result<M365Options>.Ok>().Value.SignIn.ShouldBe("none");

    [Fact]
    public void Sign_in_is_app_or_none()
    {
        M365Options.FromSettings(Settings()).ShouldBeOfType<Result<M365Options>.Ok>().Value.SignIn.ShouldBe("app");
        M365Options.FromSettings(Settings(("sign_in", "maybe"))).Problems().ShouldContain(p => p.Message.Contains("sign_in", StringComparison.Ordinal));
    }

    private static CollectorSettings Settings(params (string Key, string Value)[] extra)
    {
        var settings = DevTenant.Settings();
        settings.Remove(M365Settings.GraphUrl);
        foreach (var (key, value) in extra)
        {
            settings[key] = value;
        }

        return new CollectorSettings(settings);
    }

    private static Task<Result<CollectionResult>> CollectAsync(IGraphCredentialFactory credentials, IHttpClientFactory http, params (string Key, string Value)[] extra)
    {
        var context = new CollectorContext("example-m365", Settings(extra), new Dictionary<string, Secret>(), new FixedClock(Now), NullLogger.Instance, http);
        return new M365Collector(credentials, new RecordingWait()).CollectAsync(context, Token);
    }

    /// <summary>A credential factory that records being asked and refuses: with no sign-in it must never be asked.</summary>
    private sealed class Refusing : IGraphCredentialFactory
    {
        public bool Asked { get; private set; }

        public Result<Azure.Core.TokenCredential> Create(GraphConnection connection)
        {
            Asked = true;
            return Result.Fail<Azure.Core.TokenCredential>(new Problem("test.asked", "the credential factory was asked"));
        }
    }

    private sealed class Unused : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("no request is sent when the settings are refused");
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
