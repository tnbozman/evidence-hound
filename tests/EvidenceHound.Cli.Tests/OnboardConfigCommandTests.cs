using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary><c>hound onboard config</c>: the key goes to an owner-only file and onboarding.yaml holds only a reference to it.</summary>
public sealed class OnboardConfigCommandTests : IDisposable
{
    private const string Key = "example-test-llm-key";
    private readonly string _root = Directory.CreateTempSubdirectory("hound-onboard-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Writes_reference_and_owner_only_key_file()
    {
        var (exit, output, error) = await RunAsync(["onboard", "config", "--provider", "anthropic", "--model", "", "--config", Config], $"{Key}\n");

        exit.ShouldBe(0, error);
        var keyFile = Path.Combine(_root, "home", "hound", "secrets", "llm_api_key");
        (await File.ReadAllTextAsync(keyFile, _ct)).ShouldBe(Key);
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(keyFile).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.GetUnixFileMode(Path.GetDirectoryName(keyFile)!).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var config = await File.ReadAllTextAsync(Config, _ct);
        config.ShouldContain("provider: anthropic");
        config.ShouldContain($"api_key: file:{keyFile}");
        config.ShouldNotContain(Key);
        (output + error).ShouldNotContain(Key);
    }

    [Fact]
    public async Task Check_makes_one_minimal_request_and_never_prints_the_key()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/v1/messages").UsingPost()).RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(
            """{"id":"msg_example","type":"message","role":"assistant","model":"claude-opus-5-5","content":[{"type":"text","text":"{\"ok\":true}"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":20,"output_tokens":5}}"""));
        (await RunAsync(["onboard", "config", "--provider", "anthropic", "--model", "", "--config", Config], $"{Key}\n")).Exit.ShouldBe(0);

        var (exit, output, error) = await RunAsync(["onboard", "config", "--check", "--config", Config], "", ("HOUND_ANTHROPIC_BASE_URL", server.Url!));

        exit.ShouldBe(0, error);
        output.ShouldContain("anthropic claude-opus-5-5: the key works.");
        (output + error).ShouldNotContain(Key);
        server.LogEntries.ShouldHaveSingleItem().RequestMessage!.Headers!["x-api-key"].ShouldBe([Key]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Config => Path.Combine(_root, "repo", "onboarding.yaml");

    private async Task<(int Exit, string Output, string Error)> RunAsync(string[] args, string input, params (string Name, string Value)[] settings)
    {
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var all = settings.ToDictionary(s => s.Name, s => (string?)s.Value, StringComparer.Ordinal);
        all["XDG_CONFIG_HOME"] = Path.Combine(_root, "home");
        var exit = await HoundCli.RunAsync(args, reader, output, error, all, _ct);
        return (exit, output.ToString(), error.ToString());
    }
}
