using System.Security.Cryptography;
using System.Text;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Planning;

namespace EvidenceHound.Runtime.Status;

/// <summary>A collector's health evidence: the file holding its latest run record, and the upload that publishes it.</summary>
public sealed record HealthEvidence(CollectedFile File, AuditUpdate.UploadEvidence Upload);

/// <summary>Plans the "collector health" evidence published after every run. Pure.</summary>
public static class Health
{
    /// <summary>Evidence <c>hound-health/&lt;collector&gt;</c> holding the run record as JSON, linked to <paramref name="controlRefId"/> when one is configured.</summary>
    public static HealthEvidence Plan(RunRecord record, string? controlRefId)
    {
        ArgumentNullException.ThrowIfNull(record);
        var content = Encoding.UTF8.GetBytes(RunRecords.ToJson(record));
        var file = new CollectedFile($"collector-health-{record.Collector}.json", "application/json", content);
        var evidence = Evidence.Create(
            $"hound-health/{record.Collector}", file.FileName, file.MediaType, Convert.ToHexStringLower(SHA256.HashData(content)), content.Length, record.EndedAt)
            .Match(e => e, problems => throw new InvalidOperationException(problems[0].Message));
        return new HealthEvidence(file, new AuditUpdate.UploadEvidence(evidence) { AppliedControlRefIds = controlRefId is null ? [] : [controlRefId] });
    }
}
