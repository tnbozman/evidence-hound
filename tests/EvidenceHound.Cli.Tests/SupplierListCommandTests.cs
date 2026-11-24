using System.Text.Json.Nodes;

namespace EvidenceHound.Cli.Tests;

/// <summary>s10-06: <c>hound supplier list --data</c> prints the list a prime or an auditor asks for: who handles a kind of data, and on what authority.</summary>
public sealed class SupplierListCommandTests : IDisposable
{
    private static readonly string Supplier = """
        ref_id: SUP-EXCLOUD
        name: Example Cloud Pty Ltd
        country: AU
        domain: Example Pty Ltd
        solutions:
          - ref_id: SOL-EXCLOUD-HOST
            name: Managed hosting
            data:
              - kind: cui
                detail: CUI//SP-CTI
                role: stores
                authority:
                  name: FedRAMP Moderate equivalent
                  evidence: documents/suppliers/example-cloud-fedramp.pdf
                  expires: "2027-06-30"
              - kind: pii
                role: stores
          - ref_id: SOL-EXCLOUD-BACKUP
            name: Offsite backup
            data:
              - kind: pii
                role: stores
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("hound-supplier-list-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public SupplierListCommandTests()
    {
        TestFiles.CopyExampleRepo(Repo);
        Directory.CreateDirectory(Path.Combine(Repo, "scope", "third-parties"));
        File.WriteAllText(Path.Combine(Repo, "scope", "third-parties", "sup-excloud.yaml"), Supplier);
        var setup = Path.Combine(Repo, "scope", "setup.yaml");
        File.WriteAllText(setup, File.ReadAllText(setup).Replace(
            "        owner: it@example.com\n        tags:\n          essentials_domain: identity\n",
            "        owner: it@example.com\n        solution: SOL-EXCLOUD-HOST\n        data:\n          - kind: cui\n            detail: CUI//SP-CTI\n            role: processes\n        tags:\n          essentials_domain: identity\n",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lists_suppliers_handling_a_kind_with_authority()
    {
        var (exit, output, error) = await RunAsync(["supplier", "list", "--data", "cui", "--repo", Repo, "--json"]);

        exit.ShouldBe(ExitCodes.Ok, error);
        var row = JsonNode.Parse(output)!["solutions"]!.AsArray().ShouldHaveSingleItem()!;
        (row["supplier"]!.GetValue<string>(), row["solution"]!.GetValue<string>(), row["role"]!.GetValue<string>(), row["detail"]!.GetValue<string>())
            .ShouldBe(("SUP-EXCLOUD", "SOL-EXCLOUD-HOST", "stores", "CUI//SP-CTI"));
        (row["authority"]!.GetValue<string>(), row["evidence"]!.GetValue<string>(), row["expires"]!.GetValue<string>())
            .ShouldBe(("FedRAMP Moderate equivalent", "documents/suppliers/example-cloud-fedramp.pdf", "2027-06-30"));
        row["systems"]!.AsArray().Select(s => s!.GetValue<string>()).ShouldBe(["Example 365"]);

        (exit, output, error) = await RunAsync(["supplier", "list", "--data", "pii", "--repo", Repo]);
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("| SUP-EXCLOUD | SOL-EXCLOUD-HOST | stores |");
        output.ShouldContain("| SUP-EXCLOUD | SOL-EXCLOUD-BACKUP | stores |");
        output.ShouldContain("no authority recorded");
    }

    [Fact]
    public async Task A_sensitive_data_finding_warns_and_blocks_nothing()
    {
        // s10-05 (maintainer's decision, 2026-11-24): recording the truth must not lock the repo. The machine shop's agent hid the finding to keep working.
        var setup = Path.Combine(Repo, "scope", "setup.yaml");
        await File.WriteAllTextAsync(setup, (await File.ReadAllTextAsync(setup, _ct)).Replace(
            "kind: cui\n            detail: CUI//SP-CTI\n            role: processes", "kind: classified\n            detail: PROTECTED\n            role: processes", StringComparison.Ordinal), _ct);
        var scope = Path.Combine(Repo, "scope");

        var (exit, output, error) = await RunAsync(["scope", "validate", scope]);
        exit.ShouldBe(ExitCodes.Ok, error);
        (output + error).ShouldContain("warning: scope/setup.yaml line");
        (output + error).ShouldContain("[data.supplier_not_authorised]");

        (exit, output, error) = await RunAsync(["scope", "validate", scope, "--json"]);
        exit.ShouldBe(ExitCodes.Ok, error);
        var finding = JsonNode.Parse(output)!.AsArray().ShouldHaveSingleItem()!;
        (finding["code"]!.GetValue<string>(), finding["severity"]!.GetValue<string>()).ShouldBe(("data.supplier_not_authorised", "warning"));

        (exit, _, error) = await RunAsync(["add", "control", "--scope", scope, "--ref-id", "AC-NEW", "--name", "A new control", "--owner", "it@example.com", "-y"]);
        exit.ShouldBe(ExitCodes.Ok, error);

        // It is not lost: the weekly review lists it until the data moves or the authority is recorded.
        (exit, output, error) = await RunAsync(["review", "pending", "--repo", Repo, "--json"]);
        exit.ShouldBe(ExitCodes.Ok, error);
        var item = JsonNode.Parse(output)!["items"]!.AsArray().Single(i => i!["kind"]!.GetValue<string>() == "finding")!;
        item["key"]!.GetValue<string>().ShouldStartWith("finding:data.supplier_not_authorised/");
        item["title"]!.GetValue<string>().ShouldContain("SOL-EXCLOUD-HOST");
    }

    [Fact]
    public async Task An_unknown_kind_is_refused()
    {
        var (exit, _, error) = await RunAsync(["supplier", "list", "--data", "secret_sauce", "--repo", Repo]);

        exit.ShouldNotBe(ExitCodes.Ok);
        error.ShouldContain("secret_sauce");
    }

    private Task<(int Exit, string Output, string Error)> RunAsync(string[] args) => OnboardAgentCommandTests.RunAsync(args, "", terminal: false, _root, _ct);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Repo => Path.Combine(_root, "repo");
}
