using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Planning;

/// <summary>One change to send to CISO Assistant. The cases match the dispatcher message types.</summary>
public abstract record AuditUpdate
{
    private AuditUpdate()
    {
    }

    /// <summary>Calls the function for the case this update is.</summary>
    public abstract T Match<T>(
        Func<UploadEvidence, T> upload,
        Func<UpdateRequirementAssessment, T> requirement,
        Func<UpdateAppliedControl, T> control);

    /// <summary><c>upload_attachment</c>: create or update the evidence with ref_id <c>Evidence.Id</c> and attach the file.</summary>
    public sealed record UploadEvidence(Evidence Evidence) : AuditUpdate
    {
        /// <summary>ref_ids of applied controls the evidence is linked to after upload.</summary>
        public IReadOnlyList<string> AppliedControlRefIds { get; init; } = [];

        /// <summary>s18-D3: the evidence's CISO Assistant ID when the state read found it, so the upload does not search for it again.</summary>
        public string? KnownId { get; init; }

        public override T Match<T>(Func<UploadEvidence, T> upload, Func<UpdateRequirementAssessment, T> requirement, Func<UpdateAppliedControl, T> control)
        {
            ArgumentNullException.ThrowIfNull(upload);
            return upload(this);
        }
    }

    /// <summary><c>update_requirement_assessment</c>: set the result and observation of one requirement in one audit.</summary>
    /// <param name="ComplianceAssessmentRefId">The audit's ref_id.</param>
    /// <param name="RequirementRefId">The requirement's ref_id in the framework.</param>
    /// <param name="Result">The requirement result.</param>
    /// <param name="Observation">Text shown on the requirement assessment.</param>
    /// <param name="EvidenceRefIds">ref_ids of evidence uploaded earlier in the same plan.</param>
    public sealed record UpdateRequirementAssessment(
        string ComplianceAssessmentRefId,
        string RequirementRefId,
        ComplianceResult Result,
        string Observation,
        IReadOnlyList<string> EvidenceRefIds) : AuditUpdate
    {
        /// <summary>How the requirement is named. Defaults to <see cref="RequirementRefId"/> as a ref.</summary>
        public RequirementKey Requirement { get; init; } = RequirementKey.Ref(RequirementRefId);

        /// <summary>The CISO Assistant IDs of the rows to write. Empty: the publisher finds the rows by <see cref="Requirement"/>.</summary>
        public IReadOnlyList<string> RowIds { get; init; } = [];

        /// <summary>CISO Assistant IDs of cited evidence that is already there and is not uploaded in this plan, by ref_id.</summary>
        public IReadOnlyDictionary<string, string> KnownEvidenceIds { get; init; } = new Dictionary<string, string>();

        /// <summary>By row ID: the CISO Assistant IDs of the scope's documents a row links, which the update keeps alongside the cited evidence.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> KeptEvidenceIds { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

        public override T Match<T>(Func<UploadEvidence, T> upload, Func<UpdateRequirementAssessment, T> requirement, Func<UpdateAppliedControl, T> control)
        {
            ArgumentNullException.ThrowIfNull(requirement);
            return requirement(this);
        }
    }

    /// <summary><c>update_applied_control</c>: set an applied control's status.</summary>
    public sealed record UpdateAppliedControl(string RefId, ControlState Status) : AuditUpdate
    {
        public override T Match<T>(Func<UploadEvidence, T> upload, Func<UpdateRequirementAssessment, T> requirement, Func<UpdateAppliedControl, T> control)
        {
            ArgumentNullException.ThrowIfNull(control);
            return control(this);
        }
    }
}
