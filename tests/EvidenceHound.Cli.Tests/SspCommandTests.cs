using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Reporting;
using EvidenceHound.Core.Scope;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary>s11-02: <c>hound ssp build</c> reads CISO Assistant once and prints the plan's model, each value with its source.</summary>
public sealed class SspCommandTests : IDisposable
{
    private static readonly string Token = "example-token-for-cli-tests";

    private static readonly string Ssp = """
        ssps:
          - programme: baseline
            profile: ism
            system_name: Example corporate IT
            perimeter: Example baseline
            version: "0.1"
            roles:
              system_owner: it@example.com
              authorising_official: it@example.com
              security_contact: it@example.com
            sections:
              purpose: ssp/purpose.md
              environment: ssp/environment.md
              management: ssp/management.md
              laws_and_policies: ssp/laws-and-policies.md
        """;

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly WireMockServer _ca = WireMockServer.Start();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-ssp-tests-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private string ScopeDir => Path.Combine(_workDir, "scope");

    [Fact]
    public async Task Builds_the_plan_from_what_ciso_assistant_holds_and_sends_only_gets()
    {
        await WriteScopeAsync(Ssp);
        Instance();

        var exit = await Build("--json");

        exit.ShouldBe(ExitCodes.Ok, _error.ToString());
        var plan = JsonNode.Parse(_output.ToString())!;
        plan["programme"]!.GetValue<string>().ShouldBe("baseline");
        var system = plan["inventory"]!.AsArray().ShouldHaveSingleItem()!;
        (system["name"]!["value"]!.GetValue<string>(), system["owner"]!["value"]!.GetValue<string>()).ShouldBe(("Example 365", "it@example.com"));
        system["owner"]!["source"]!["kind"]!.GetValue<string>().ShouldBe("ciso_assistant");
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Strict_passes_without_gaps_and_the_poam_is_printed()
    {
        await WriteScopeAsync(Ssp);
        Instance();

        (await Build("--strict")).ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldContain("No gaps.");
        _output.GetStringBuilder().Clear();

        var exit = await HoundCli.RunAsync(
            ["ssp", "poam", "--programme", "baseline", "--scope", ScopeDir, "--json"],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token },
            _ct);

        exit.ShouldBe(ExitCodes.Ok, _error.ToString());
        JsonNode.Parse(_output.ToString())!["items"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Prints_the_sp_800_18_layout_with_section_files_and_the_annex()
    {
        await WriteScopeAsync(Ssp);
        Directory.CreateDirectory(Path.Combine(_workDir, "ssp"));
        await File.WriteAllTextAsync(Path.Combine(_workDir, "ssp", "purpose.md"), "Example corporate IT runs the company's mail and files.\n", _ct);
        Instance();

        (await Build()).ShouldBe(ExitCodes.Ok, _error.ToString());

        var markdown = _output.ToString();
        markdown.ShouldContain("## 9. General description and purpose\n\nExample corporate IT runs the company's mail and files.");
        markdown.ShouldContain("## 13. Security controls (annex)");
        markdown.ShouldContain("| EX-1.1 |");
        markdown.ShouldContain("_Section `environment` is not written yet._");
    }

    [Fact]
    public async Task The_nist_800_171_profile_adds_cmmc_asset_categories_and_poam_rules()
    {
        // s17-03: one layout; the profile adds the CMMC asset category column, the gaps for untagged systems and CMMC's POA&M conditions.
        await WriteScopeAsync(Ssp.Replace("profile: ism", "profile: nist-800-171", StringComparison.Ordinal));
        Instance();

        (await Build()).ShouldBe(ExitCodes.Ok, _error.ToString());

        var markdown = _output.ToString();
        markdown.ShouldContain("| System | Type | Owner | Description | CMMC asset category |\n");
        markdown.ShouldContain("- Example 365: no CMMC asset category;");
        markdown.ShouldContain("A Conditional CMMC Level 2 status also needs an SPRS score of at least 88");
    }

    [Fact]
    public async Task Sprs_affirmation_pack_reads_the_plan_and_the_month_snapshot()
    {
        // s17-06: hound sprs affirmation-pack, on this fixture's plan with the nist-800-171 profile and a posture snapshot in the evidence store.
        await WriteScopeAsync(Ssp.Replace("profile: ism", "profile: nist-800-171", StringComparison.Ordinal));
        Instance();
        var evidence = Path.Combine(_workDir, "evidence");
        Directory.CreateDirectory(Path.Combine(evidence, "reports"));
        var config = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(config, $"ciso_assistant:\n  url: {_ca.Url}/api/\n  token: env:HOUND_CA_TOKEN\nevidence:\n  root: {evidence}\ncollectors: []\n", _ct);
        var posture = new PostureSnapshot(
            new ReportingPeriod(PeriodKind.Month, 2026, 11), new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero), [], [], [], [], [], [], [],
            [new DueItem(DueKind.ManualEvidence, "EV-BACKUP-RESTORE: Daily backups", "Backup owners", new DateOnly(2026, 11, 15), "pending")]);
        await File.WriteAllTextAsync(Path.Combine(evidence, "reports", "posture-2026-11.json"), PostureSnapshotJson.Write(posture), _ct);

        var exit = await HoundCli.RunAsync(
            ["sprs", "affirmation-pack", "--programme", "baseline", "--period", "2026-11", "--scope", ScopeDir, "--config", config],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            new Dictionary<string, string?> { ["HOUND_CA_TOKEN"] = Token, ["HOUND_TODAY"] = "2026-12-02" },
            _ct);

        exit.ShouldBe(ExitCodes.Ok, _error.ToString());
        var pack = _output.ToString();
        pack.ShouldStartWith("# Annual affirmation pack: baseline, 2026-12-02\n");
        pack.ShouldContain("SPRS score: -203 (from 110");
        pack.ShouldContain("No open POA&M items.");
        pack.ShouldContain("| EV-BACKUP-RESTORE: Daily backups | Backup owners | 2026-11-15 | 17 |");
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Json_carries_the_annex_with_poam_links()
    {
        await WriteScopeAsync(Ssp);
        Instance();

        (await Build("--json")).ShouldBe(ExitCodes.Ok, _error.ToString());

        var plan = JsonNode.Parse(_output.ToString())!;
        var row = plan["annex"]!.AsArray().ShouldHaveSingleItem()!;
        row["control"]!.GetValue<string>().ShouldBe("EX-1.1");
        row["asd_status"]!["value"]!.GetValue<string>().ShouldBe("Not assessed"); // The fixture holds no row for EX-1.1.
        row["responsible_entity"]!["value"]!.GetValue<string>().ShouldBe("Consumer");
        row["control_types"]!.AsArray().ShouldBeEmpty();
        row["maturity_levels"]!.AsArray().Select(l => l!.GetValue<string>()).ShouldBe(["1"]);
        row["status"]!["value"].ShouldNotBeNull();
        plan["classification"]!["value"].ShouldBeNull();
    }

    [Fact]
    public async Task Snapshot_out_rebuilds_the_same_draft_without_ciso_assistant()
    {
        // s11-05: the reviewed draft is reproducible from its snapshot, so a signed version can be checked by hash.
        await WriteScopeAsync(Ssp);
        Instance();
        var snapshot = Path.Combine(_workDir, "ssp-0.1.json");

        (await Build("--snapshot-out", snapshot)).ShouldBe(ExitCodes.Ok, _error.ToString());
        var draft = _output.ToString();
        _logs.Messages.ShouldContain(m => m.Contains($"draft SHA-256 {SspVersion.Hash(draft)}", StringComparison.Ordinal));
        _output.GetStringBuilder().Clear();
        var calls = _ca.LogEntries.Count;

        (await Build("--from-snapshot", snapshot)).ShouldBe(ExitCodes.Ok, _error.ToString());

        _output.ToString().ShouldBe(draft);
        draft.ShouldContain("(DRAFT)");
        _ca.LogEntries.Count.ShouldBe(calls);
    }

    [Fact]
    public async Task A_signed_snapshot_builds_the_approved_plan()
    {
        await WriteScopeAsync(Ssp);
        Instance();
        var snapshot = Path.Combine(_workDir, "ssp-0.1.json");
        (await Build("--snapshot-out", snapshot)).ShouldBe(ExitCodes.Ok, _error.ToString());
        await WriteScopeAsync(Ssp + SignOff(SspVersion.Hash(_output.ToString())));
        _output.GetStringBuilder().Clear();

        (await Build("--from-snapshot", snapshot)).ShouldBe(ExitCodes.Ok, _error.ToString());

        _output.ToString().ShouldNotContain("(DRAFT)");
        _output.ToString().ShouldContain("> Approved by it@example.com on 2026-11-24.");
    }

    [Fact]
    public async Task A_snapshot_that_is_not_the_reviewed_draft_is_refused()
    {
        await WriteScopeAsync(Ssp + SignOff(new string('0', 64)));
        Instance();
        var snapshot = Path.Combine(_workDir, "ssp-0.1.json");
        (await Build("--snapshot-out", snapshot)).ShouldBe(ExitCodes.Ok, _error.ToString());

        (await Build("--from-snapshot", snapshot)).ShouldBe(ExitCodes.Configuration);

        ErrorCodes().ShouldContain("ssp.signoff_hash_mismatch");
    }

    [Fact]
    public async Task Format_docx_also_writes_the_plan_as_a_word_file()
    {
        await WriteScopeAsync(Ssp);
        Instance();
        var docx = Path.Combine(_workDir, "ssp-0.1.docx");

        (await Build("--format", "docx", "--out", docx)).ShouldBe(ExitCodes.Ok, _error.ToString());

        _output.ToString().ShouldContain("(DRAFT)");
        using var zip = new System.IO.Compression.ZipArchive(File.OpenRead(docx));
        zip.GetEntry("word/document.xml").ShouldNotBeNull();
        zip.GetEntry("word/header1.xml").ShouldNotBeNull();
    }

    [Fact]
    public async Task Format_docx_needs_a_file_to_write()
    {
        await WriteScopeAsync(Ssp);
        Instance();

        (await Build("--format", "docx")).ShouldBe(ExitCodes.Configuration);

        ErrorCodes().ShouldContain("ssp.out_required");
    }

    [Fact]
    public async Task Suggest_lists_the_gap_controls_as_json_for_your_own_agent()
    {
        // s11-06: hound has no model; it hands the person's agent the facts, here from a snapshot without contacting CISO Assistant.
        await WriteScopeAsync(Ssp);
        Instance();
        var snapshot = Path.Combine(_workDir, "ssp-0.1.json");
        (await Build("--snapshot-out", snapshot)).ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.GetStringBuilder().Clear();
        _ca.Reset();

        var exit = await HoundCli.RunAsync(
            ["ssp", "suggest", "--programme", "baseline", "--scope", ScopeDir, "--from-snapshot", snapshot], _output, _error, logging => logging.AddProvider(_logs), new Dictionary<string, string?>(), _ct);

        exit.ShouldBe(ExitCodes.Ok, _error.ToString());
        var brief = JsonNode.Parse(_output.ToString())!;
        brief["programme"]!.GetValue<string>().ShouldBe("baseline");
        brief["controls"]!.AsArray().ShouldBeEmpty();
        _ca.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Describe_writes_to_controls_yaml_not_ciso_assistant()
    {
        await WriteScopeAsync(Ssp);

        var exit = await Describe("AC-MFA", "Every user signs in to Example 365 with MFA.");

        exit.ShouldBe(ExitCodes.Ok, _error.ToString());
        var controls = await File.ReadAllTextAsync(Path.Combine(ScopeDir, "controls.yaml"), _ct);
        var lines = controls.Split('\n');
        var at = Array.FindIndex(lines, l => l.TrimStart() == "description: Every user signs in to Example 365 with MFA.");
        at.ShouldBeGreaterThan(0, controls);
        lines[at - 1].ShouldBe(lines[at][..(lines[at].Length - lines[at].TrimStart().Length)] + "# drafted with AI: review before merging", controls);
        lines[at - 1].Length.ShouldBeGreaterThan(lines[at - 1].TrimStart().Length, controls);
        _ca.LogEntries.ShouldBeEmpty();
        (await HoundCli.RunAsync(["scope", "validate", ScopeDir], _output, _error, logging => logging.AddProvider(_logs), new Dictionary<string, string?>(), _ct)).ShouldBe(ExitCodes.Ok, _error.ToString());
    }

    [Fact]
    public async Task Describe_refuses_a_control_the_scope_does_not_declare()
    {
        await WriteScopeAsync(Ssp);

        (await Describe("AC-UNKNOWN", "Anything.")).ShouldBe(ExitCodes.Configuration);

        ErrorCodes().ShouldContain("ssp.control_not_in_scope");
    }

    [Fact]
    public async Task Diff_lists_what_changed_between_two_snapshots()
    {
        await WriteScopeAsync(Ssp);
        Instance();
        var before = Path.Combine(_workDir, "ssp-0.1.json");
        var after = Path.Combine(_workDir, "ssp-0.2.json");
        (await Build("--snapshot-out", before)).ShouldBe(ExitCodes.Ok, _error.ToString());
        await File.WriteAllTextAsync(after, (await File.ReadAllTextAsync(before, _ct)).Replace("\"Example baseline\"", "\"Example baseline and warehouse\"", StringComparison.Ordinal), _ct);
        _output.GetStringBuilder().Clear();

        var exit = await HoundCli.RunAsync(["ssp", "diff", before, after], _output, _error, logging => logging.AddProvider(_logs), new Dictionary<string, string?>(), _ct);

        exit.ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldContain("| boundary | perimeter | Example baseline | Example baseline and warehouse |");
    }

    [Fact]
    public async Task Publish_uploads_the_signed_plan_with_the_audit_report_as_a_new_revision()
    {
        await WriteScopeAsync(Ssp);
        Instance();
        var snapshot = Path.Combine(_workDir, "ssp-0.1.json");
        (await Build("--snapshot-out", snapshot)).ShouldBe(ExitCodes.Ok, _error.ToString());
        await WriteScopeAsync(Ssp + SignOff(SspVersion.Hash(_output.ToString())));
        _ca.Reset();
        List("compliance-assessments", new JsonObject { ["id"] = "ca-1", ["ref_id"] = "EXAMPLE-BASELINE-2026" });
        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/ca-1/word_report/").UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithBody([1, 2, 3]));
        List("evidences");
        _ca.Given(Request.Create().WithPath("/api/evidences/").UsingPost()).RespondWith(Response.Create().WithStatusCode(201).WithBody("""{"id": "ev-ssp"}"""));
        _ca.Given(Request.Create().WithPath("/api/evidence-revisions/").UsingPost()).RespondWith(Response.Create().WithStatusCode(201).WithBody("""{"id": "rev-1"}"""));

        var exit = await Publish(snapshot);

        exit.ShouldBe(ExitCodes.Ok, _error.ToString());
        var upload = _ca.LogEntries.Select(e => e.RequestMessage!).Single(r => r.Path == "/api/evidence-revisions/" && r.Method == "POST");
        System.Text.Encoding.Latin1.GetString(upload.BodyAsBytes!).ShouldContain("filename=ssp-baseline-0.1.zip");
        _ca.LogEntries.Select(e => e.RequestMessage!).Single(r => r.Path == "/api/evidences/" && r.Method == "POST").Body!.ShouldContain("SSP-BASELINE");
    }

    [Fact]
    public async Task Publish_refuses_an_unsigned_plan()
    {
        await WriteScopeAsync(Ssp);
        Instance();
        var snapshot = Path.Combine(_workDir, "ssp-0.1.json");
        (await Build("--snapshot-out", snapshot)).ShouldBe(ExitCodes.Ok, _error.ToString());
        _ca.Reset();

        (await Publish(snapshot)).ShouldBe(ExitCodes.Configuration);

        ErrorCodes().ShouldContain("ssp.not_signed");
        _ca.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_scope_problem_stops_before_ciso_assistant_is_read()
    {
        await WriteScopeAsync(Ssp.Replace("perimeter: Example baseline", "perimeter: Nowhere", StringComparison.Ordinal));

        var exit = await Build();

        exit.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("Nowhere");
        _ca.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_programme_is_refused()
    {
        await WriteScopeAsync(Ssp);

        var exit = await BuildFor("no-such-programme");

        exit.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("no-such-programme");
        _ca.LogEntries.ShouldBeEmpty();
    }

    public void Dispose()
    {
        _ca.Stop();
        _ca.Dispose();
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }

    private Task<int> Build(params string[] args) => BuildFor("baseline", args);

    private Task<int> Publish(string snapshot) =>
        HoundCli.RunAsync(
            ["ssp", "publish", "--programme", "baseline", "--scope", ScopeDir, "--snapshot", snapshot],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token },
            _ct);

    private Task<int> Describe(string control, string text) =>
        HoundCli.RunAsync(["ssp", "describe", "--control", control, "--text", text, "--scope", ScopeDir], _output, _error, logging => logging.AddProvider(_logs), new Dictionary<string, string?>(), _ct);

    private string[] ErrorCodes() =>
        [.. _logs.Entries.Where(e => e.Level == LogLevel.Error).SelectMany(e => e.State).Where(kv => kv.Key == "Code").Select(kv => (string)kv.Value!)];

    private static string SignOff(string sha256) =>
        $"\n    sign_offs:\n      - {{version: \"0.1\", by: it@example.com, date: \"2026-11-24\", sha256: {sha256}}}\n";

    private Task<int> BuildFor(string programme, params string[] args) =>
        HoundCli.RunAsync(
            ["ssp", "build", "--programme", programme, "--scope", ScopeDir, .. args],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token },
            _ct);

    /// <summary>A CISO Assistant holding the example domain and its one system, owned by the IT user.</summary>
    private void Instance()
    {
        List("folders",
            new JsonObject { ["id"] = "f-global", ["name"] = "Global", ["parent_folder"] = null },
            new JsonObject { ["id"] = "f-ex", ["name"] = "Example Pty Ltd", ["parent_folder"] = new JsonObject { ["id"] = "f-global", ["str"] = "Global" } });
        Object("folders", "f-ex", new JsonObject { ["id"] = "f-ex", ["name"] = "Example Pty Ltd", ["parent_folder"] = "f-global" });
        List("users", new JsonObject { ["id"] = "u-it", ["email"] = "it@example.com" });
        List("actors", new JsonObject { ["id"] = "act-it", ["str"] = "it@example.com", ["type"] = "user", ["specific"] = new JsonObject { ["id"] = "u-it", ["str"] = "it@example.com" } });
        List("assets", new JsonObject { ["id"] = "a-365", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "folder" } });
        Object("assets", "a-365", new JsonObject { ["id"] = "a-365", ["name"] = "Example 365", ["type"] = "PR", ["folder"] = "f-ex", ["parent_assets"] = new JsonArray(), ["owner"] = new JsonArray("act-it") });
        foreach (var resource in new[] { "frameworks", "loaded-libraries", "compliance-assessments", "evidences", "applied-controls", "requirement-assessments", "teams", "roles", "role-assignments", "user-groups", "perimeters", "entities", "solutions", "contracts", "representatives", "entity-assessments", "tiers" })
        {
            List(resource);
        }
    }

    private void List(string resource, params JsonObject[] items) =>
        _ca.Given(Request.Create().WithPath($"/api/{resource}/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = items.Length, ["results"] = new JsonArray(items) }.ToJsonString()));

    private void Object(string resource, string id, JsonObject body) =>
        _ca.Given(Request.Create().WithPath($"/api/{resource}/{id}/object/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body.ToJsonString()));

    private async Task WriteScopeAsync(string ssp)
    {
        Directory.CreateDirectory(Path.Combine(ScopeDir, "audits"));
        Directory.CreateDirectory(Path.Combine(_workDir, "frameworks"));
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "setup.yaml"), ScopeCommandTests.Setup + """

            programmes:
              - id: baseline
                name: Example baseline
                framework: urn:example:risk:framework:example-baseline
                groups: ["1"]
                audit: EXAMPLE-BASELINE-2026

            """, _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "controls.yaml"), ScopeCommandTests.Controls, _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "audits", "example-baseline-2026.yaml"), ScopeCommandTests.Audit, _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "access.yaml"), "users:\n  - email: it@example.com\nroles:\n  - role: approver\n    domain: Example Pty Ltd\n    users: [it@example.com]\n", _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "ssp.yaml"), ssp, _ct);
        await File.WriteAllTextAsync(Path.Combine(_workDir, "frameworks", "example-baseline.json"), FrameworkSnapshotJson.Write(new FrameworkSnapshot(
            "urn:example:risk:library:example-baseline",
            "urn:example:risk:framework:example-baseline",
            1,
            [new ImplementationGroup("1", "Level one")],
            [new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-1.1", "EX-1.1", null, ["1"], "Every account uses multi-factor authentication")])), _ct);
    }
}
