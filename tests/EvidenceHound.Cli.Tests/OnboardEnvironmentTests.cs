using EvidenceHound.Collectors.M365;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using AccessToken = Azure.Core.AccessToken;
using TokenCredential = Azure.Core.TokenCredential;
using TokenRequestContext = Azure.Core.TokenRequestContext;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-28: <c>hound onboard environment</c> on a copy of the example repo, without a model, with recorded Microsoft Graph answers: one decision
/// per Essential Eight mitigation, written to scope/capabilities.yaml and as not-applicable rows in each system's own audit.
/// </summary>
public sealed class OnboardEnvironmentTests : IDisposable
{
    internal const string Signals = """
        {"source": "Microsoft Graph (recorded 2026-11-23)", "licences": ["AAD_PREMIUM_P2", "INTUNE_A"], "configuration_types": []}
        """;

    // Who decides and the approving role, then the mitigations in the framework's order: pa, pos, mfa, rap, ac, rmom, uah, rb.
    internal const string Answers =
        "Alex Example\nGRC lead\n"
        + "g\n"                                     // pa: a gap
        + "a\n"                                     // pos: accept Example 365 (Intune licence)
        + "a\n"                                     // mfa: accept Example 365 (Entra ID P2)
        + "a\n"                                     // rap: accept Example 365 (Entra ID P2)
        + "s\nExample app control\nSUP-EXAPPCTL\n"  // ac: a third-party product not onboarded yet, and its supplier
        + "m\n"                                     // rmom: manual
        + "a\n"                                     // uah: accept Example 365
        + "s\nExample backup service\n";            // rb: the backup service

    private readonly string _root = Directory.CreateTempSubdirectory("hound-onboard-environment-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public OnboardEnvironmentTests()
    {
        TestFiles.CopyExampleRepo(Repo);
        File.WriteAllText(Path.Combine(Repo, "signals.json"), Signals);
    }

    private string Repo => Path.Combine(_root, "repo");

    [Fact]
    public async Task Named_system_and_supplier_are_queued_for_onboarding()
    {
        var (exit, output, error) = await RunAsync(Answers);

        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldContain("[3/8] mfa");
        error.ShouldContain("Microsoft Graph (recorded 2026-11-23): licence AAD_PREMIUM_P2 (Microsoft Entra ID P2) is subscribed");

        var parsed = ScopeLoader.Parse(
            new ScopeSource("setup.yaml", "organisation:\n  root_folder: Global\n"), null, [],
            capabilities: new ScopeSource("capabilities.yaml", await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "capabilities.yaml"), _ct)));
        parsed.Problems.ShouldBeEmpty();
        var map = parsed.Scope.Capabilities.ShouldNotBeNull();
        map.Mitigations.Select(m => (m.Mitigation, m.ProvidedBy)).ShouldBe(
        [
            ("pa", CapabilityProvision.Gap), ("pos", CapabilityProvision.System), ("mfa", CapabilityProvision.System), ("rap", CapabilityProvision.System),
            ("ac", CapabilityProvision.System), ("rmom", CapabilityProvision.Manual), ("uah", CapabilityProvision.System), ("rb", CapabilityProvision.System),
        ]);
        map.Mitigations.ShouldAllBe(m => m.ApprovedBy == "GRC lead");
        map.Mitigations.Single(m => m.Mitigation == "mfa").Signals.ShouldHaveSingleItem().ShouldContain("AAD_PREMIUM_P2");
        map.Mitigations.Single(m => m.Mitigation == "ac").Systems.Select(s => s.Value).ShouldBe(["Example app control"]);

