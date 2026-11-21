using System.Collections.Concurrent;
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

    public Task<Result<Unit>> CheckHealthAsync(CancellationToken cancellationToken) => Done("health");

    public Task<Result<string>> FindRequirementAssessmentAsync(string complianceAssessmentRefId, string requirementRefId, CancellationToken cancellationToken)
    {
        Calls.Add($"find {requirementRefId}");
        return Task.FromResult(MissingRequirements.Contains(requirementRefId)
            ? Result.Fail<string>(new Problem("ciso_assistant.not_found", $"No requirement assessment {requirementRefId}."))
            : Result.Ok($"ra-{requirementRefId}"));
    }

    public Task<Result<Unit>> UpdateRequirementAssessmentAsync(string requirementAssessmentId, RequirementAssessmentValues values, CancellationToken cancellationToken)
    {
        Requirements[requirementAssessmentId] = values;
        return Done($"update {requirementAssessmentId}");
    }

    public Task<Result<RemoteEvidence>> FindOrCreateEvidenceAsync(string refId, string name, CancellationToken cancellationToken)
    {
        Calls.Add($"find-or-create {refId}");
        return Task.FromResult(Result.Ok(_evidenceByRefId.GetOrAdd(refId, id => new RemoteEvidence($"uuid-{id}", null))));
    }

    public Task<Result<Unit>> UploadEvidenceRevisionAsync(string evidenceId, EvidenceFile file, CancellationToken cancellationToken)
    {
        var refId = _evidenceByRefId.Single(e => e.Value.Id == evidenceId).Key;
        _evidenceByRefId[refId] = new RemoteEvidence(evidenceId, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(file.Content.Span)));
        return Done($"upload {evidenceId} {file.FileName}");
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
