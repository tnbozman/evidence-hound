using System.Collections.Concurrent;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Tests.Publishing;

/// <summary>A stateful fake CISO Assistant: evidence keeps its latest hash, so re-publishing behaves like the real one.</summary>
internal sealed class InMemoryCisoAssistant : ICisoAssistantClient
{
    private readonly ConcurrentDictionary<string, RemoteEvidence> _evidenceByRefId = new();

    public List<string> Calls { get; } = [];

    public Dictionary<string, RequirementAssessmentValues> Requirements { get; } = [];

    public Dictionary<string, ControlState> Controls { get; } = [];

    public HashSet<string> MissingRequirements { get; } = [];

    public HashSet<string> UnreachableEvidence { get; } = [];

    /// <summary>The name each evidence was created with, by ref_id.</summary>
    public Dictionary<string, string> EvidenceNames { get; } = [];

    public Task<Result<Unit>> CheckHealthAsync(CancellationToken cancellationToken) => Done("health");

    /// <summary>Requirement rows of every audit. A requirement with no row here has one row, <c>ra-&lt;ref or urn&gt;</c>, in every group.</summary>
    public List<RemoteRequirementAssessment> Rows { get; } = [];

    /// <summary>The selected implementation groups of every audit; empty selects all.</summary>
    public List<string> SelectedGroups { get; } = [];

    public Task<Result<IReadOnlyList<RemoteRequirementAssessment>>> FindRequirementAssessmentsAsync(
        string complianceAssessmentRefId, RequirementKey requirement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        Calls.Add($"find {requirement.Value}");
        if (MissingRequirements.Contains(requirement.Value))
        {
            return Task.FromResult(Result.Fail<IReadOnlyList<RemoteRequirementAssessment>>(new Problem("ciso_assistant.not_found", $"No requirement assessment {requirement.Value}.")));
        }

        var rows = Rows.Where(r => requirement.Kind == RequirementKeyKind.Urn ? r.Urn == requirement.Value : r.RefId == requirement.Value).ToList();
        return Task.FromResult(Result.Ok<IReadOnlyList<RemoteRequirementAssessment>>(rows.Count > 0
            ? rows
            : [new RemoteRequirementAssessment($"ra-{requirement.Value}", $"urn:example:req:{requirement.Value}", requirement.Value, [])]));
    }

    public Task<Result<string>> FindRequirementAssessmentAsync(string complianceAssessmentRefId, string requirementRefId, CancellationToken cancellationToken)
    {
        Calls.Add($"find {requirementRefId}");
        return Task.FromResult(MissingRequirements.Contains(requirementRefId)
            ? Result.Fail<string>(new Problem("ciso_assistant.not_found", $"No requirement assessment {requirementRefId}."))
            : Result.Ok($"ra-{requirementRefId}"));
    }

    public Task<Result<RemoteAudit>> FindAuditAsync(string complianceAssessmentRefId, CancellationToken cancellationToken)
    {
        Calls.Add($"audit {complianceAssessmentRefId}");
        return Task.FromResult(Result.Ok(new RemoteAudit($"ca-{complianceAssessmentRefId}", [.. SelectedGroups])));
    }

    public Task<Result<Unit>> UpdateRequirementAssessmentAsync(string requirementAssessmentId, RequirementAssessmentValues values, CancellationToken cancellationToken)
    {
        Requirements[requirementAssessmentId] = values;
        return Done($"update {requirementAssessmentId}");
    }

    public Task<Result<RemoteEvidence>> FindOrCreateEvidenceAsync(string refId, string name, CancellationToken cancellationToken)
    {
        Calls.Add($"find-or-create {refId}");
        EvidenceNames.TryAdd(refId, name);
        return Task.FromResult(UnreachableEvidence.Contains(refId)
            ? Result.Fail<RemoteEvidence>(new Problem("ciso_assistant.unavailable", "CISO Assistant could not be reached."))
            : Result.Ok(_evidenceByRefId.GetOrAdd(refId, id => new RemoteEvidence($"uuid-{id}", null))));
    }

    public Task<Result<Unit>> UploadEvidenceRevisionAsync(string evidenceId, EvidenceFile file, CancellationToken cancellationToken)
    {
        var refId = _evidenceByRefId.Single(e => e.Value.Id == evidenceId).Key;
        _evidenceByRefId[refId] = new RemoteEvidence(evidenceId, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(file.Content.Span)));
        return Done($"upload {evidenceId} {file.FileName}");
    }

    public Dictionary<string, List<string>> ControlEvidence { get; } = [];

    public HashSet<string> MissingControls { get; } = [];

    public Task<Result<Unit>> LinkEvidenceToAppliedControlAsync(string controlRefId, string evidenceId, CancellationToken cancellationToken)
    {
        if (MissingControls.Contains(controlRefId))
        {
            return Task.FromResult(Result.Fail<Unit>(new Problem("ciso_assistant.not_found", $"No applied control {controlRefId}.")));
        }

        var linked = ControlEvidence.TryGetValue(controlRefId, out var list) ? list : ControlEvidence[controlRefId] = [];
        linked.Add(evidenceId);
        return Done($"link {evidenceId} to {controlRefId}");
    }

    public Task<Result<Unit>> UpdateAppliedControlAsync(string refId, ControlState status, CancellationToken cancellationToken)
    {
        Controls[refId] = status;
        return Done($"control {refId}");
    }

    private Task<Result<Unit>> Done(string call)
    {
        Calls.Add(call);
        return Task.FromResult(Result.Ok(Unit.Value));
    }
}

/// <summary>Evidence file contents by evidence ID.</summary>
internal sealed class InMemoryEvidenceFiles(Dictionary<string, byte[]> files) : IEvidenceFiles
{
    public Task<Result<ReadOnlyMemory<byte>>> ReadAsync(Core.Checks.Evidence evidence, CancellationToken cancellationToken) =>
        Task.FromResult(files.TryGetValue(evidence.Id, out var content)
            ? Result.Ok<ReadOnlyMemory<byte>>(content)
            : Result.Fail<ReadOnlyMemory<byte>>(new Problem("evidence.file_not_found", $"No file for {evidence.Id}.")));
}
