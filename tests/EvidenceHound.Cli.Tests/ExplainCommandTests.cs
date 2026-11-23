using System.Text.Json.Nodes;

namespace EvidenceHound.Cli.Tests;

/// <summary><c>hound explain</c> reads a saved plan, a diff, or two versions of a file, and writes a review summary. Review only.</summary>
public sealed class ExplainCommandTests : IDisposable
{
    internal const string Plan = """
        ## Scope plan

        1 to create, 0 to update, 1 to delete. No drift.

        | Change | Kind | Object | Fields |
        | --- | --- | --- | --- |
        | Create | assessment | EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023/EX-2 | result: not_applicable |
        | Delete | control | CTL-OLD |  |
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("hound-explain-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_a_diff_or_a_saved_plan()
    {
        var plan = Path.Combine(_root, "plan.md");
        await File.WriteAllTextAsync(plan, Plan, _ct);

        var (exit, output, error) = await RunAsync(["explain", plan, "--repo", _root], "");

        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("The plan is the source of truth: 1 to create, 0 to update, 1 to delete, 0 drifted.");
        output.ShouldContain("- Deletes control CTL-OLD.");
        File.ReadAllText(plan).ShouldBe(Plan);

        // A diff on standard input, such as git diff | hound explain.
        const string diff = "diff --git a/mappings/example.yaml b/mappings/example.yaml\n--- a/mappings/example.yaml\n+++ b/mappings/example.yaml\n@@ -1,3 +1,2 @@\n requirements:\n-  - ref: EX-4\n   - ref: EX-5\n";
        (exit, output, error) = await RunAsync(["explain", "--repo", _root], diff);
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("1 file changed, 0 lines added, 1 line removed.");
        output.ShouldContain("Removes 1 line(s) naming checks or requirements from mappings/example.yaml.");

        // A check removed from a profile that a mapping in the repo still uses.
        Directory.CreateDirectory(Path.Combine(_root, "mappings"));
        await File.WriteAllTextAsync(Path.Combine(_root, "mappings", "example.yaml"), "requirements:\n  - ref: EX-4\n    checks: [devices.supported_os]\n", _ct);
        const string profileDiff = "--- a/profiles/example.yaml\n+++ b/profiles/example.yaml\n@@ -1,2 +1,1 @@\n checks:\n-  - id: devices.supported_os\n";
        (exit, output, error) = await RunAsync(["explain", "--repo", _root], profileDiff);
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("Removes check devices.supported_os from profiles/example.yaml, but mappings/example.yaml still uses it.");

        // Two versions of one file.
        var before = Path.Combine(_root, "before.yaml");
        var after = Path.Combine(_root, "after.yaml");
        await File.WriteAllTextAsync(before, "ref_id: EX-AUDIT\nresult: not_assessed\n", _ct);
        await File.WriteAllTextAsync(after, "ref_id: EX-AUDIT\nresult: not_applicable\n", _ct);
        (exit, output, error) = await RunAsync(["explain", before, after, "--repo", _root], "");
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("Marks 1 requirement(s) not applicable in after.yaml.");

        // Nothing to read is a usage error.
        (exit, _, _) = await RunAsync(["explain", "--repo", _root], "");
        exit.ShouldBe(ExitCodes.Usage);
    }

    [Fact]
    public async Task Json_gives_what_code_reads_from_the_change_and_asks_no_model()
    {
        var plan = Path.Combine(_root, "plan.md");
        await File.WriteAllTextAsync(plan, Plan, _ct);

        // s09-37: for an agent. No onboarding config exists, and none is needed.
        var (exit, output, error) = await RunAsync(["explain", plan, "--json", "--repo", _root], "");

        exit.ShouldBe(ExitCodes.Ok, error);
        var explained = JsonNode.Parse(output)!;
        (explained["source"]!.GetValue<string>(), explained["kind"]!.GetValue<string>()).ShouldBe(("plan.md", "plan"));
        explained["summary"]!.GetValue<string>().ShouldBe("1 to create, 0 to update, 1 to delete, 0 drifted.");
        explained["flags"]!.AsArray().Select(f => f!.GetValue<string>()).ShouldContain("Deletes control CTL-OLD.");
        explained["topics"]!.AsArray().ShouldNotBeNull();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<(int Exit, string Output, string Error)> RunAsync(string[] args, string input)
    {
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_CONFIG_HOME"] = Path.Combine(_root, "home") };
        var exit = await HoundCli.RunAsync(args, reader, output, error, settings, _ct);
        return (exit, output.ToString(), error.ToString());
    }
}
