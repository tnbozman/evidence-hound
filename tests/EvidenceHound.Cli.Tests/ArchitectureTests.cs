using System.Reflection;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// No language model in evidence collection: only <c>hound onboard</c> reaches the onboarding code and the model SDKs. The core, the runtime,
/// the collectors and the CISO Assistant client never reference them, and the commands that collect, publish and run never use them.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly string[] Forbidden = ["EvidenceHound.Onboarding", "Anthropic", "OpenAI"];

    private static readonly string[] Collection =
    [
        "EvidenceHound.Core", "EvidenceHound.Core.Yaml", "EvidenceHound.Runtime", "EvidenceHound.CisoAssistant", "EvidenceHound.Collectors.Abstractions",
        "EvidenceHound.Collectors.Rest", "EvidenceHound.Collectors.M365", "EvidenceHound.Collectors.Manual", "EvidenceHound.Transport.Kafka",
    ];

    private static readonly string[] CollectionCommands = ["RunCommand.cs", "ServeCommand.cs", "CollectCommand.cs", "PublishCommand.cs", "PlanCommand.cs"];

    [Fact]
    public void Runner_and_collectors_do_not_reference_onboarding_or_model_sdks()
    {
        foreach (var name in Collection)
        {
            var references = Assembly.Load(name).GetReferencedAssemblies().Select(r => r.Name!).ToArray();
            references.ShouldNotContain(r => Forbidden.Any(f => r == f || r.StartsWith(f + ".", StringComparison.Ordinal)), $"{name} must not reach a language model");
        }

        Assembly.Load("EvidenceHound.Onboarding").GetReferencedAssemblies().Select(r => r.Name).ShouldContain("Anthropic");
        foreach (var file in CollectionCommands)
        {
            File.ReadAllText(Path.Combine(ExampleRepoTests.RepoRoot, "src", "EvidenceHound.Cli", file)).ShouldNotContain("Onboarding", customMessage: file);
        }
    }
}
