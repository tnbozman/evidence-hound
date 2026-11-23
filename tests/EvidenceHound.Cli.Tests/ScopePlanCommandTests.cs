using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary><c>hound scope plan</c> reads CISO Assistant and prints what applying the scope would change; <c>hound scope apply</c> writes it.</summary>
public sealed class ScopePlanCommandTests : IDisposable
{
    private static readonly string Token = "example-token-for-cli-tests";
    private static readonly FrameworkSnapshot Snapshot = new(
        "urn:example:risk:library:example-baseline",
        "urn:example:risk:framework:example-baseline",
        1,
        [new ImplementationGroup("1", "Level one")],
        [new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-1.1", "EX-1.1", null, ["1"], "Every account uses multi-factor authentication")]);

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly WireMockServer _ca = WireMockServer.Start();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-scope-plan-tests-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private string ScopeDir => Path.Combine(_workDir, "scope");

    [Fact]
    public async Task Plan_against_an_empty_instance_lists_every_create_as_markdown_and_sends_only_gets()
    {
        await WriteScopeAsync();
        EmptyInstance();

        var exitCode = await Plan();

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var markdown = _output.ToString();
        markdown.ShouldStartWith("## Scope plan\n");
        markdown.ShouldContain("7 to create, 0 to update, 0 to delete. No drift.");
        markdown.ShouldContain("| Create | framework | urn:example:risk:framework:example-baseline |");
        markdown.ShouldContain("| Create | domain | Example Pty Ltd |");
        markdown.ShouldContain("| Create | perimeter | Example Pty Ltd/Example baseline | name: Example baseline<br>domain: Example Pty Ltd |");
        markdown.ShouldContain("| Create | system | Example 365 |");
        markdown.ShouldContain("| Create | audit | EXAMPLE-BASELINE-2026 |");
        markdown.ShouldContain("| Create | control | AC-MFA |");
        markdown.IndexOf("| domain |", StringComparison.Ordinal).ShouldBeLessThan(markdown.IndexOf("| control |", StringComparison.Ordinal));
        _ca.LogEntries.ShouldNotBeEmpty();
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Plan_with_access_yaml_sets_users_and_their_roles_and_reviews_mfa_and_service_accounts()
    {
        await WriteScopeAsync();
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "access.yaml"), """
            users:
              - email: it@example.com
                first_name: Alex
                last_name: Example
              - email: hound-runner@example.com
                service_account: publish
            roles:
              - role: administrator
                domain: Global
                users: [it@example.com]
              - role: analyst
                domain: Example Pty Ltd
                users: [hound-runner@example.com]
            """, _ct);
        EmptyInstance();
        List("users",
            new JsonObject { ["id"] = "u-it", ["email"] = "it@example.com", ["is_active"] = true, ["has_mfa_enabled"] = false, ["user_groups"] = new JsonArray() },
            new JsonObject { ["id"] = "u-run", ["email"] = "hound-runner@example.com", ["is_active"] = true, ["has_mfa_enabled"] = false, ["user_groups"] = new JsonArray(new JsonObject { ["id"] = "g-adm", ["str"] = "Global - Administrator" }) });
        List("user-groups", new JsonObject { ["id"] = "g-adm", ["name"] = "Global - Administrator", ["folder"] = new JsonObject { ["id"] = "f-global", ["str"] = "Global" }, ["builtin"] = true });
        List("roles", new JsonObject { ["id"] = "r-adm", ["name"] = "Administrator", ["builtin"] = true });
        List("role-assignments", new JsonObject
        {
            ["id"] = "ra-adm",
            ["name"] = "BI-RA-ADM",
            ["role"] = new JsonObject { ["id"] = "r-adm", ["str"] = "Administrator" },
            ["user"] = null,
            ["user_group"] = new JsonObject { ["id"] = "g-adm", ["str"] = "BI-UG-ADM" },
            ["perimeter_folders"] = new JsonArray(new JsonObject { ["id"] = "f-global", ["str"] = "Global" }),
            ["builtin"] = true,
        });

        var exitCode = await Plan();

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var markdown = _output.ToString();
        // A role is membership of the domain's group for it, so it shows as the user's roles; the runner leaves the administrators group.
        markdown.ShouldContain("| Update | user | it@example.com |");
        markdown.ShouldContain("| Update | user | hound-runner@example.com |");
        markdown.ShouldContain("roles: BI-RL-ADM on Global → BI-RL-ANA on Example Pty Ltd");
        markdown.ShouldContain("roles: (none) → BI-RL-ADM on Global");
        markdown.ShouldNotContain("role_assignment");
        markdown.ShouldContain("### Access review");
        markdown.ShouldContain("| it@example.com | has not turned on multi-factor authentication |");
        markdown.ShouldContain("| hound-runner@example.com | holds BI-RL-ADM on Global; a publishing service account needs only analyst on the domains it publishes to |");
    }

    [Fact]
    public async Task Plan_with_settings_yaml_changes_only_named_settings_and_lists_the_others_when_asked()
    {
        await WriteScopeAsync();
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "settings.yaml"), "feature_flags:\n  tprm: false\ngeneral:\n  enforce_mfa: true\n", _ct);
        EmptyInstance();
        _ca.Given(Request.Create().WithPath("/api/settings/feature-flags/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["name"] = "feature-flags", ["tprm"] = true, ["bia"] = false }.ToJsonString()));
        _ca.Given(Request.Create().WithPath("/api/settings/general/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["name"] = "general", ["value"] = new JsonObject { ["enforce_mfa"] = false, ["currency"] = "€" } }.ToJsonString()));

        var exitCode = await Plan("--show-unmanaged");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var markdown = _output.ToString();
        markdown.ShouldContain("| Update | feature_flag | tprm | value: true → false |");
        markdown.ShouldContain("| Update | setting | enforce_mfa | value: false → true |");
        markdown.ShouldContain("### Unmanaged settings");
        markdown.ShouldContain("| feature_flag | bia | false |");
        markdown.ShouldContain("| setting | currency | \"€\" |");
        markdown.ShouldNotContain("| feature_flag | tprm | true |");
    }

    [Fact]
    public async Task Plan_as_json_lists_changes_field_by_field_and_drift()
    {
        await WriteScopeAsync();
        EmptyInstance(perimeters: [new JsonObject { ["id"] = "p-old", ["folder"] = new JsonObject { ["id"] = "f-global", ["str"] = "Global" } }]);
        Object("perimeters", "p-old", new JsonObject { ["id"] = "p-old", ["name"] = "Old perimeter", ["ref_id"] = "PER-OLD", ["folder"] = "f-global" });

        var exitCode = await Plan("--format", "json");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var plan = JsonNode.Parse(_output.ToString())!;
        var system = plan["changes"]!.AsArray().Single(c => c!["kind"]!.GetValue<string>() == "system")!;
        (system["action"]!.GetValue<string>(), system["key"]!.GetValue<string>()).ShouldBe(("create", "Example 365"));
        system["fields"]!.AsArray().Select(f => (f!["field"]!.GetValue<string>(), (string?)f["old"], f["new"]!.GetValue<string>()))
            .ShouldContain(("type", (string?)null, "primary"));
        var drift = plan["drift"]!.AsArray().ShouldHaveSingleItem()!;
        (drift["kind"]!.GetValue<string>(), drift["key"]!.GetValue<string>(), drift["reason"]!.GetValue<string>(), drift["managed"]!.GetValue<bool>())
            .ShouldBe(("perimeter", "Global/Old perimeter", "not_in_scope", false));
    }

    [Fact]
    public async Task Plan_on_the_day_given_opens_the_periods_of_an_audit_with_a_cycle_and_lists_them()
    {
        await WriteScopeAsync(ScopeCommandTests.Audit.Replace("EXAMPLE-BASELINE-2026", "EXAMPLE-BASELINE", StringComparison.Ordinal) + "\ncycle: {period: quarter, starts: \"2026-07-01\"}\n");
        EmptyInstance();
        var settings = new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token, ["HOUND_TODAY"] = "2026-11-21" };

        var exitCode = await Plan(settings);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var markdown = _output.ToString();
        markdown.ShouldContain("| Create | audit | EXAMPLE-BASELINE-2026Q3 |");
        markdown.ShouldContain("### Audit periods");
        markdown.ShouldContain("| EXAMPLE-BASELINE-2026Q3 | closes: in review, awaiting sign-off |");
        markdown.ShouldContain("| EXAMPLE-BASELINE-2026Q4 | opens, from EXAMPLE-BASELINE-2026Q3 |");
        _output.GetStringBuilder().Clear();

        (await Plan(settings, "--format", "json")).ShouldBe(ExitCodes.Ok, _error.ToString());
        JsonNode.Parse(_output.ToString())!["periods"]!.AsArray().Select(p => (p!["audit"]!.GetValue<string>(), p["event"]!.GetValue<string>(), (string?)p["baseline"]))
            .ShouldBe([("EXAMPLE-BASELINE-2026Q3", "closes", (string?)null), ("EXAMPLE-BASELINE-2026Q4", "opens", "EXAMPLE-BASELINE-2026Q3")]);
    }

    [Fact]
    public async Task Plan_refuses_an_invalid_scope_without_reading_ciso_assistant()
    {
        await WriteScopeAsync(ScopeCommandTests.Audit.Replace("systems: [Example 365]", "systems: [Example tablets]", StringComparison.Ordinal));

        var exitCode = await Plan();

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("scope.reference_unknown", StringComparison.Ordinal));
        _ca.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Plan_needs_the_ciso_assistant_token()
    {
        await WriteScopeAsync();

        var exitCode = await Plan(settings: new() { ["HOUND_CA_URL"] = $"{_ca.Url}/api/" });

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("HOUND_CA_TOKEN", StringComparison.Ordinal));
        _ca.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Apply_dry_run_prints_the_plan_and_writes_nothing()
    {
        await WriteScopeAsync();
        EmptyInstance();

        var exitCode = await Run("apply", "--dry-run");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldStartWith("## Scope plan\n");
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Apply_writes_audits_controls_and_rows_after_the_structure_they_need()
    {
        await WriteScopeAsync();
        HeldStructure();
        Accept("POST", "/api/compliance-assessments/");
        Accept("POST", "/api/applied-controls/");
        Accept("PATCH", "/api/requirement-assessments/ra-1/");

        var exitCode = await Run("apply");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var report = JsonNode.Parse(_output.ToString())!;
        (report["written"]!.GetValue<int>(), report["failed"]!.GetValue<int>(), report["skipped"]!.GetValue<int>(), report["not_applied"]!.GetValue<int>()).ShouldBe((3, 0, 0, 0));
        report["changes"]!.AsArray().Select(c => (c!["action"]!.GetValue<string>(), c["kind"]!.GetValue<string>(), c["key"]!.GetValue<string>())).ShouldBe(
            [("create", "audit", "EXAMPLE-BASELINE-2026"), ("create", "control", "AC-MFA"), ("create", "assessment", "EXAMPLE-BASELINE-2026/EX-1.1")]);
        report["drift"]!.AsArray().ShouldBeEmpty();
        report["compliance"]!.AsArray().ShouldNotBeNull();
        _ca.LogEntries.Where(e => e.RequestMessage!.Method != "GET").Select(e => (e.RequestMessage!.Method, e.RequestMessage.Path)).ShouldBe(
            [("POST", "/api/compliance-assessments/"), ("POST", "/api/applied-controls/"), ("PATCH", "/api/requirement-assessments/ra-1/")]);
    }

    [Fact]
    public async Task Export_writes_scope_files_that_validate_and_plan_to_no_changes()
    {
        HeldStructure();
        var exported = Path.Combine(_workDir, "exported", "scope");

        var exitCode = await Export("--out", exported);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldContain("1 domain(s), 0 control(s), 0 audit(s)");
        var setup = await File.ReadAllTextAsync(Path.Combine(exported, "setup.yaml"), _ct);
        setup.ShouldContain("  - name: Example Pty Ltd\n");
        setup.ShouldContain("      - name: Example baseline\n");
        setup.ShouldContain("      - name: Example 365\n");
        _output.GetStringBuilder().Clear();

        (await HoundCli.RunAsync(["scope", "validate", exported], _output, _error, logging => logging.AddProvider(_logs), new Dictionary<string, string?>(), _ct)).ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.GetStringBuilder().Clear();
        (await HoundCli.RunAsync(
            ["scope", "plan", exported, "--format", "json"], _output, _error, logging => logging.AddProvider(_logs),
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token }, _ct)).ShouldBe(ExitCodes.Ok, _error.ToString());
        JsonNode.Parse(_output.ToString())!["changes"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Export_with_files_downloads_each_document_into_the_compliance_repo()
    {
        HeldStructure();
        HeldAuditWithDocument();
        var exported = Path.Combine(_workDir, "exported", "scope");

        var exitCode = await Export("--out", exported, "--with-files");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var downloaded = Path.Combine(_workDir, "exported", "documents", "identity-policy", "identity-policy.pdf");
        (await File.ReadAllTextAsync(downloaded, _ct)).ShouldBe("example policy");
        var audit = await File.ReadAllTextAsync(Path.Combine(exported, "audits", "ex-audit.yaml"), _ct);
        audit.ShouldContain("    files: [documents/identity-policy/identity-policy.pdf]\n");
        File.Exists(Path.Combine(exported, "frameworks", "example-baseline.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task Apply_with_a_failed_write_reports_it_and_exits_with_publish_failed()
    {
        await WriteScopeAsync();
        EmptyInstance();
        Accept("POST", "/api/stored-libraries/urn:example:risk:library:example-baseline/import/");
        _ca.Given(Request.Create().WithPath("/api/folders/").UsingPost()).RespondWith(Response.Create().WithStatusCode(400).WithBody("""{"name": ["This field is required."]}"""));

        var exitCode = await Run("apply");

        exitCode.ShouldBe(ExitCodes.PublishFailed);
        var report = JsonNode.Parse(_output.ToString())!;
        (report["written"]!.GetValue<int>(), report["failed"]!.GetValue<int>(), report["skipped"]!.GetValue<int>()).ShouldBe((1, 1, 5));
    }

    [Fact]
    public async Task Markdown_has_changes_to_compliance_section()
    {
        await WriteScopeAsync(HandWritten("compliant"));
        HeldStructure();
        HeldAuditWithRow("not_assessed");

        var exitCode = await Plan();

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var markdown = _output.ToString();
        markdown.ShouldContain("""

            ### Changes to compliance

            **EXAMPLE-BASELINE-2026**: 1 result changes, no regressions.

            - Level one: 0 → 1 compliant of 1

            |  | Requirement | Title | Strategy | Result | Why |
            | --- | --- | --- | --- | --- | --- |
            |  | EX-1.1 | Every account uses multi-factor authentication | (none) | not_assessed → compliant | hand-written in the audit file |

            """.ReplaceLineEndings("\n"));
        markdown.IndexOf("| Change | Kind |", StringComparison.Ordinal).ShouldBeLessThan(markdown.IndexOf("### Changes to compliance", StringComparison.Ordinal));
        if (markdown.Contains("### Drift", StringComparison.Ordinal))
        {
            markdown.IndexOf("### Changes to compliance", StringComparison.Ordinal).ShouldBeLessThan(markdown.IndexOf("### Drift", StringComparison.Ordinal));
        }

        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Markdown_has_changes_to_compliance_section_json_has_compliance()
    {
        await WriteScopeAsync(HandWritten("non_compliant"));
        HeldStructure();
        HeldAuditWithRow("compliant");

        var exitCode = await Plan("--format", "json");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var compliance = JsonNode.Parse(_output.ToString())!["compliance"]!.AsArray().ShouldHaveSingleItem()!;
        compliance["audit"]!.GetValue<string>().ShouldBe("EXAMPLE-BASELINE-2026");
        var group = compliance["groups"]!.AsArray().ShouldHaveSingleItem()!;
        (group["group"]!.GetValue<string>(), group["before"]!.GetValue<int>(), group["after"]!.GetValue<int>(), group["total"]!.GetValue<int>()).ShouldBe(("1", 1, 0, 1));
        var row = compliance["rows"]!.AsArray().ShouldHaveSingleItem()!;
        (row["requirement"]!.GetValue<string>(), row["old"]!.GetValue<string>(), row["new"]!.GetValue<string>(), row["regresses"]!.GetValue<bool>())
            .ShouldBe(("EX-1.1", "compliant", "non_compliant", true));
        row["title"]!.GetValue<string>().ShouldBe("Every account uses multi-factor authentication");
        row["why"]!.GetValue<string>().ShouldBe("hand-written in the audit file");
        row["parent"].ShouldBeNull();
    }

    [Fact]
    public async Task Plan_with_no_result_changes_has_no_compliance_section()
    {
        await WriteScopeAsync(HandWritten("compliant"));
        HeldStructure();
        HeldAuditWithRow("compliant");

        (await Plan()).ShouldBe(ExitCodes.Ok, _error.ToString());

        _output.ToString().ShouldNotContain("Changes to compliance");
    }

    [Fact]
    public async Task Detailed_exitcode_is_0_when_empty_and_2_with_changes()
    {
        await WriteScopeAsync();
        EmptyInstance();
        (await Plan("--detailed-exitcode")).ShouldBe(2, _error.ToString());

        HeldStructure();
        var exported = Path.Combine(_workDir, "exported", "scope");
        (await Export("--out", exported)).ShouldBe(ExitCodes.Ok, _error.ToString());
        (await HoundCli.RunAsync(
            ["scope", "plan", exported, "--detailed-exitcode"], _output, _error, logging => logging.AddProvider(_logs),
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token }, _ct)).ShouldBe(0, _output.ToString() + _error);

        // Any error is 1 with the flag, so that 2 always means changes: a usage error, and a missing token.
        (await Plan("--detailed-exitcode", "--format", "yaml")).ShouldBe(1);
        (await Plan(new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/" }, "--detailed-exitcode")).ShouldBe(1);
        (await Plan(new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/" })).ShouldBe(ExitCodes.Configuration);
    }

    [Fact]
    public async Task Saved_plan_holds_no_token()
    {
        await WriteScopeAsync();
        EmptyInstance();
        var saved = Path.Combine(_workDir, "plan.json");

        (await Plan("--out", saved)).ShouldBe(ExitCodes.Ok, _error.ToString());

        var text = await File.ReadAllTextAsync(saved, _ct);
        text.ShouldNotContain(Token);
        var json = JsonNode.Parse(text)!;
        json["ciso_assistant_url"]!.GetValue<string>().ShouldBe($"{_ca.Url}/api/");
        json["inputs_sha256"]!.GetValue<string>().Length.ShouldBe(64);
        json["plan"]!["changes"]!.AsArray().Count.ShouldBe(7);
        _output.ToString().ShouldStartWith("## Scope plan\n");
    }

    [Fact]
    public async Task Apply_saved_plan_without_replanning()
    {
        await WriteScopeAsync();
        HeldStructure();
        Accept("POST", "/api/compliance-assessments/");
        Accept("POST", "/api/applied-controls/");
        Accept("PATCH", "/api/requirement-assessments/ra-1/");
        var saved = Path.Combine(_workDir, "plan.json");
        (await Plan("--out", saved)).ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.GetStringBuilder().Clear();
        _ca.ResetLogEntries();

        var exitCode = await ApplySaved(saved);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var report = JsonNode.Parse(_output.ToString())!;
        report["written"]!.GetValue<int>().ShouldBe(3);
        _ca.LogEntries.Where(e => e.RequestMessage!.Method != "GET").Select(e => (e.RequestMessage!.Method, e.RequestMessage.Path)).ShouldBe(
            [("POST", "/api/compliance-assessments/"), ("POST", "/api/applied-controls/"), ("PATCH", "/api/requirement-assessments/ra-1/")]);
    }

    [Fact]
    public async Task Apply_refuses_a_stale_plan_when_scope_or_instance_changed()
    {
        await WriteScopeAsync();
        HeldStructure();
        var saved = Path.Combine(_workDir, "plan.json");
        (await Plan("--out", saved)).ShouldBe(ExitCodes.Ok, _error.ToString());

        // Another CISO Assistant.
        (await ApplySaved(saved, url: "http://ciso.example.com/api/")).ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("stale");

        // The scope files changed since the plan.
        var controls = Path.Combine(ScopeDir, "controls.yaml");
        await File.AppendAllTextAsync(controls, "    owner: it@example.com\n", _ct);
        (await ApplySaved(saved)).ShouldBe(ExitCodes.Configuration);
        await File.WriteAllTextAsync(controls, ScopeCommandTests.Controls, _ct);

        // Someone made the audit in CISO Assistant after the plan, which planned to create it.
        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/").UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject
            {
                ["count"] = 1,
                ["results"] = new JsonArray(new JsonObject { ["id"] = "ca-new", ["ref_id"] = "EXAMPLE-BASELINE-2026", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "f-ex" } }),
            }.ToJsonString()));
        Object("compliance-assessments", "ca-new", new JsonObject { ["id"] = "ca-new", ["ref_id"] = "EXAMPLE-BASELINE-2026", ["name"] = "Made by hand", ["folder"] = "f-ex" });
        _error.GetStringBuilder().Clear();
        (await ApplySaved(saved)).ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("scope.plan_stale");
        _error.ToString().ShouldContain("EXAMPLE-BASELINE-2026");

        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Apply_refuses_an_invalid_scope_without_reading_ciso_assistant()
    {
        await WriteScopeAsync(ScopeCommandTests.Audit.Replace("systems: [Example 365]", "systems: [Example tablets]", StringComparison.Ordinal));

        var exitCode = await Run("apply");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _ca.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Plan_refuses_a_system_whose_supplier_has_no_concluded_assessment_and_writes_nothing()
    {
        await WriteScopeAsync();
        await WriteSupplierAsync();
        EmptyInstance();
        SupplierRecords();

        var exitCode = await Plan();

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("system.supplier_not_approved", StringComparison.Ordinal) && m.Contains("SUP-EX", StringComparison.Ordinal) && m.Contains("no concluded assessment", StringComparison.Ordinal));
        _output.ToString().ShouldNotContain("## Scope plan");
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Apply_refuses_a_system_whose_supplier_is_not_approved_and_writes_nothing()
    {
        await WriteScopeAsync();
        await WriteSupplierAsync();
        EmptyInstance();
        SupplierRecords();

        var exitCode = await Run("apply");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("system.supplier_not_approved", StringComparison.Ordinal));
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Plan_lists_a_lapsed_supplier_approval_and_a_tier_that_differs_from_the_forced_one()
    {
        await WriteScopeAsync();
        await WriteSupplierAsync();
        HeldStructure();
        SupplierRecords(new JsonObject
        {
            ["id"] = "ea-1",
            ["ref_id"] = "EA-EX-2025-06",
            ["name"] = "Example Cloud review 2025",
            ["entity"] = new JsonObject { ["id"] = "e-1", ["str"] = "Example Cloud Pty Ltd" },
            ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" },
            ["due_date"] = "2025-06-30",
            ["conclusion"] = "ok",
            ["expiry_date"] = "2026-06-30",
        });
        var settings = new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token, ["HOUND_TODAY"] = "2026-11-21" };

        var exitCode = await Plan(settings);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var markdown = _output.ToString();
        markdown.ShouldContain("### Suppliers");
        markdown.ShouldContain("| SUP-EX | Example 365 | SUP-EX is no longer approved: EA-EX-2025-06's conclusion expired on 2026-06-30.");
        markdown.ShouldContain("[supplier.approval_lapsed]");
        markdown.ShouldContain("| SUP-EX |  | CISO Assistant holds SUP-EX at tier critical; the scope forces important: Hosts the customer portal. [supplier.tier_differs] |");
        _output.GetStringBuilder().Clear();

        (await Plan(settings, "--format", "json")).ShouldBe(ExitCodes.Ok, _error.ToString());
        JsonNode.Parse(_output.ToString())!["suppliers"]!.AsArray().Select(n => (n!["code"]!.GetValue<string>(), n["supplier"]!.GetValue<string>(), (string?)n["system"]))
            .ShouldBe([("supplier.approval_lapsed", "SUP-EX", (string?)"Example 365"), ("supplier.tier_differs", "SUP-EX", (string?)null)]);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        _ca.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }

    private Task<int> Plan(params string[] args) => Plan(null, args);

    private Task<int> Export(params string[] args) =>
        HoundCli.RunAsync(
            ["scope", "export", .. args],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token },
            _ct);

    /// <summary>An audit in the example domain whose row links a document with one file, and the audit's framework with its one node.</summary>
    private void HeldAuditWithDocument()
    {
        static JsonObject In(string id, string folder, string? refId = null) =>
            new() { ["id"] = id, ["ref_id"] = refId, ["folder"] = new JsonObject { ["id"] = folder, ["str"] = folder } };

        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/").UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = 1, ["results"] = new JsonArray(In("ca-ex", "f-ex", "EX-AUDIT")) }.ToJsonString()));
        Object("compliance-assessments", "ca-ex", new JsonObject
        {
            ["id"] = "ca-ex",
            ["ref_id"] = "EX-AUDIT",
            ["name"] = "Example audit",
            ["framework"] = "fw-1",
            ["perimeter"] = "p-1",
            ["folder"] = "f-ex",
            ["selected_implementation_groups"] = new JsonArray("1"),
        });
        _ca.Given(Request.Create().WithPath("/api/evidences/").UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = 1, ["results"] = new JsonArray(In("ev-1", "f-ex", "identity-policy")) }.ToJsonString()));
        Object("evidences", "ev-1", new JsonObject { ["id"] = "ev-1", ["ref_id"] = "identity-policy", ["name"] = "Identity policy", ["folder"] = "f-ex" });
        List("evidence-revisions", new JsonObject { ["id"] = "rev-1", ["attachment_hash"] = new string('a', 64) });
        _ca.Given(Request.Create().WithPath("/api/evidences/ev-1/attachment/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Disposition", "attachment; filename=\"identity-policy.pdf\"").WithBody("example policy"));
        _ca.Given(Request.Create().WithPath("/api/requirement-assessments/").UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject
            {
                ["count"] = 1,
                ["results"] = new JsonArray(new JsonObject
                {
                    ["id"] = "ra-1",
                    ["status"] = "to_do",
                    ["result"] = "not_assessed",
                    ["evidences"] = new JsonArray("ev-1"),
                    ["requirement"] = new JsonObject { ["urn"] = Snapshot.Nodes[0].Urn, ["ref_id"] = "EX-1.1" },
                }),
            }.ToJsonString()));
        Object("frameworks", "fw-1", new JsonObject
        {
            ["id"] = "fw-1",
            ["urn"] = Snapshot.FrameworkUrn,
            ["library"] = "lib-1",
            ["implementation_groups_definition"] = new JsonArray(new JsonObject { ["ref_id"] = "1", ["name"] = "Level one" }),
        });
        List("requirement-nodes", new JsonObject
        {
            ["urn"] = Snapshot.Nodes[0].Urn,
            ["ref_id"] = "EX-1.1",
            ["name"] = "Every account uses multi-factor authentication",
            ["assessable"] = true,
            ["implementation_groups"] = new JsonArray("1"),
        });
    }

    /// <summary>The scope's audit with EX-1.1 written by hand: done, with <paramref name="result"/>.</summary>
    private static string HandWritten(string result) =>
        ScopeCommandTests.Audit.Replace("unlisted:", $"assessments:\n  - ref: EX-1.1\n    status: done\n    result: {result}\n    observation: Checked at the March review.\nunlisted:", StringComparison.Ordinal);

    /// <summary>The scope's audit held in the domain's folder, with its one row (EX-1.1) at <paramref name="result"/>.</summary>
    private void HeldAuditWithRow(string result)
    {
        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/").UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject
            {
                ["count"] = 1,
                ["results"] = new JsonArray(new JsonObject { ["id"] = "ca-1", ["ref_id"] = "EXAMPLE-BASELINE-2026", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "f-ex" } }),
            }.ToJsonString()));
        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/ca-1/object/").UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject
            {
                ["id"] = "ca-1",
                ["ref_id"] = "EXAMPLE-BASELINE-2026",
                ["name"] = "Example baseline 2026",
                ["framework"] = "fw-1",
                ["perimeter"] = "p-1",
                ["folder"] = "f-ex",
                ["selected_implementation_groups"] = new JsonArray("1"),
            }.ToJsonString()));
        _ca.Given(Request.Create().WithPath("/api/requirement-assessments/").UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject
            {
                ["count"] = 1,
                ["results"] = new JsonArray(new JsonObject
                {
                    ["id"] = "ra-1",
                    ["status"] = "done",
                    ["result"] = result,
                    ["observation"] = "Checked at the March review.",
                    ["requirement"] = new JsonObject { ["urn"] = Snapshot.Nodes[0].Urn, ["ref_id"] = "EX-1.1" },
                }),
            }.ToJsonString()));
    }

    private Task<int> ApplySaved(string saved, string? url = null) =>
        HoundCli.RunAsync(
            ["scope", "apply", saved],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = url ?? $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token },
            _ct);

    private Task<int> Run(string command, params string[] args) =>
        HoundCli.RunAsync(
            ["scope", command, ScopeDir, .. args],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token },
            _ct);

    private void Accept(string method, string path) =>
        _ca.Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(method == "POST" ? 201 : 200).WithBody(new JsonObject { ["id"] = "new-id" }.ToJsonString()));

    private Task<int> Plan(Dictionary<string, string?>? settings, params string[] args) =>
        HoundCli.RunAsync(
            ["scope", "plan", ScopeDir, .. args],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            settings ?? new() { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token },
            _ct);

    /// <summary>A CISO Assistant with only its Global folder: nothing the scope declares exists yet.</summary>
    private void EmptyInstance(JsonObject[]? perimeters = null, JsonObject[]? folders = null)
    {
        List("folders", [new JsonObject { ["id"] = "f-global", ["name"] = "Global", ["parent_folder"] = null }, .. folders ?? []]);
        foreach (var resource in new[] { "users", "actors", "frameworks", "loaded-libraries", "assets", "compliance-assessments", "evidences", "applied-controls", "requirement-assessments", "teams", "roles", "role-assignments", "user-groups" })
        {
            List(resource);
        }

        List("perimeters", perimeters ?? []);
    }

    /// <summary>
    /// A CISO Assistant holding the scope's framework, domain, perimeter and system as <c>hound</c> last wrote them, and, outside the scope's
    /// folders, objects the writer finds by ref_id: an audit with one row, and the control.
    /// </summary>
    private void HeldStructure()
    {
        static JsonObject In(string id, string folder, string? refId = null, string? name = null) =>
            new() { ["id"] = id, ["name"] = name, ["ref_id"] = refId, ["folder"] = new JsonObject { ["id"] = folder, ["str"] = folder } };
        static string Marked(params (string Name, string Value)[] fields) =>
            $"[hound scope {Core.Scope.ScopeMarker.Fingerprint(fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal))}]";

        List("folders", new JsonObject { ["id"] = "f-global", ["name"] = "Global", ["parent_folder"] = null }, new JsonObject { ["id"] = "f-ex", ["name"] = "Example Pty Ltd", ["parent_folder"] = new JsonObject { ["id"] = "f-global", ["str"] = "Global" } });
        Object("folders", "f-ex", new JsonObject { ["id"] = "f-ex", ["name"] = "Example Pty Ltd", ["parent_folder"] = "f-global", ["description"] = Marked() });
        List("frameworks", new JsonObject { ["id"] = "fw-1", ["urn"] = Snapshot.FrameworkUrn, ["library"] = new JsonObject { ["id"] = "lib-1", ["urn"] = Snapshot.LibraryUrn } });
        List("loaded-libraries", new JsonObject { ["id"] = "lib-1", ["urn"] = Snapshot.LibraryUrn, ["version"] = Snapshot.Version });
        List("perimeters", In("p-1", "f-ex", name: "Example baseline"));
        Object("perimeters", "p-1", new JsonObject { ["id"] = "p-1", ["name"] = "Example baseline", ["folder"] = "f-ex", ["description"] = Marked(("name", "Example baseline"), ("domain", "Example Pty Ltd")) });
        List("assets", In("a-1", "f-ex", name: "Example 365"));
        Object("assets", "a-1", new JsonObject
        {
            ["id"] = "a-1",
            ["name"] = "Example 365",
            ["type"] = "PR",
            ["folder"] = "f-ex",
            ["owner"] = new JsonArray(),
            ["parent_assets"] = new JsonArray(),
            ["description"] = Marked(("type", "primary"), ("domain", "Example Pty Ltd")),
        });
        List("compliance-assessments", In("ca-1", "f-elsewhere", "EXAMPLE-BASELINE-2026"));
        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/ca-1/object/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["id"] = "ca-1", ["selected_implementation_groups"] = new JsonArray("1") }.ToJsonString()));
        List("requirement-assessments", new JsonObject { ["id"] = "ra-1", ["requirement"] = new JsonObject { ["urn"] = Snapshot.Nodes[0].Urn, ["ref_id"] = "EX-1.1", ["implementation_groups"] = new JsonArray("1") } });
        List("applied-controls", In("ac-1", "f-elsewhere", "AC-MFA"));
        foreach (var resource in new[] { "users", "actors", "evidences" })
        {
            List(resource);
        }
    }

    /// <summary>The scope's system supplied through SUP-EX's hosting, which the organisation forces to the important tier.</summary>
    private async Task WriteSupplierAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "setup.yaml"), ScopeCommandTests.Setup.Replace("type: primary", "type: primary\n        solution: SOL-EX-HOST", StringComparison.Ordinal), _ct);
        Directory.CreateDirectory(Path.Combine(ScopeDir, "third-parties"));
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "third-parties", "sup-ex.yaml"), """
            ref_id: SUP-EX
            name: Example Cloud Pty Ltd
            domain: Example Pty Ltd
            dependency: 3
            penetration: 2
            maturity: 3
            trust: 3
            forced_tier:
              tier: important
              reason: Hosts the customer portal.
            solutions:
              - ref_id: SOL-EX-HOST
                name: Managed hosting
                criticality: 3
            contracts:
              - ref_id: CTR-EX-HOST
                name: Hosting agreement
                status: active
                solutions: [SOL-EX-HOST]
            """, _ct);
    }

    /// <summary>
    /// CISO Assistant's third-party records: with <paramref name="assessments"/>, SUP-EX held at the critical tier with its hosting and active
    /// contract and those assessments; without, no third parties at all.
    /// </summary>
    private void SupplierRecords(params JsonObject[] assessments)
    {
        static JsonObject Ref(string id, string text) => new() { ["id"] = id, ["str"] = text };
        List("tiers", new JsonObject { ["id"] = "t-1", ["key"] = "critical" }, new JsonObject { ["id"] = "t-2", ["key"] = "important" });
        List("representatives");
        List("entity-assessments", assessments);
        if (assessments.Length == 0)
        {
            List("entities");
            List("solutions");
            List("contracts");
            return;
        }

        List("entities", new JsonObject { ["id"] = "e-1", ["ref_id"] = "SUP-EX", ["name"] = "Example Cloud Pty Ltd", ["folder"] = Ref("f-ex", "Example Pty Ltd"), ["tier"] = Ref("t-1", "Critical"), ["builtin"] = false });
        List("solutions", new JsonObject { ["id"] = "s-1", ["ref_id"] = "SOL-EX-HOST", ["name"] = "Managed hosting", ["provider_entity"] = Ref("e-1", "Example Cloud Pty Ltd"), ["criticality"] = 3 });
        List("contracts", new JsonObject
        {
            ["id"] = "c-1",
            ["ref_id"] = "CTR-EX-HOST",
            ["name"] = "Hosting agreement",
            ["status"] = "active",
            ["folder"] = Ref("f-ex", "Example Pty Ltd"),
            ["provider_entity"] = Ref("e-1", "Example Cloud Pty Ltd"),
            ["solutions"] = new JsonArray(Ref("s-1", "Managed hosting")),
        });
    }

    private void List(string resource, params JsonObject[] items) =>
        _ca.Given(Request.Create().WithPath($"/api/{resource}/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = items.Length, ["results"] = new JsonArray(items) }.ToJsonString()));

    private void Object(string resource, string id, JsonObject body) =>
        _ca.Given(Request.Create().WithPath($"/api/{resource}/{id}/object/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body.ToJsonString()));

    private async Task WriteScopeAsync(string audit = ScopeCommandTests.Audit)
    {
        Directory.CreateDirectory(Path.Combine(ScopeDir, "audits"));
        Directory.CreateDirectory(Path.Combine(_workDir, "frameworks"));
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "setup.yaml"), ScopeCommandTests.Setup, _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "controls.yaml"), ScopeCommandTests.Controls, _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "audits", "example-baseline-2026.yaml"), audit, _ct);
        await File.WriteAllTextAsync(Path.Combine(_workDir, "frameworks", "example-baseline.json"), FrameworkSnapshotJson.Write(Snapshot), _ct);
    }
}
