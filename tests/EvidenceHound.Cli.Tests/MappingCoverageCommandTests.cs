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
        text.ShouldContain("| Parent | Group | Automated | Manual | Uncovered |");
        text.ShouldContain("| mfa | 2 |");
        text.ShouldContain("| rb | 1 |");
        text.ShouldNotContain("| ml3");
        text.ShouldMatch(@"(?s).*Total: \d+ automated, \d+ manual, 0 uncovered.*");
    }

    [Fact]
    public async Task Without_manual_sources_every_attested_requirement_is_uncovered()
    {
        var exitCode = await Run(
            "mapping", "coverage", Repo("mappings", "essential-eight-2023.yaml"),
            "--framework", Repo("frameworks", "essential-eight-2023.json"),
            "--groups", "1,2");

        exitCode.ShouldBe(ExitCodes.Ok);
        _output.ToString().ShouldMatch(@"(?s).*Total: \d+ automated, 0 manual, [1-9]\d* uncovered.*");
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
