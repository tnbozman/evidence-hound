using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors;
using EvidenceHound.Collectors.Testing;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Runtime.Tests;

/// <summary>s11-13: the built-in <c>ai-register</c> collector checks the AI register entries in the compliance repo's scope files.</summary>
public sealed class AiRegisterCollectorTests : IDisposable
{
    internal const string Setup = """
        organisation:
          root_folder: Global
        domains:
          - name: Example Pty Ltd
            systems:
              - name: Example Chat
                type: supporting
                owner: it@example.com
                ai:
                  tier: low
                  screened_on: "2026-02-01"
                  use: customised
                  oversight: human_in_the_loop
                  oversight_note: Staff read every draft before sending.
                  impact_assessment: not_required
                  in_use_from: "2026-02-10"
                  last_tested_on: "2026-02-05"
              - name: Example laptops
                type: supporting
        """;

    private static readonly DateTimeOffset Now = new(2026, 11, 24, 2, 0, 0, TimeSpan.Zero);

    private readonly string _scope = Directory.CreateTempSubdirectory("hound-ai-register-").FullName;

    [Fact]
    public async Task Checks_the_register_and_saves_it_as_evidence()
    {
        await File.WriteAllTextAsync(Path.Combine(_scope, "setup.yaml"), Setup, TestContext.Current.CancellationToken);

        var output = (await new AiRegisterCollector().CollectAsync(Context(_scope), TestContext.Current.CancellationToken)).Value();

        output.Checks.Select(c => c.CheckId.Value).ShouldBe(AiRegisterCollector.Checks);
        Outcome(output, "ai.owner_assigned").ShouldBe(CheckOutcome.Pass);
        Outcome(output, "ai.impact_assessment_before_use").ShouldBe(CheckOutcome.NotApplicable);
        var evidence = output.Files.ShouldHaveSingleItem();
        evidence.FileName.ShouldBe("ai-register.json");
        output.Checks.ShouldAllBe(c => c.EvidenceRefs.Single() == "ai-register.json" && c.Source == "example-ai" && c.CollectedAt == Now);
        var json = JsonNode.Parse(Read(evidence))!;
        json["file"]!.GetValue<string>().ShouldBe("scope/setup.yaml");
        json["sha256"]!.GetValue<string>().Length.ShouldBe(64);
        var system = json["systems"]!.AsArray().ShouldHaveSingleItem()!;
        (system["name"]!.GetValue<string>(), system["tier"]!.GetValue<string>(), system["in_use"]!.GetValue<bool>()).ShouldBe(("Example Chat", "low", true));
    }

    [Fact]
    public async Task Unloadable_scope_is_an_error()
    {
        await File.WriteAllTextAsync(Path.Combine(_scope, "setup.yaml"), Setup.Replace("tier: low", "tier: extreme", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        var result = await new AiRegisterCollector().CollectAsync(Context(_scope), TestContext.Current.CancellationToken);

        var problem = result.ShouldBeOfType<Result<CollectionResult>.Fail>().Errors.ShouldHaveSingleItem();
        problem.Code.ShouldBe("ai_register.scope_invalid");
        problem.Message.ShouldContain("extreme");
    }

    [Fact]
    public async Task Missing_setup_file_is_an_error()
    {
        var result = await new AiRegisterCollector().CollectAsync(Context(_scope), TestContext.Current.CancellationToken);

        result.ShouldBeOfType<Result<CollectionResult>.Fail>().Errors.ShouldHaveSingleItem().Code.ShouldBe("ai_register.setup_missing");
    }

    public void Dispose() => Directory.Delete(_scope, recursive: true);

    private static CollectorContext Context(string scope) => new(
        "example-ai",
        new CollectorSettings(new Dictionary<string, string> { [AiRegisterCollector.ScopeSetting] = scope }),
        new Dictionary<string, Secret>(),
        new FixedClock(Now),
        NullLogger.Instance,
        new TestHttpClients());

    private static CheckOutcome Outcome(CollectionResult output, string check) => output.Checks.Single(c => c.CheckId.Value == check).Outcome;

    private static string Read(CollectedEvidence file)
    {
        using var stream = file.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

/// <summary>The built-in <c>ai-register</c> collector passes the collector contract.</summary>
public sealed class AiRegisterCollectorContractTests : CollectorContractTests<AiRegisterCollector>, IDisposable
{
    private readonly string _scope = Directory.CreateTempSubdirectory("hound-ai-register-contract-").FullName;

    public AiRegisterCollectorContractTests() => File.WriteAllText(Path.Combine(_scope, "setup.yaml"), AiRegisterCollectorTests.Setup);

    protected override CollectorScenario Working => new() { Settings = new Dictionary<string, string> { [AiRegisterCollector.ScopeSetting] = _scope } };

    public void Dispose() => Directory.Delete(_scope, recursive: true);

    protected override AiRegisterCollector CreateCollector() => new();
}
