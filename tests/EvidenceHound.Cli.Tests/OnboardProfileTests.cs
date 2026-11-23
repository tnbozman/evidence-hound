using System.Text.Json.Nodes;
using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Sessions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-23: <c>hound onboard profile &lt;system&gt;</c> after applicability: a fake model drafts the profile for the accepted API checks; with
/// the go-ahead the draft samples the recorded Example Asset API once; a wrong path in the first draft is fed back and fixed in the second
/// round; the person confirms each check's result, which is saved with the hashed recordings as a profile test that passes.
/// </summary>
public sealed class OnboardProfileTests : IDisposable
{
    private const string Secret = "example-api-client-secret-2d7e";

    private static readonly string Library = Path.Combine(AppContext.BaseDirectory, "files", "profiles");

    private readonly string _root = Directory.CreateTempSubdirectory("hound-onboard-profile-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly WireMockServer _model = WireMockServer.Start();
    private readonly WireMockServer _api = WireMockServer.Start();

    public OnboardProfileTests()
    {
        Directory.CreateDirectory(Path.Combine(Repo, "skills"));
        Directory.CreateDirectory(Path.Combine(Repo, "profiles"));
        File.WriteAllText(Path.Combine(Repo, "onboarding.yaml"), $"llm:\n  provider: anthropic\n  model: \"\"\n  api_key: env:EXAMPLE_LLM_KEY\nskills_path: {Path.Combine(Repo, "skills")}\n");
        var folder = Path.Combine(Repo, "onboarding", "EXAMPLE-ASSETS");
        Directory.CreateDirectory(folder);
        string[] keys = ["EX-1", "EX-2", "EX-3", "EX-4"];
        var session = new OnboardSession("EXAMPLE-ASSETS", OnboardStage.Decided)
        {
            Reviewer = "Alex Example",
            Product = "Example Asset API",
            Framework = "essential-eight-2023",
            Proposals = [.. keys.Select(k => new Proposal(k, $"urn:example:{k}", k, $"Requirement {k}", DecisionKind.ApiCheck, "The API reads it.", null))],
            Decisions = [.. keys.Select(k => new Decision(k, $"urn:example:{k}", k, DecisionKind.ApiCheck, "The API reads it.", DecisionKind.ApiCheck, "Alex Example"))],
        };
        File.WriteAllText(Path.Combine(folder, "session.json"), OnboardSessionJson.Write(session));

        var example = File.ReadAllText(Path.Combine(Library, "example.yaml"));
        _model.Given(Request.Create().WithPath("/v1/messages").UsingPost()).InScenario("rounds").WillSetStateTo("second")
            .RespondWith(Reply(example.Replace("get: v1/orgs/${org_id}/devices", "get: v1/orgs/${org_id}/devcies", StringComparison.Ordinal)));
        _model.Given(Request.Create().WithPath("/v1/messages").UsingPost()).InScenario("rounds").WhenStateIs("second")
            .RespondWith(Reply(example));

        var recordings = Path.Combine(Library, "example.tests", "recordings");
        Serve("/oauth2/token", "token.json", null, "POST");
        Serve("/v1/whoami", "whoami.json");
        Serve("/v1/orgs/org-0001/devices", "devices.json");
        Serve("/v1/orgs/org-0001/devices", "devices.2.json", ("cursor", "page-2"));
        Serve("/v1/orgs/org-0001/users", "users.json", ("offset", "0"));
        Serve("/v1/orgs/org-0001/users", "users.2.json", ("offset", "2"));
        Serve("/v1/orgs/org-0001/roles", "roles.json");

        void Serve(string path, string file, (string Name, string Value)? param = null, string method = "GET")
        {
            var request = Request.Create().WithPath(path).UsingMethod(method);
            var body = File.ReadAllText(Path.Combine(recordings, file)).Replace("{{api}}", _api.Url!, StringComparison.Ordinal);
            _api.Given(param is { } p ? request.WithParam(p.Name, p.Value) : request).AtPriority(param is null ? 10 : 5)
                .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body));
        }
    }

    private string Repo => Path.Combine(_root, "repo");

    [Fact]
    public async Task A_wrong_path_is_fixed_in_the_second_round_and_the_confirmed_results_become_a_passing_test()
    {
        var (exit, output, error) = await RunAsync("y\ny\ny\ny\n");

        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("2 round(s)");
        _model.LogEntries.Count.ShouldBe(2);
        var second = _model.LogEntries[1].RequestMessage!.Body!;
        second.ShouldContain("devcies");
        second.ShouldContain("profile.sample_failed");
        var sent = string.Concat(_model.LogEntries.Select(e => e.RequestMessage!.Body));
        sent.ShouldNotContain("alex@example.com");
        sent.ShouldNotContain(Secret);

        File.ReadAllText(Path.Combine(Repo, "profiles", "example.yaml")).ShouldNotContain("devcies");
        var test = File.ReadAllText(Path.Combine(Repo, "profiles", "example.tests", "onboarding.yaml"));
        test.ShouldContain("\"example.device.os_supported\": \"pass\"");
        test.ShouldNotContain(Secret);
        Directory.GetFiles(Path.Combine(Repo, "profiles", "example.tests", "recordings")).ShouldAllBe(f => !File.ReadAllText(f).Contains("alex@example.com"));

        var replay = await HoundCli.RunAsync(["profile", "test", "example", "--profiles", Path.Combine(Repo, "profiles")], new StringWriter(), new StringWriter(), null, new Dictionary<string, string?>(StringComparer.Ordinal), _ct);
        replay.ShouldBe(ExitCodes.Ok);
    }

    [Fact]
    public async Task Without_the_go_ahead_nothing_calls_the_api()
    {
        var (exit, output, error) = await RunAsync("n\n", sample: false);

        exit.ShouldBe(ExitCodes.Ok, error);
        _api.LogEntries.ShouldBeEmpty();
        output.ShouldContain("not sampled");
        File.Exists(Path.Combine(Repo, "profiles", "example.yaml")).ShouldBeTrue();
    }

    public void Dispose()
    {
        _model.Dispose();
        _api.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private static IResponseBuilder Reply(string profile)
    {
        var text = new JsonObject { ["profile_yaml"] = profile, ["notes"] = "Drafted from the API summary." }.ToJsonString();
        var message = new JsonObject
        {
            ["id"] = "msg_example",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = "claude-opus-5-5",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["stop_reason"] = "end_turn",
            ["stop_sequence"] = null,
            ["usage"] = new JsonObject { ["input_tokens"] = 900, ["output_tokens"] = 600 },
        };
        return Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(message.ToJsonString());
    }

    private async Task<(int Exit, string Output, string Error)> RunAsync(string input, bool sample = true)
    {
        string[] args =
        [
            "onboard", "profile", "EXAMPLE-ASSETS", "--repo", Repo, "--name", "example",
            "--set", "client_id=00000000-0000-4000-8000-0000000000c1", "--set", $"base_url={_api.Url}/", "--set", $"token_url={_api.Url}/oauth2/token",
            "--set", "supported_os=14, 15", "--secret", "client_secret=env:EXAMPLE_API_SECRET", .. sample ? ["--sample"] : Array.Empty<string>(),
        ];
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_CONFIG_HOME"] = Path.Combine(_root, "home"),
            ["EXAMPLE_LLM_KEY"] = "example-llm-key-value-5e1a",
            ["EXAMPLE_API_SECRET"] = Secret,
            ["HOUND_ANTHROPIC_BASE_URL"] = _model.Url,
        };
        var exit = await HoundCli.RunAsync(args, reader, output, error, settings, _ct);
        return (exit, output.ToString(), error.ToString());
    }
}
