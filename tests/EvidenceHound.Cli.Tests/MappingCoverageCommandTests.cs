using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary>s12-01: <c>hound mapping coverage</c> prints automated, manual and uncovered counts per parent node and group.</summary>
public sealed class MappingCoverageCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();

    private static string Repo(params string[] parts) => Path.Combine([ExampleRepoTests.RepoRoot, .. parts]);

    [Fact]
    public async Task Essential_eight_ml2_has_no_uncovered_requirement_with_the_example_manual_sources()
    {
        var exitCode = await Run(
            "mapping", "coverage", Repo("mappings", "essential-eight-2023.yaml"),
            "--framework", Repo("frameworks", "essential-eight-2023.json"),
            "--groups", "1,2",
            "--manual", Repo("examples", "manual-sources", "essential-eight"),
            "--manual", Repo("examples", "example-essential-eight.yaml"));

        exitCode.ShouldBe(ExitCodes.Ok);
        var text = _output.ToString();
        text.ShouldContain("| Parent | Group | Automated | Manual | Draft | Uncovered |");
        text.ShouldContain("| mfa | 2 |");
        text.ShouldContain("| rb | 1 |");
        text.ShouldNotContain("| ml3");
        text.ShouldMatch(@"(?s).*Total: \d+ automated, \d+ manual, 0 draft, 0 uncovered.*");
    }

    [Fact]
    public async Task Without_manual_sources_every_mapped_requirement_counts_as_automated()
    {
        var exitCode = await Run(
            "mapping", "coverage", Repo("mappings", "essential-eight-2023.yaml"),
            "--framework", Repo("frameworks", "essential-eight-2023.json"),
            "--groups", "1,2");

        exitCode.ShouldBe(ExitCodes.Ok);
        _output.ToString().ShouldMatch(@"(?s).*Total: 155 automated, 0 manual, 0 draft, 0 uncovered.*");
    }

    [Fact]
    public async Task Json_lists_one_object_per_parent_and_group()
    {
        var exitCode = await Run(
            "mapping", "coverage", Repo("mappings", "ai6.yaml"),
            "--framework", Repo("frameworks", "ai6.json"),
            "--groups", "F",
            "--manual", Repo("examples", "manual-sources", "ai6"),
            "--json");

        exitCode.ShouldBe(ExitCodes.Ok);
        using var json = System.Text.Json.JsonDocument.Parse(_output.ToString());
        var rows = json.RootElement.GetProperty("rows");
        rows.GetArrayLength().ShouldBe(6);
        rows[0].GetProperty("group").GetString().ShouldBe("F");
        json.RootElement.GetProperty("uncovered").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Cmmc_rows_that_cover_part_of_their_practice_are_counted_so_a_person_finishes_them()
    {
        // s17-09: every CMMC row is covers: part, yet coverage read "34 automated" as if the checks answered those practices.
        var exitCode = await Run(
            "mapping", "coverage", Repo("mappings", "cmmc-2.0.yaml"), "--framework", Repo("frameworks", "cmmc-2.0.json"), "--groups", "L1,L2", "--json");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        using var json = System.Text.Json.JsonDocument.Parse(_output.ToString());
        var automated = json.RootElement.GetProperty("automated").GetInt32();
        automated.ShouldBeGreaterThan(0);
        json.RootElement.GetProperty("part").GetInt32().ShouldBe(automated);
        json.RootElement.GetProperty("rows")[0].TryGetProperty("part", out _).ShouldBeTrue();

        _output.GetStringBuilder().Clear();
        (await Run("mapping", "coverage", Repo("mappings", "cmmc-2.0.yaml"), "--framework", Repo("frameworks", "cmmc-2.0.json"), "--groups", "L1,L2"))
            .ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldContain($"{automated} of the automated cover only part of their requirement: a person assesses the rest.");
    }

    [Fact]
    public async Task Essentials_phase_1_is_counted_per_domain_with_no_uncovered_action()
    {
        // s14-01: the ASD Essentials rates each action per domain, so coverage also counts per domain and priority phase.
        var exitCode = await Run(
            "mapping", "coverage", Repo("mappings", "asd-essentials-enterprise-it.yaml"),
            "--framework", Repo("frameworks", "asd-essentials-enterprise-it.json"),
            "--groups", "1",
            "--manual", Repo("examples", "manual-sources"),
            "--manual", Repo("examples", "example-essential-eight.yaml"));

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var text = _output.ToString();
        text.ShouldContain("| Top level | Group | Automated | Manual | Draft | Uncovered |");
        text.ShouldContain("| Boundary services | 1 |");
        text.ShouldContain("| Identity services | 1 |");
        text.ShouldContain("| Governance and culture | 1 |");
        text.ShouldContain("| Visibility and response | 1 |");
        text.ShouldNotContain("| Business systems");
        // s14-04: the same principle sits in several domains, so the per-parent table names the domain too.
        text.ShouldContain("| Boundary services / Secure system lifecycle | 1 |");
        text.ShouldContain("| Identity services / Secure system lifecycle | 1 |");
        text.ShouldMatch(@"(?s).*Total: \d+ automated, \d+ manual, 0 draft, 0 uncovered.*");
    }

    [Fact]
    public async Task A_derived_mapping_counts_as_draft_until_reviewed()
    {
        // s14-03 rehearsal: an all-draft derived mapping read as "22 automated, 33 manual", yet runs publish none of it.
        var draft = Path.Combine(Path.GetTempPath(), $"hound-coverage-{Guid.NewGuid():N}.yaml");
        try
        {
            (await Run(
                "mapping", "derive", Repo("mappings", "essential-eight-2023.yaml"),
                "--from", Repo("frameworks", "essential-eight-2023.json"),
                "--to", Repo("frameworks", "asd-essentials-enterprise-it.json"),
                "--links", Repo("frameworks", "mapping-essential-eight-2023-and-asd-essentials-enterprise-it.yaml"),
                "--groups", "1",
                "--out", draft)).ShouldBe(ExitCodes.Ok, _error.ToString());
            _output.GetStringBuilder().Clear();

            var exitCode = await Run("mapping", "coverage", draft, "--framework", Repo("frameworks", "asd-essentials-enterprise-it.json"), "--groups", "1");

            exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
            _output.ToString().ShouldMatch(@"(?s).*Total: 0 automated, 0 manual, 55 draft, 38 uncovered.*");
        }
        finally
        {
            File.Delete(draft);
        }
    }

    [Fact]
    public async Task A_manual_folder_with_no_manual_source_is_reported()
    {
        // s14-03 rehearsal: --manual pointed at a folder with no sources, so every attested check counted as automated.
        var empty = Directory.CreateTempSubdirectory("hound-no-manual-");
        try
        {
            var exitCode = await Run(
                "mapping", "coverage", Repo("mappings", "disp.yaml"), "--framework", Repo("frameworks", "disp.json"), "--manual", empty.FullName);

            exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
            _logs.Messages.ShouldContain(m => m.Contains("no manual source", StringComparison.Ordinal));
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Unknown_group_is_a_configuration_error()
    {
        var exitCode = await Run(
            "mapping", "coverage", Repo("mappings", "ai6.yaml"), "--framework", Repo("frameworks", "ai6.json"), "--groups", "9");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("9");
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
    }

    private Task<int> Run(params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs));
}
