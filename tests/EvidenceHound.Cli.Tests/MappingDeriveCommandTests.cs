using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Yaml;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary>s14-01: <c>hound mapping derive</c> drafts the ASD Essentials mapping from the Essential Eight one and the transition mapping.</summary>
public sealed class MappingDeriveCommandTests : IDisposable
{
    private static readonly string LegacyAuth = "urn:intuitem:risk:req_node:asd-essentials-enterprise-it:identity-services:pro-13:it.pro-13.4";

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("hound-derive-").FullName;

    private static string Repo(params string[] parts) => Path.Combine([ExampleRepoTests.RepoRoot, .. parts]);

    [Fact]
    public async Task Writes_a_draft_essentials_mapping_from_the_essential_eight_mapping()
    {
        var output = Path.Combine(_dir, "essentials.yaml");

        var exitCode = await Run(
            "mapping", "derive", Repo("mappings", "essential-eight-2023.yaml"),
            "--from", Repo("frameworks", "essential-eight-2023.json"),
            "--to", Repo("frameworks", "asd-essentials-enterprise-it.json"),
            "--links", Repo("frameworks", "mapping-essential-eight-2023-and-asd-essentials-enterprise-it.yaml"),
            "--groups", "1",
            "--out", output);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var loaded = MappingLoader.Parse(await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken), output).Match(l => l, p => throw new InvalidOperationException(p[0].Message));
        loaded.Mapping.FrameworkUrn.ShouldBe("urn:intuitem:risk:framework:asd-essentials-enterprise-it");
        loaded.Mapping.Requirements.ShouldAllBe(r => r.Draft && r.Key.Kind == RequirementKeyKind.Urn);
        var mfa = loaded.Mapping.Requirements.Single(r => r.RequirementRef == LegacyAuth);
        mfa.Checks.Select(c => c.Value).ShouldContain("identity.mfa.enforced_conditional_access");
        _output.ToString().ShouldContain("draft");
    }

    [Fact]
    public async Task Without_out_the_draft_is_printed()
    {
        var exitCode = await Run(
            "mapping", "derive", Repo("mappings", "essential-eight-2023.yaml"),
            "--from", Repo("frameworks", "essential-eight-2023.json"),
            "--to", Repo("frameworks", "asd-essentials-enterprise-it.json"),
            "--links", Repo("frameworks", "mapping-essential-eight-2023-and-asd-essentials-enterprise-it.yaml"));

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldContain("framework_urn: urn:intuitem:risk:framework:asd-essentials-enterprise-it");
        _output.ToString().ShouldContain("draft: true");
    }

    [Fact]
    public async Task An_existing_out_file_is_kept_unless_force_is_given()
    {
        // s14-03 rehearsal: deriving over the reviewed reference mapping silently replaced it with an all-draft file.
        var output = Path.Combine(_dir, "asd-essentials-enterprise-it.yaml");
        await File.WriteAllTextAsync(output, "reviewed", TestContext.Current.CancellationToken);
        string[] derive =
        [
            "mapping", "derive", Repo("mappings", "essential-eight-2023.yaml"),
            "--from", Repo("frameworks", "essential-eight-2023.json"),
            "--to", Repo("frameworks", "asd-essentials-enterprise-it.json"),
            "--links", Repo("frameworks", "mapping-essential-eight-2023-and-asd-essentials-enterprise-it.yaml"),
            "--out", output,
        ];

        var refused = await Run(derive);

        refused.ShouldBe(ExitCodes.Usage);
        (await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken)).ShouldBe("reviewed");
        _logs.Messages.ShouldContain(m => m.Contains("--force", StringComparison.Ordinal));

        var forced = await Run([.. derive, "--force"]);

        forced.ShouldBe(ExitCodes.Ok, _error.ToString());
        (await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken)).ShouldContain("draft: true");
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private Task<int> Run(params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs));
}
