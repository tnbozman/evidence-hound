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
    public async Task Agent_decides_capabilities_through_pending_and_decide()
    {
        // s09-41: no prompt is answered. pending lists what is undecided; decide records one mitigation, with no approver.
        var (exit, output, error) = await AgentAsync("pending");
        exit.ShouldBe(ExitCodes.Ok, error);
        var pending = System.Text.Json.Nodes.JsonNode.Parse(output)!;
        pending["pending"]!.AsArray().Select(m => m!["mitigation"]!.GetValue<string>()).ShouldBe(["pa", "pos", "mfa", "rap", "ac", "rmom", "uah", "rb"]);
        pending["pending"]![0]!["requirements"]!.GetValue<int>().ShouldBeGreaterThan(0);
        pending["systems"]!.AsArray().Select(s => s!.GetValue<string>()).ShouldContain("Example 365");

        (exit, output, error) = await AgentAsync("decide", "mfa", "--provided-by", "system", "--system", "Example 365", "--reason", "Entra ID enforces multi-factor authentication.", "--evidence", "collector");
        exit.ShouldBe(ExitCodes.Ok, error);
        System.Text.Json.Nodes.JsonNode.Parse(output)!["approved"]!.GetValue<bool>().ShouldBeFalse();
        (exit, _, error) = await AgentAsync("decide", "pa", "--provided-by", "gap", "--reason", "No product patches applications yet.");
        exit.ShouldBe(ExitCodes.Ok, error);

        var parsed = ScopeLoader.Parse(
            new ScopeSource("setup.yaml", "organisation:\n  root_folder: Global\n"), null, [],
            capabilities: new ScopeSource("capabilities.yaml", await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "capabilities.yaml"), _ct)));
        parsed.Scope.Capabilities!.Mitigations.Select(m => (m.Mitigation, m.ProvidedBy, m.ApprovedBy)).ShouldBe([("pa", CapabilityProvision.Gap, null), ("mfa", CapabilityProvision.System, null)]);
        parsed.Scope.Capabilities.Mitigations[1].Reason.ShouldBe("Entra ID enforces multi-factor authentication.");

        // Deciding again replaces a draft. What is not valid is refused: a mitigation the framework lacks, a system decision without a system.
        (exit, _, error) = await AgentAsync("decide", "pa", "--provided-by", "manual", "--reason", "The owner reports patching each month.");
        exit.ShouldBe(ExitCodes.Ok, error);
        (exit, _, error) = await AgentAsync("decide", "nope", "--provided-by", "gap", "--reason", "None.");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("is not a mitigation of");
        (exit, _, error) = await AgentAsync("decide", "rb", "--provided-by", "system", "--reason", "The backup service.");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("name it with --system");

        (exit, output, error) = await AgentAsync("pending");
        System.Text.Json.Nodes.JsonNode.Parse(output)!["pending"]!.AsArray().Count.ShouldBe(6);
        System.Text.Json.Nodes.JsonNode.Parse(output)!["drafts"]!.AsArray().Select(m => m!["mitigation"]!.GetValue<string>()).ShouldBe(["pa", "mfa"]);

        // A person approves each on a terminal, and an approved decision is not replaced by an agent.
        (exit, output, error) = await OnboardAgentCommandTests.RunAsync(["approve", "--repo", Repo], "Sam Example\ny\ny\n", terminal: true, Path.Combine(_root, "home"), _ct);
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("Approved 2 of 2.");
        (exit, _, error) = await AgentAsync("decide", "mfa", "--provided-by", "gap", "--reason", "Changed my mind.");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("was approved by Sam Example");
    }

    private Task<(int Exit, string Output, string Error)> AgentAsync(params string[] args) =>
        OnboardAgentCommandTests.RunAsync(["onboard", "environment", .. args, "--repo", Repo, "--json"], "", terminal: false, Path.Combine(_root, "home"), _ct);

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
        audit.ShouldNotContain("result: not_applicable");
        audit.ShouldContain("provided_by: [Example app control]");
        audit.ShouldContain("Met by Example app control (no audit yet; onboard it with 'hound onboard Example app control')");
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
    public async Task Requirements_the_mapping_computes_are_left_to_it_so_the_scope_validates_with_the_deployment()
    {
        var (exit, output, error) = await RunAsync(Answers);

        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("Left to the mapping of audit EX-E8-2026, which computes them: ");
        var problems = new StringWriter();
        var validate = await HoundCli.RunAsync(
            ["scope", "validate", Path.Combine(Repo, "scope"), "--config", Path.Combine(Repo, "hound.yaml")], new StringWriter(), problems, null, new Dictionary<string, string?>(StringComparer.Ordinal), _ct);
        validate.ShouldBe(ExitCodes.Ok, problems.ToString());
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

    [Fact]
    public async Task The_model_proposes_after_the_signals_and_the_person_decides()
    {
        using var anthropic = WireMockServer.Start();
        var proposal = new System.Text.Json.Nodes.JsonObject
        {
            ["mitigations"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
            {
                ["mitigation"] = "pa",
                ["provided_by"] = "gap",
                ["systems"] = new System.Text.Json.Nodes.JsonArray(),
                ["reasoning"] = "No vulnerability scanner is in scope.",
            }),
        }.ToJsonString();
        var message = new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = "msg_example",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = "claude-opus-5-5",
            ["content"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["type"] = "text", ["text"] = proposal }),
            ["stop_reason"] = "end_turn",
            ["stop_sequence"] = null,
            ["usage"] = new System.Text.Json.Nodes.JsonObject { ["input_tokens"] = 900, ["output_tokens"] = 60 },
        }.ToJsonString();
        anthropic.Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(message));
        await File.WriteAllTextAsync(
            Path.Combine(Repo, "onboarding.yaml"),
            $"llm:\n  provider: anthropic\n  model: \"\"\n  api_key: env:EXAMPLE_LLM_KEY\nskills_path: {Path.Combine(Repo, "skills")}\n",
            _ct);

        var (exit, _, error) = await RunAsync("Alex Example\nGRC lead\na\nq\n", ["--signals", Path.Combine(Repo, "signals.json")], model: anthropic.Url);

        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldContain("Suggested: a gap.");
        error.ShouldContain("Model: No vulnerability scanner is in scope.");
        var sent = anthropic.LogEntries.ShouldHaveSingleItem().RequestMessage!.Body!;
        sent.ShouldContain("environment-capabilities");
        sent.ShouldContain("AAD_PREMIUM_P2");
        var map = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "capabilities.yaml"), _ct);
        map.ShouldContain("Accepted by Alex Example from the model's proposal: No vulnerability scanner is in scope.");
        Directory.GetFiles(Path.Combine(Repo, "onboarding", "environment", "requests")).ShouldNotBeEmpty();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<(int Exit, string Output, string Error)> RunAsync(string input, string[]? source = null, string? model = null)
    {
        string[] args =
        [
            "onboard", "environment", "--repo", Repo, "--framework", Path.Combine(Repo, "frameworks", "essential-eight-2023.json"),
            .. source ?? ["--signals", Path.Combine(Repo, "signals.json")], "--microsoft-system", "Example 365", .. model is null ? ["--no-model"] : Array.Empty<string>(),
        ];
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_CONFIG_HOME"] = Path.Combine(_root, "home"),
            ["EXAMPLE_M365_SECRET"] = "example-client-secret-value-7c2d",
            ["EXAMPLE_LLM_KEY"] = "example-llm-key-value-5e1a",
            ["HOUND_ANTHROPIC_BASE_URL"] = model,
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
