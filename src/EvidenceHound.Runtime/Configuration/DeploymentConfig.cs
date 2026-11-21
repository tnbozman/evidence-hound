using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Runtime.Configuration;

/// <summary>One deployment's configuration, validated. Paths are absolute, resolved against the config file's folder.</summary>
public sealed record DeploymentConfig(
    CisoAssistantConfig CisoAssistant,
    EvidenceConfig Evidence,
    IReadOnlyList<CollectorInstance> Collectors,
    TargetConfig Target)
{
    /// <summary>Every audit results are published to, in config order. A single <c>target</c> is a list of one.</summary>
    public IReadOnlyList<TargetConfig> Targets => throw new NotImplementedException();
}

/// <summary>How to reach CISO Assistant. <paramref name="Transport"/> is <c>rest</c> or <c>kafka</c>.</summary>
public sealed record CisoAssistantConfig(Uri Url, SecretReference Token, string Transport, string? KafkaBootstrapServers);

/// <summary>Where evidence is stored, and for how many days old runs are kept (null keeps everything).</summary>
public sealed record EvidenceConfig(string Root, int? RetentionDays);

/// <summary>One configured collector: a unique name, a known type, a cron schedule, plain settings and secret references.</summary>
public sealed record CollectorInstance(
    string Name,
    string Type,
    string Schedule,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, SecretReference> Secrets);

/// <summary>
/// The audit that results are published to: the mapping file, the audit's ref_id, control key to applied control ref_id, and the
/// applied control that each collector's health evidence is linked to.
/// </summary>
public sealed record TargetConfig(string MappingPath, string ComplianceAssessmentRefId, IReadOnlyDictionary<string, string> Controls, string? HealthControlRefId = null);
