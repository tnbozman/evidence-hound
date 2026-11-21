using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.CisoAssistant.Tests;

public sealed class LoggingTests : IDisposable
{
    private readonly FakeCisoAssistant _ca = new();

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Token_is_redacted_in_logs()
    {
        _ca.Server.Given(Request.Create().WithPath("/api/health/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"status": "ok"}"""));

        await _ca.Client.CheckHealthAsync(TestContext.Current.CancellationToken);

        _ca.Logs.Lines.ShouldContain(line => line.Contains("Authorization", StringComparison.Ordinal));
        _ca.Logs.Lines.ShouldAllBe(line => !line.Contains(FakeCisoAssistant.Token, StringComparison.Ordinal));
    }
}
