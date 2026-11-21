using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;

namespace EvidenceHound.Core.Publishing;

/// <summary>One-line descriptions of audit updates for reports and logs.</summary>
public static class UpdateDescriptions
{
    public static string Describe(AuditUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return update.Match(
            upload => $"upload evidence {upload.Evidence.Id} ({upload.Evidence.FileName})",
            requirement => $"set requirement {requirement.RequirementRefId} in {requirement.ComplianceAssessmentRefId} to {Names.Of(requirement.Result)}",
            control => $"set applied control {control.RefId} to {Names.Of(control.Status)}");
    }
}
