using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Yaml;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-41: <c>hound supplier draft</c> drafts a vendor, and a reseller or managed service provider as third parties of their own that name
/// the vendor they stand for, each with the documents the agent read and kept.
/// </summary>
public sealed class SupplierDraftCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-supplier-draft-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public SupplierDraftCommandTests() => TestFiles.CopyExampleRepo(Repo);

    [Fact]
    public async Task Reseller_and_msp_are_drafted_as_their_own_third_parties()
    {
        var kept = Path.Combine(Repo, "documents", "suppliers", "example-reseller-terms.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        await File.WriteAllBytesAsync(kept, [0x25, 0x50, 0x44, 0x46], _ct);

        (await DraftAsync("SUP-VENDOR", "Example Vendor Ltd", "Firewall")).Exit.ShouldBe(ExitCodes.Ok);
        var (exit, output, error) = await DraftAsync(
            "SUP-RESELLER", "Example Reseller Pty Ltd", "Firewall licences", "--kind", "reseller", "--for", "SUP-VENDOR", "--document", "documents/suppliers/example-reseller-terms.pdf");
        exit.ShouldBe(ExitCodes.Ok, error);
        var answer = JsonNode.Parse(output)!;
        answer["kind"]!.GetValue<string>().ShouldBe("reseller");
        answer["file"]!.GetValue<string>().ShouldBe("scope/third-parties/sup-reseller.yaml");
        answer["approved"]!.GetValue<bool>().ShouldBeFalse();
        (exit, _, error) = await DraftAsync("SUP-MSP", "Example Managed IT Pty Ltd", "Firewall management", "--kind", "msp", "--for", "SUP-VENDOR");
        exit.ShouldBe(ExitCodes.Ok, error);

        // Three files, each a third party of its own that validates, with a draft contract and an open review.
        string[] names = ["sup-vendor", "sup-reseller", "sup-msp"];
        var files = names.Select(f => Path.Combine(Repo, "scope", "third-parties", $"{f}.yaml")).ToArray();
        var parsed = ScopeLoader.Parse(
            new ScopeSource("setup.yaml", await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "setup.yaml"), _ct)), null, [],
            thirdParties: [.. files.Select(f => new ScopeSource(f, File.ReadAllText(f)))]);
        parsed.Problems.ShouldBeEmpty();
        parsed.Scope.ThirdParties.Select(e => (e.RefId, e.Description)).ShouldBe(
        [
            ("SUP-VENDOR", null),
            ("SUP-RESELLER", "Reseller of Example Vendor Ltd (SUP-VENDOR)."),
            ("SUP-MSP", "Managed service provider for Example Vendor Ltd (SUP-VENDOR)."),
        ], ignoreOrder: true);
        parsed.Scope.ThirdParties.ShouldAllBe(e => e.Contracts.Single().Status == "draft" && e.Assessments.Count == 1);

        // The kept document is named with its hash, so a reviewer can tell it is the file the agent read.
        var reseller = await File.ReadAllTextAsync(files[1], _ct);
        reseller.ShouldContain($"# Kept document: documents/suppliers/example-reseller-terms.pdf (sha256 {Convert.ToHexStringLower(SHA256.HashData([0x25, 0x50, 0x44, 0x46]))})");

        // A reseller or managed service provider stands for a vendor already drafted; a kept document is a file in the repo.
        (exit, _, error) = await DraftAsync("SUP-OTHER", "Example Other Pty Ltd", "Resale", "--kind", "reseller");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("names the vendor with --for");
        (exit, _, error) = await DraftAsync("SUP-OTHER", "Example Other Pty Ltd", "Resale", "--kind", "msp", "--for", "SUP-NOBODY");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("SUP-NOBODY is not a third party");
        (exit, _, error) = await DraftAsync("SUP-OTHER", "Example Other Pty Ltd", "Resale", "--document", "documents/suppliers/missing.pdf");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("documents/suppliers/missing.pdf does not exist");
        File.Exists(Path.Combine(Repo, "scope", "third-parties", "sup-other.yaml")).ShouldBeFalse();
    }

    [Fact]
    public async Task Contract_terms_and_a_description_come_from_options_and_an_msp_may_run_the_organisations_own_systems()
    {
        // s10-03 rehearsal: a small shop's spreadsheet holds renewal dates and costs, and its MSP runs its own laptops for no one vendor.
        var (exit, _, error) = await DraftAsync(
            "SUP-EXITP", "Example IT Partners Pty Ltd", "Managed IT", "--kind", "msp", "--description", "Managed IT: laptops, patching and firewall changes.",
            "--start", "2026-07-01", "--end", "2027-06-30", "--annual-expense", "18000", "--currency", "AUD");

        exit.ShouldBe(ExitCodes.Ok, error);
        var file = Path.Combine(Repo, "scope", "third-parties", "sup-exitp.yaml");
        var parsed = ScopeLoader.Parse(
            new ScopeSource("setup.yaml", await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "setup.yaml"), _ct)), null, [],
            thirdParties: [new ScopeSource(file, await File.ReadAllTextAsync(file, _ct))]);
        parsed.Problems.ShouldBeEmpty();
        var entity = parsed.Scope.ThirdParties.Single();
        entity.Description.ShouldBe("Managed IT: laptops, patching and firewall changes.");
        var contract = entity.Contracts.Single();
        (contract.Status, contract.Start, contract.End, contract.AnnualExpense, contract.Currency).ShouldBe(("draft", "2026-07-01", "2027-06-30", "18000", "AUD"));

        // Dates are checked before anything is written.
        (exit, _, error) = await DraftAsync("SUP-EXLATE", "Example Late Pty Ltd", "Support", "--start", "2027-01-01", "--end", "2026-01-01");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("ends before it starts");
        (exit, _, error) = await DraftAsync("SUP-EXLATE", "Example Late Pty Ltd", "Support", "--end", "30 June 2027");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("yyyy-MM-dd");
        File.Exists(Path.Combine(Repo, "scope", "third-parties", "sup-exlate.yaml")).ShouldBeFalse();
    }

    [Fact]
    public async Task Ref_ids_get_their_prefix_and_one_that_does_not_fit_is_refused()
    {
        var (exit, _, error) = await DraftAsync("example-other", "Example Other Pty Ltd", "Resale");

        exit.ShouldBe(ExitCodes.Ok, error);
        var drafted = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "third-parties", "sup-example-other.yaml"), _ct);
        drafted.ShouldContain("ref_id: \"SUP-EXAMPLE-OTHER\"");
        drafted.ShouldContain("ref_id: \"SOL-EXAMPLE-OTHER\"");
        drafted.ShouldContain("ref_id: \"CTR-EXAMPLE-OTHER\"");

        (exit, _, error) = await DraftAsync("Example Another", "Example Another Pty Ltd", "Resale");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("'Example Another' is not a ref ID");
        error.ShouldContain("SUP-");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Repo => Path.Combine(_root, "repo");

    private Task<(int Exit, string Output, string Error)> DraftAsync(string entity, string name, string solution, params string[] more) => OnboardAgentCommandTests.RunAsync(
        [
            "supplier", "draft", "--repo", Repo, "--ref", entity, "--name", name, "--solution-ref", entity.Replace("SUP-", "SOL-", StringComparison.Ordinal), "--solution-name", solution,
            "--contract-ref", entity.Replace("SUP-", "CTR-", StringComparison.Ordinal), "--contract-name", $"{solution} agreement",
            "--level", "dependency=2:Supplies the firewall only.", "--json", .. more,
        ],
        "", terminal: false, Path.Combine(_root, "home"), _ct);
}