        // No blob: the Essential Eight audit of Example 365, the laptops and the backup service gets application control as not applicable,
        // provided by the product, and the whole scope still validates.
        var audit = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "audits", "ex-e8-2026.yaml"), _ct);
        audit.ShouldContain("result: not_applicable");
        audit.ShouldContain("Provided by Example app control (no audit yet; onboard it with 'hound onboard Example app control')");
        output.ShouldContain("Wrote scope/capabilities.yaml");
        output.ShouldContain("Next: onboard Example app control (ac) with 'hound onboard \"Example app control\"'; its supplier SUP-EXAPPCTL goes through the supplier gate first.");
        output.ShouldContain("Gap: pa has no product; no system's audit answers its 14 requirement(s).");
        var validate = await HoundCli.RunAsync(["scope", "validate", Path.Combine(Repo, "scope")], new StringWriter(), new StringWriter(), null, new Dictionary<string, string?>(StringComparer.Ordinal), _ct);
        validate.ShouldBe(ExitCodes.Ok);

        // Run again: every mitigation is decided, so nothing is asked, and no row is added twice.
        (exit, output, _) = await RunAsync("");
        exit.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("All 8 mitigations are decided");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "audits", "ex-e8-2026.yaml"), _ct)).ShouldBe(audit);
    }

    [Fact]
    public async Task Graph_signals_come_from_the_m365_collector_in_hound_yaml()
    {
        using var graph = WireMockServer.Start();
        Respond(graph, "/v1.0/subscribedSkus", """{"value": [{"skuPartNumber": "AAD_PREMIUM_P2", "capabilityStatus": "Enabled"}]}""");
        Respond(graph, "/v1.0/deviceManagement/deviceConfigurations", """{"value": [{"@odata.type": "#microsoft.graph.windowsUpdateForBusinessConfiguration", "id": "c1"}]}""");
        var config = Path.Combine(Repo, "hound.yaml");
        var text = await File.ReadAllTextAsync(config, _ct);
        await File.WriteAllTextAsync(
            config,
            text.Replace("      supported_windows_builds: \"10.0.22631, 10.0.26100\"\n", $"      supported_windows_builds: \"10.0.22631, 10.0.26100\"\n      graph_url: {graph.Url}/\n", StringComparison.Ordinal)
                .Replace("client_certificate: file:/run/secrets/m365_client_certificate", "client_secret: env:EXAMPLE_M365_SECRET", StringComparison.Ordinal),
            _ct);

        var (exit, _, error) = await RunAsync("Alex Example\nGRC lead\ng\na\nq\n", ["--graph", "example-m365"]);

        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldContain("Microsoft Graph (example-m365): Intune holds a Windows Update for Business ring");
        error.ShouldNotContain("example-client-secret-value");
        graph.LogEntries.Count.ShouldBe(2);
        var map = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "capabilities.yaml"), _ct);
        map.ShouldContain("Example 365");
    }

    [Fact]
    public async Task Graph_and_recorded_signals_are_not_both_given()
    {
        var (exit, _, error) = await RunAsync("", ["--graph", "example-m365", "--signals", Path.Combine(Repo, "signals.json")]);

        exit.ShouldBe(ExitCodes.Configuration, error);
        error.ShouldContain("not both");
    }

    [Fact]
    public async Task An_unknown_collector_is_named()
    {
        var (exit, _, error) = await RunAsync("", ["--graph", "example-nothing"]);

        exit.ShouldBe(ExitCodes.Configuration, error);
        error.ShouldContain("example-nothing");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<(int Exit, string Output, string Error)> RunAsync(string input, string[]? source = null)
    {
        string[] args =
        [
            "onboard", "environment", "--repo", Repo, "--framework", Path.Combine(Repo, "frameworks", "essential-eight-2023.json"),
            .. source ?? ["--signals", Path.Combine(Repo, "signals.json")], "--microsoft-system", "Example 365", "--no-model",
        ];
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_CONFIG_HOME"] = Path.Combine(_root, "home"),
            ["EXAMPLE_M365_SECRET"] = "example-client-secret-value-7c2d",
        };
        var exit = await HoundCli.RunAsync(
            args, reader, output, error, null, [], settings, services => services.Replace(ServiceDescriptor.Singleton<IGraphCredentialFactory>(new FixedCredentials())), _ct);
        return (exit, output.ToString(), error.ToString());
    }

    private static void Respond(WireMockServer server, string path, string body) =>
        server.Given(Request.Create().WithPath(path).UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body));

    /// <summary>Hands out a made-up token, whatever the app registration.</summary>
    private sealed class FixedCredentials : IGraphCredentialFactory
    {
        public Result<TokenCredential> Create(GraphConnection connection) => Result.Ok<TokenCredential>(new Fixed());

        private sealed class Fixed : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("example-graph-token", DateTimeOffset.UtcNow.AddHours(1));

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                ValueTask.FromResult(GetToken(requestContext, cancellationToken));
        }
    }
}
