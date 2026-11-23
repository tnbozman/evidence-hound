using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Sessions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-23, s09-40: <c>hound onboard profile &lt;system&gt;</c> after applicability checks the profile written for the accepted API checks
/// (by a person or their coding assistant; hound drafts none itself). With the go-ahead it samples the recorded Example Asset API once; the
/// person confirms each check's result, which is saved with the hashed recordings as a profile test that passes.
/// </summary>
public sealed class OnboardProfileTests : IDisposable
{
    internal const string Secret = "example-api-client-secret-2d7e";

    private static readonly string Library = Path.Combine(AppContext.BaseDirectory, "files", "profiles");

    private readonly string _root = Directory.CreateTempSubdirectory("hound-onboard-profile-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly WireMockServer _api = WireMockServer.Start();

    public OnboardProfileTests()
    {
        Directory.CreateDirectory(Path.Combine(Repo, "skills"));
        Directory.CreateDirectory(Path.Combine(Repo, "profiles"));
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

    private string Profile => Path.Combine(Repo, "profiles", "example.yaml");

    private static string Example => File.ReadAllText(Path.Combine(Library, "example.yaml"));

    [Fact]
    public async Task The_written_profile_is_sampled_and_the_confirmed_results_become_a_passing_test()
    {
        await File.WriteAllTextAsync(Profile, Example, _ct);

        var (exit, output, error) = await RunAsync("y\ny\ny\ny\n");

        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("Checked profiles/example.yaml.");
        output.ShouldContain("Sampled once, read-only");
        (await File.ReadAllTextAsync(Profile, _ct)).ShouldBe(Example);
        var test = File.ReadAllText(Path.Combine(Repo, "profiles", "example.tests", "onboarding.yaml"));
        test.ShouldContain("\"example.device.os_supported\": \"pass\"");
        test.ShouldNotContain(Secret);
        Directory.GetFiles(Path.Combine(Repo, "profiles", "example.tests", "recordings")).ShouldAllBe(f => !File.ReadAllText(f).Contains("alex@example.com"));

        var replay = await HoundCli.RunAsync(["profile", "test", "example", "--profiles", Path.Combine(Repo, "profiles")], new StringWriter(), new StringWriter(), null, new Dictionary<string, string?>(StringComparer.Ordinal), _ct);
        replay.ShouldBe(ExitCodes.Ok);
    }

    [Fact]
    public async Task A_wrong_path_in_the_profile_is_reported_and_the_profile_is_left_as_written()
    {
        var wrong = Example.Replace("get: v1/orgs/${org_id}/devices", "get: v1/orgs/${org_id}/devcies", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Profile, wrong, _ct);

        var (exit, output, _) = await RunAsync("y\ny\ny\ny\n");

        exit.ShouldBe(ExitCodes.ProfileTestsFailed);
        output.ShouldContain("Still to fix: profile.sample_failed");
        (await File.ReadAllTextAsync(Profile, _ct)).ShouldBe(wrong);
    }

    [Fact]
    public async Task Without_a_profile_it_points_to_the_coding_assistant()
    {
        var (exit, _, error) = await RunAsync("");

        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("profiles/example.yaml does not exist");
        error.ShouldContain("hound agent setup");
        _api.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_the_go_ahead_nothing_calls_the_api()
    {
        await File.WriteAllTextAsync(Profile, Example, _ct);

        var (exit, output, error) = await RunAsync("n\n", sample: false);

        exit.ShouldBe(ExitCodes.Ok, error);
        _api.LogEntries.ShouldBeEmpty();
        output.ShouldContain("not sampled");
    }

    public void Dispose()
    {
        _api.Dispose();
        Directory.Delete(_root, recursive: true);
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
            ["EXAMPLE_API_SECRET"] = Secret,
        };
        var exit = await HoundCli.RunAsync(args, reader, output, error, settings, _ct);
        return (exit, output.ToString(), error.ToString());
    }
}
