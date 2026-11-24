namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s12-02: the fictional spreadsheet registers in <c>examples/migration/</c> move into a compliance repo with the commands the migration
/// guide gives, row by row, as an agent would run them, and the reconciliation counts in the guide hold.
/// </summary>
public sealed class MigrationExampleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-migration-tests-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static string Examples => Path.Combine(ExampleRepoTests.RepoRoot, "examples", "migration");

    private string Repo => Path.Combine(_root, "repo");

    private string Scope => Path.Combine(Repo, "scope");

    [Fact]
    public async Task The_sample_registers_load_with_the_commands_in_the_guide()
    {
        TestFiles.CopyExampleRepo(Repo);
        var systems = Csv("systems.csv");
        var controls = Csv("controls.csv");
        var suppliers = Csv("suppliers.csv");

        foreach (var row in systems)
        {
            string[] parents = row["parents"].Length > 0 ? ["--parents", row["parents"]] : [];
            (await Run(
            [
                "add", "system", "--scope", Scope, "--yes", "--domain", "Example Pty Ltd", "--name", row["name"], "--ref-id", row["ref_id"],
                "--type", row["type"], "--owner", row["owner"], "--description", row["description"], .. parents, "--audit", "EX-E8-2026",
            ])).ShouldBe(ExitCodes.Ok, row["name"]);
        }

        foreach (var row in controls)
        {
            (await Run(
                "add", "control", "--scope", Scope, "--yes", "--domain", "Example Pty Ltd", "--ref-id", row["ref_id"], "--name", row["name"],
                "--owner", row["owner"], "--category", row["category"], "--systems", row["systems"], "--audit", "EX-E8-2026",
                "--requirements", row["requirements"])).ShouldBe(ExitCodes.Ok, row["ref_id"]);
        }

        foreach (var row in suppliers)
        {
            string[] data = row["data"].Length > 0 ? ["--data", row["data"]] : [];
            var (exit, _, error) = await OnboardAgentCommandTests.RunAsync(
                [
                    "supplier", "draft", "--repo", Repo, "--ref", row["ref"], "--name", row["name"], "--kind", row["kind"],
                    "--solution-ref", row["solution_ref"], "--solution-name", row["solution_name"],
                    "--contract-ref", row["contract_ref"], "--contract-name", row["contract_name"],
                    "--description", row["description"], "--end", row["contract_end"], "--json", .. data,
                ],
                "", terminal: false, Path.Combine(_root, "home"), _ct);
            exit.ShouldBe(ExitCodes.Ok, error);
        }

        // Reconciliation: every register row is now in the repo, by ref_id.
        var setup = await File.ReadAllTextAsync(Path.Combine(Scope, "setup.yaml"), _ct);
        systems.ShouldAllBe(row => setup.Contains($"ref_id: {row["ref_id"]}", StringComparison.Ordinal));
        var controlFile = await File.ReadAllTextAsync(Path.Combine(Scope, "controls.yaml"), _ct);
        controls.ShouldAllBe(row => controlFile.Contains(row["ref_id"], StringComparison.Ordinal));
        var drafts = Directory.EnumerateFiles(Repo, "*.yaml", SearchOption.AllDirectories).Select(f => File.ReadAllText(f)).ToList();
        suppliers.ShouldAllBe(row => drafts.Any(d => d.Contains(row["ref"], StringComparison.Ordinal)));
        (await Run("scope", "validate", Scope)).ShouldBe(ExitCodes.Ok);
        suppliers.Where(row => row["data"].Length > 0).ShouldAllBe(row => drafts.Any(d => d.Contains(row["data"].Split(':')[2], StringComparison.Ordinal)));
    }

    [Fact]
    public void The_reconciliation_counts_in_the_guide_match_the_sample_registers()
    {
        var guide = File.ReadAllText(Path.Combine(ExampleRepoTests.RepoRoot, "docs", "adopt", "migrate-from-spreadsheets.md"));

        guide.ShouldContain($"| Systems | {Csv("systems.csv").Count} |");
        guide.ShouldContain($"| Applied controls | {Csv("controls.csv").Count} |");
        guide.ShouldContain($"| Suppliers | {Csv("suppliers.csv").Count} |");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Task<int> Run(params string[] args) =>
        HoundCli.RunAsync(args, new StringReader(""), TextWriter.Null, TextWriter.Null, configureLogging: null, _ct);

    /// <summary>A simple CSV: a heading row, then rows; a field in double quotes may hold commas.</summary>
    private static List<Dictionary<string, string>> Csv(string name)
    {
        var lines = File.ReadAllLines(Path.Combine(Examples, name)).Where(l => l.Length > 0).ToList();
        var headings = Fields(lines[0]);
        return [.. lines.Skip(1).Select(line => headings.Zip(Fields(line)).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal))];
    }

    private static List<string> Fields(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
