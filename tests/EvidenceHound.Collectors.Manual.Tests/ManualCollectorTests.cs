using EvidenceHound.Core.Checks;
using static EvidenceHound.Collectors.Manual.Tests.DropFolder;

namespace EvidenceHound.Collectors.Manual.Tests;

public sealed class ManualCollectorTests : IDisposable
{
    private static readonly string Export = "firewall-config-2026-11.txt";

    private static readonly string Attested = """
        who: Alex Example
        date: 2026-11-20T09:30:00Z
        note: Rules reviewed with the network team.
        outcomes:
          network.firewall.rules_reviewed: pass
          network.firewall.config_exported: pass
        """;

    private readonly DropFolder _drop = new();

    [Fact]
    public async Task New_file_with_attestation_passes()
    {
        _drop.Add(Export, "rule 1: allow https from any to web-01.example.com", Now.AddDays(-2));
        _drop.Attest(Export, Attested);

        var result = (await _drop.CollectAsync()).Value();

        result.Checks.Select(c => c.CheckId.Value).ShouldBe([Reviewed, Exported]);
        result.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Pass && c.CollectedAt == Now && c.Source == "example-firewall-config");
        result.Checks[0].Observation.ShouldContain("Alex Example");
        result.Checks[0].Observation.ShouldContain("Rules reviewed with the network team.");
        result.Checks[0].EvidenceRefs.ShouldBe([Export, Attestation.SidecarFor(Export)]);
        result.Files.Select(f => f.FileName).ShouldBe([Export, Attestation.SidecarFor(Export)], ignoreOrder: true);
    }

    [Fact]
    public async Task Only_attested_results_are_published()
    {
        // s09-42: an agent's proposal beside the file is pending. It is not evidence, and no run publishes it or what it says.
        const string content = "rule 1: allow https from any to web-01.example.com";
        _drop.Add(Export, content, Now.AddDays(-2));
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(_drop.Path, Export))));
        File.WriteAllText(Path.Combine(_drop.Path, Export + ".proposal.yaml"), $"proposed_by: agent\nfile_sha256: {hash}\nobservation: The agent read 14 rules.\noutcomes:\n  {Reviewed}: pass\n");

        var pending = (await _drop.CollectAsync()).Value();

        pending.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Partial && c.Observation.Contains("not attested", StringComparison.Ordinal) && !c.Observation.Contains("14 rules", StringComparison.Ordinal));
        pending.Files.ShouldHaveSingleItem().FileName.ShouldBe(Export);

        // Attested by a person from the proposal: published, and it says who proposed and who attested, with the file's hash.
        _drop.Attest(Export, Attested.Replace("who: Alex Example", $"who: Sam Example\nproposed_by: agent\nfile_sha256: {hash}", StringComparison.Ordinal));
        var attested = (await _drop.CollectAsync()).Value();

        attested.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Pass);
        attested.Checks[0].Observation.ShouldStartWith($"Proposed by agent, attested pass by Sam Example on 2026-11-20: {Export} (sha256 {hash[..12]}).", Case.Sensitive);
        attested.Checks[0].EvidenceRefs.ShouldBe([Export, Attestation.SidecarFor(Export)]);
        attested.Files.Select(f => f.FileName).ShouldBe([Export, Attestation.SidecarFor(Export)], ignoreOrder: true);

        // A file changed after it was attested is no longer the file the person attested.
        _drop.Add(Export, content + "\nrule 2: allow any from any to any", Now.AddDays(-1));
        var changed = (await _drop.CollectAsync()).Value();
        changed.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Partial && c.Observation.Contains("changed since it was attested", StringComparison.Ordinal));
    }

    [Fact]
    public async Task File_without_attestation_is_partial()
    {
        _drop.Add(Export, "rule 1: allow https", Now.AddDays(-2));

        var result = (await _drop.CollectAsync()).Value();

        result.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Partial && c.Observation.Contains("evidence present, not attested", StringComparison.OrdinalIgnoreCase));
        result.Checks[0].EvidenceRefs.ShouldBe([Export]);
        result.Files.ShouldHaveSingleItem().FileName.ShouldBe(Export);
    }

    [Fact]
    public async Task No_file_within_interval_is_overdue_error()
    {
        _drop.Add(Export, "rule 1: allow https", Now.AddDays(-40));
        _drop.Attest(Export, Attested.Replace("2026-11-20T09:30:00Z", "2026-10-12T02:00:00Z", StringComparison.Ordinal));

        var result = (await _drop.CollectAsync()).Value();

        result.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Error && c.Observation.Contains("overdue since 2026-11-11", StringComparison.Ordinal));
        result.Checks.ShouldAllBe(c => c.EvidenceRefs.Count == 0);
        result.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Same_file_twice_is_not_reuploaded()
    {
        _drop.Add("firewall-a.txt", "rule 1: allow https", Now.AddDays(-3));
        _drop.Add("firewall-b.txt", "rule 1: allow https", Now.AddDays(-2));

        var result = (await _drop.CollectAsync()).Value();

        result.Files.ShouldHaveSingleItem().FileName.ShouldBe("firewall-b.txt");
        result.Checks.ShouldAllBe(c => c.EvidenceRefs.Count == 1 && c.EvidenceRefs[0] == "firewall-b.txt");
    }

    [Fact]
    public async Task No_file_ever_is_an_error_naming_the_owner_and_interval()
    {
        var result = (await _drop.CollectAsync()).Value();

        result.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Error);
        result.Checks[0].Observation.ShouldContain("No evidence uploaded yet");
        result.Checks[0].Observation.ShouldContain("every 30 days");
        result.Checks[0].Observation.ShouldContain("Alex Example");
    }

    [Fact]
    public async Task Newest_file_decides_and_older_files_in_the_interval_are_still_evidence()
    {
        _drop.Add("firewall-old.txt", "rule 1: allow http", Now.AddDays(-20));
        _drop.Add(Export, "rule 1: allow https", Now.AddDays(-2));
        _drop.Attest(Export, Attested);

        var result = (await _drop.CollectAsync()).Value();

        result.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Pass);
        result.Files.Select(f => f.FileName).ShouldBe(["firewall-old.txt", Export, Attestation.SidecarFor(Export)], ignoreOrder: true);
    }

    [Fact]
    public async Task Attested_fail_is_reported_as_fail_and_missing_outcome_as_partial()
    {
        _drop.Add(Export, "rule 1: allow any", Now.AddDays(-2));
        _drop.Attest(Export, """
            who: Alex Example
            date: 2026-11-20
            outcomes:
              network.firewall.rules_reviewed: fail
            """);

        var result = (await _drop.CollectAsync()).Value();

        result.Checks.Single(c => c.CheckId.Value == Reviewed).Outcome.ShouldBe(CheckOutcome.Fail);
        var exported = result.Checks.Single(c => c.CheckId.Value == Exported);
        exported.Outcome.ShouldBe(CheckOutcome.Partial);
        exported.Observation.ShouldContain("not attested for this check");
    }

    [Fact]
    public async Task Files_not_matching_the_patterns_are_ignored()
    {
        _drop.Add("notes.md", "not an export", Now.AddDays(-1));

        var result = (await _drop.CollectAsync()).Value();

        result.Files.ShouldBeEmpty();
        result.Checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Error);
    }

    [Fact]
    public async Task Invalid_attestation_is_a_problem()
    {
        _drop.Add(Export, "rule 1: allow https", Now.AddDays(-2));
        _drop.Attest(Export, "who: Alex Example\ndate: 2026-11-20\noutcomes: {network.firewall.rules_reviewed: maybe}\n");

        var problem = (await _drop.CollectAsync()).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("manual.attestation_invalid");
        problem.Message.ShouldContain(Attestation.SidecarFor(Export));
        problem.Message.ShouldContain("maybe");
    }

    [Fact]
    public async Task Missing_drop_folder_is_a_problem()
    {
        var problem = (await _drop.CollectAsync(Settings(System.IO.Path.Combine(_drop.Path, "missing")))).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("manual.drop_folder_missing");
    }

    [Fact]
    public async Task Invalid_settings_are_problems()
    {
        var settings = Settings(_drop.Path);
        settings[ManualSettings.DueDays] = "soon";
        settings[ManualSettings.Checks] = "Not A Check";

        var problems = (await _drop.CollectAsync(settings)).Problems();

        problems.Select(p => p.Code).ShouldBe(["manual.setting_invalid", "manual.setting_invalid"]);
    }

    [Fact]
    public void Describes_its_checks_through_the_checks_setting_and_its_folder_as_a_path()
    {
        var description = new ManualCollector().Describe();

        description.Checks.ShouldBeEmpty();
        description.ChecksSetting.ShouldBe(ManualSettings.Checks);
        description.Settings.Single(s => s.Name == ManualSettings.DropFolder).ShouldBe(new SettingDescription(ManualSettings.DropFolder, description.Settings[0].Description, Required: true, IsPath: true));
        description.Settings.Where(s => s.Required).Select(s => s.Name).ShouldBe([ManualSettings.DropFolder, ManualSettings.Checks, ManualSettings.DueDays, ManualSettings.Owner]);
        description.Settings.Single(s => s.Name == ManualSettings.Patterns).Required.ShouldBeFalse();
        description.Secrets.ShouldBeEmpty();
    }

    public void Dispose() => _drop.Dispose();
}
