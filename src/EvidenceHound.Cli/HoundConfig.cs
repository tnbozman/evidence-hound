namespace EvidenceHound.Cli;

/// <summary>The parts of a deployment config the walking skeleton understands. s03 replaces this with the full schema.</summary>
internal sealed record HoundConfig(IReadOnlyList<string> CollectorNames, TargetConfig? Target = null);

/// <summary>The audit that results are published to. <paramref name="MappingPath"/> is absolute, resolved against the config file's folder.</summary>
internal sealed record TargetConfig(string MappingPath, string ComplianceAssessmentRefId, IReadOnlyDictionary<string, string> Controls);

/// <summary>Outcome of loading a config file: either a config or a reason it could not be loaded.</summary>
internal abstract record ConfigLoad
{
    private ConfigLoad()
    {
    }

    public sealed record Loaded(HoundConfig Config) : ConfigLoad;

    public sealed record Failed(string Reason) : ConfigLoad;
}
