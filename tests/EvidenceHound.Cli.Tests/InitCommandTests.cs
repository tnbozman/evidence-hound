namespace EvidenceHound.Cli.Tests;

/// <summary>s08-09: <c>hound init</c> scaffolds a new organisation's compliance repo that validates as written.</summary>
public sealed class InitCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-init-tests-").FullName;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private string Repo => Path.Combine(_root, "compliance");

    [Fact]
    public async Task Yes_writes_a_repo_that_validates()
    {
        var exitCode = await Run("", "init", Repo, "--yes");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        foreach (var file in new[] { "hound.yaml", "scope/setup.yaml", "scope/controls.yaml", "mappings/essential-eight-2023.yaml", "frameworks/essential-eight-2023.json", ".gitignore", "README.md", ".devcontainer/devcontainer.json", ".devcontainer/Dockerfile" })
        {
            File.Exists(Path.Combine(Repo, file)).ShouldBeTrue(file);
        }

        Directory.GetFiles(Path.Combine(Repo, "scope", "audits"), "*.yaml").ShouldBeEmpty("audits are drafted per system (s18-08)");
        await ShouldValidateAsync();
    }

    [Fact]
    public async Task Says_to_make_it_a_git_repository_and_names_the_command_that_lists_collector_types()
    {
        // s13-07 rehearsal: hound init writes no git repository, and the config comment named 'hound collectors', which needs a subcommand.
        (await Run("", "init", Repo, "--yes")).ShouldBe(ExitCodes.Ok, _error.ToString());

        _output.ToString().ShouldContain("git init");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "README.md"), TestContext.Current.CancellationToken)).ShouldContain("git init");
        var config = await File.ReadAllTextAsync(Path.Combine(Repo, "hound.yaml"), TestContext.Current.CancellationToken);
        config.ShouldContain("hound collectors list lists the types");
    }

    [Fact]
    public async Task Writes_the_weekly_review_task_for_the_compliance_owner_from_the_start()
    {
        // s10-05 rehearsal: a new repo had no evidence plan, so nothing reminded the person of the weekly review.
        var ct = TestContext.Current.CancellationToken;

        var exitCode = await Run("", "init", Repo, "--yes", "--owner", "grc@example.com");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var plan = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "evidence-plan.yaml"), ct);
        plan.ShouldContain("ref_id: EV-WEEKLY-REVIEW");
        plan.ShouldContain("every: 1 week");
        plan.ShouldContain("runbook: manual-sources/weekly-compliance-review.md");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "manual-sources", "weekly-compliance-review.md"), ct)).ShouldContain("hound review");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "access.yaml"), ct)).ShouldContain("grc@example.com");
        await ShouldValidateAsync();
    }

    [Fact]
    public async Task Compliance_owner_defaults_to_the_accountable_executive()
    {
        // s09-43: compliance is owned by the accountable executive, not the IT person who owns systems.
        var asked = await Run("\n\n\n\n\n\n\n", "init", Path.Combine(_root, "asked"));
        var defaulted = await Run("", "init", Repo, "--yes");

        asked.ShouldBe(ExitCodes.Ok, _error.ToString());
        defaulted.ShouldBe(ExitCodes.Ok, _error.ToString());
        _error.ToString().ShouldContain("Compliance owner's email, as a CISO Assistant user [ceo@example.com]");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "controls.yaml"), TestContext.Current.CancellationToken)).ShouldContain("owner: ceo@example.com");
    }

    [Fact]
    public async Task Writes_a_quarterly_series_starting_this_financial_year_quarter()
    {
        // s09-47: the organisation's audit is the series <CODE>-E8; its periods read EX-E8-Q2-2027FY and so on (s09-46).
        var exitCode = await RunOn("2026-11-23", "", "init", Repo, "--yes", "--organisation-audit");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var audit = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "audits", "ex-e8.yaml"), TestContext.Current.CancellationToken);
        audit.ShouldContain("ref_id: EX-E8\n");
        audit.ShouldContain("name: Essential Eight ML2\n");
        audit.ShouldContain("cycle: {period: quarter, starts: \"2026-10-01\"}");
        audit.ShouldNotContain("due_date");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "hound.yaml"), TestContext.Current.CancellationToken)).ShouldContain("compliance_assessment: EX-E8\n");
        await ShouldValidateAsync();
    }

    [Fact]
    public async Task A_monthly_series_starts_this_month()
    {
        var exitCode = await RunOn("2026-11-23", "", "init", Repo, "--yes", "--interval", "month", "--due", "2027-06-30", "--organisation-audit");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        (await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "audits", "ex-e8.yaml"), TestContext.Current.CancellationToken))
            .ShouldContain("cycle: {period: month, starts: \"2026-11-01\"}");
        _error.ToString().ShouldContain("--due is no longer used");
    }

    [Fact]
    public async Task An_interval_other_than_month_or_quarter_is_refused()
    {
        var exitCode = await RunOn("2026-11-23", "", "init", Repo, "--yes", "--interval", "year");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("The audit interval 'year' must be quarter or month.");
        Directory.Exists(Repo).ShouldBeFalse();
    }

    [Fact]
    public async Task Every_answer_can_be_given_as_an_option()
    {
        // s09-41: an agent gives every answer as an option, so nothing is asked even without --yes, and reads the result as JSON.
        var exitCode = await Run(
            "", "init", Repo, "--org", "Example Two Pty Ltd", "--code", "EX2", "--url", "https://ciso.example.com/api/", "--time-zone", "Australia/Perth", "--level", "1",
            "--interval", "month", "--owner", "grc@example.com", "--organisation-audit", "--assistant", "claude", "--json");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _error.ToString().ShouldNotContain("]: ");
        _error.ToString().ShouldNotContain("Organisation name");
        var answer = System.Text.Json.Nodes.JsonNode.Parse(_output.ToString())!;
        answer["organisation"]!.GetValue<string>().ShouldBe("Example Two Pty Ltd");
        answer["audits"]!.AsArray().Select(a => a!.GetValue<string>()).ShouldBe(["EX2-E8"]);
        var files = answer["files"]!.AsArray().Select(f => f!.GetValue<string>()).ToArray();
        files.ShouldContain("scope/setup.yaml");
        files.ShouldContain("CLAUDE.md");
        files.ShouldContain(".claude/skills/onboard-the-organisation/SKILL.md");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "hound.yaml"), TestContext.Current.CancellationToken)).ShouldContain("Australia/Perth");
        await ShouldValidateAsync();
    }

    [Fact]
    public async Task Answers_on_stdin_set_the_organisation_code_level_and_owner()
    {
        // Organisation, code, CISO Assistant URL, time zone, maturity level, audit interval, owner.
        var exitCode = await Run("Example Two Pty Ltd\nEX2\n\n\n3\nquarter\ngrc@example.com\n", "init", Repo, "--organisation-audit");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var setup = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "setup.yaml"), TestContext.Current.CancellationToken);
        setup.ShouldContain("- name: Example Two Pty Ltd");
        var audit = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "audits", "ex2-e8.yaml"), TestContext.Current.CancellationToken);
        audit.ShouldContain("ref_id: EX2-E8");
        audit.ShouldContain("groups: [\"1\", \"2\", \"3\"]");
        audit.ShouldContain("cycle: {period: quarter, starts: ");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "controls.yaml"), TestContext.Current.CancellationToken)).ShouldContain("owner: grc@example.com");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "hound.yaml"), TestContext.Current.CancellationToken)).ShouldContain("compliance_assessment: EX2-E8");
        _error.ToString().ShouldContain("Organisation name [Example Pty Ltd]");
        _error.ToString().ShouldContain("Compliance owner's email, as a CISO Assistant user");
        _error.ToString().ShouldContain("Audit interval (quarter or month) [quarter]");
        await ShouldValidateAsync();
    }

    [Fact]
    public async Task A_pasted_control_character_is_dropped_from_an_answer()
    {
        var exitCode = await Run("Example Two Pty Ltd\nEX2\n\n\n2\nquarter\n\u0016grc@example.com\n", "init", Repo);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var controls = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "controls.yaml"), TestContext.Current.CancellationToken);
        controls.ShouldContain("owner: grc@example.com");
        controls.ShouldNotContain("\u0016");
    }

    [Fact]
    public async Task Folder_that_is_not_empty_is_refused()
    {
        Directory.CreateDirectory(Repo);
        var existing = Path.Combine(Repo, "notes.txt");
        await File.WriteAllTextAsync(existing, "keep me", TestContext.Current.CancellationToken);

        var exitCode = await Run("", "init", Repo, "--yes");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("not empty");
        Directory.GetFileSystemEntries(Repo).ShouldBe([existing]);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Framework_disp_adds_the_disp_audit()
    {
        // s14-02: DISP membership readiness is held as an audit beside the Essential Eight, from the custom library hound carries.
        var ct = TestContext.Current.CancellationToken;

        var exitCode = await RunOn("2026-11-23", "", "init", Repo, "--yes", "--framework", "disp");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        foreach (var file in new[] { "frameworks/disp.yaml", "frameworks/disp.json", "mappings/disp.yaml", "scope/audits/ex-disp.yaml" })
        {
            File.Exists(Path.Combine(Repo, file)).ShouldBeTrue(file);
        }

        var audit = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "audits", "ex-disp.yaml"), ct);
        audit.ShouldContain("ref_id: EX-DISP\n");
        audit.ShouldContain("framework: urn:evidence-hound:risk:framework:disp-membership\n");
        var setup = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "setup.yaml"), ct);
        setup.ShouldContain("file: ../frameworks/disp.yaml");
        var config = await File.ReadAllTextAsync(Path.Combine(Repo, "hound.yaml"), ct);
        config.ShouldNotContain("compliance_assessment: EX-E8\n");
        config.ShouldContain("mapping: mappings/disp.yaml");
        config.ShouldContain("compliance_assessment: EX-DISP\n");
        var readme = await File.ReadAllTextAsync(Path.Combine(Repo, "README.md"), ct);
        readme.ShouldContain("EX-DISP");
        readme.ShouldContain("examples/manual-sources/disp");
        readme.ShouldContain("hound mapping coverage mappings/disp.yaml --framework frameworks/disp.json --manual hound.yaml");
        await ShouldValidateAsync();
    }

    [Fact]
    public async Task Organisation_audit_only_with_an_organisation_wide_source()
    {
        // s18-08: audits are per system, so each shows its own gaps; an organisation-wide audit no organisation-wide source feeds is an
        // empty shell. init writes one only when asked, and says how to draft one per system.
        var ct = TestContext.Current.CancellationToken;
        (await Run("", "init", Repo, "--yes")).ShouldBe(ExitCodes.Ok, _error.ToString());

        Directory.GetFiles(Path.Combine(Repo, "scope", "audits"), "*.yaml").ShouldBeEmpty();
        var config = await File.ReadAllTextAsync(Path.Combine(Repo, "hound.yaml"), ct);
        config.ShouldContain("targets: []");
        var readme = await File.ReadAllTextAsync(Path.Combine(Repo, "README.md"), ct);
        readme.ShouldContain("hound audit draft");
        readme.ShouldContain("one audit per system");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "setup.yaml"), ct)).ShouldContain("audit_series: {ref_id: EX-E8, groups: [\"1\", \"2\"], cycle: {period: quarter, starts: ");
        await ShouldValidateAsync();

        var organisation = Path.Combine(_root, "organisation");
        (await Run("", "init", organisation, "--yes", "--organisation-audit")).ShouldBe(ExitCodes.Ok, _error.ToString());
        Directory.GetFiles(Path.Combine(organisation, "scope", "audits"), "*.yaml").ShouldHaveSingleItem();
        (await File.ReadAllTextAsync(Path.Combine(organisation, "hound.yaml"), ct)).ShouldContain("compliance_assessment: EX-E8\n");
    }

    [Fact]
    public async Task An_unknown_framework_is_refused()
    {
        var exitCode = await Run("", "init", Repo, "--yes", "--framework", "iso-27001");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("disp");
    }

    private async Task ShouldValidateAsync()
    {
        var config = Path.Combine(Repo, "hound.yaml");
        (await Run("", "config", "validate", config)).ShouldBe(ExitCodes.Ok, _error.ToString() + _output);
        (await Run("", "scope", "validate", Path.Combine(Repo, "scope"), "--config", config)).ShouldBe(ExitCodes.Ok, _error.ToString() + _output);
    }

    private Task<int> RunOn(string today, string input, params string[] args) =>
        HoundCli.RunAsync(args, new StringReader(input), _output, _error, new Dictionary<string, string?> { ["HOUND_TODAY"] = today }, TestContext.Current.CancellationToken);

    private Task<int> Run(string input, params string[] args) =>
        HoundCli.RunAsync(args, new StringReader(input), _output, _error, configureLogging: null, TestContext.Current.CancellationToken);
}
