using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Publishing;

/// <summary>
/// The narrow port the publisher talks to. Adapters turn HTTP (or anything else) into these calls;
/// expected failures come back as problems with codes <c>ciso_assistant.auth</c>, <c>.not_found</c>,
/// <c>.rejected</c>, <c>.ambiguous</c> and <c>.unavailable</c>.
/// </summary>
public interface ICisoAssistantClient
{
    Task<Result<Unit>> CheckHealthAsync(CancellationToken cancellationToken);

    /// <summary>The ID of the one requirement assessment for this requirement in this audit.</summary>
    Task<Result<string>> FindRequirementAssessmentAsync(string complianceAssessmentRefId, string requirementRefId, CancellationToken cancellationToken);

    Task<Result<Unit>> UpdateRequirementAssessmentAsync(string requirementAssessmentId, RequirementAssessmentValues values, CancellationToken cancellationToken);

    /// <summary>The evidence with this ref_id, created with <paramref name="name"/> when there is none.</summary>
    Task<Result<RemoteEvidence>> FindOrCreateEvidenceAsync(string refId, string name, CancellationToken cancellationToken);

    /// <summary>Adds a new revision holding <paramref name="file"/> to the evidence.</summary>
    Task<Result<Unit>> UploadEvidenceRevisionAsync(string evidenceId, EvidenceFile file, CancellationToken cancellationToken);

    /// <summary>Adds the evidence to the evidences of the one applied control with this ref_id, if it is not there already.</summary>
    Task<Result<Unit>> LinkEvidenceToAppliedControlAsync(string controlRefId, string evidenceId, CancellationToken cancellationToken);

    /// <summary>Sets the status of the one applied control with this ref_id.</summary>
    Task<Result<Unit>> UpdateAppliedControlAsync(string refId, ControlState status, CancellationToken cancellationToken);
}

/// <summary>What a requirement assessment update writes. <paramref name="EvidenceIds"/> are CISO Assistant evidence IDs, not ref_ids.</summary>
public sealed record RequirementAssessmentValues(ComplianceResult Result, string Observation, IReadOnlyList<string> EvidenceIds);

/// <summary>An evidence object in CISO Assistant and the SHA-256 of its latest revision's file, if it has one.</summary>
public sealed record RemoteEvidence(string Id, string? LatestSha256);

/// <summary>A file to upload as an evidence revision.</summary>
public sealed record EvidenceFile(string FileName, ReadOnlyMemory<byte> Content);
