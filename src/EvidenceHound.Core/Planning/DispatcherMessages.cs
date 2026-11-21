using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Planning;

/// <summary>Writes an <see cref="AuditUpdate"/> as a CISO Assistant dispatcher message (<c>message_type</c>, <c>selector</c>, <c>values</c>).</summary>
public static class DispatcherMessages
{
    /// <param name="update">The update.</param>
    /// <param name="fileContent">Supplies the base64 file content for an evidence upload. The publisher reads the file; a dry run can describe it.</param>
    public static JsonObject ToJson(AuditUpdate update, Func<Evidence, string> fileContent)
    {
        ArgumentNullException.ThrowIfNull(fileContent);

        return update switch
        {
            AuditUpdate.UploadEvidence upload => Message(
                "upload_attachment",
                new JsonObject { ["name"] = upload.Evidence.FileName, ["ref_id"] = upload.Evidence.Id },
                new JsonObject { ["file_content"] = fileContent(upload.Evidence), ["file_name"] = upload.Evidence.FileName }),
            AuditUpdate.UpdateRequirementAssessment requirement => Message(
                "update_requirement_assessment",
                new JsonObject
                {
                    ["compliance_assessment__ref_id"] = requirement.ComplianceAssessmentRefId,
                    ["requirement__ref_id"] = requirement.RequirementRefId,
                },
                new JsonObject
                {
                    ["result"] = Names.Of(requirement.Result),
                    ["observation"] = requirement.Observation,
                    ["evidences"] = new JsonArray([.. requirement.EvidenceRefIds.Select(id => JsonValue.Create(id))]),
                }),
            AuditUpdate.UpdateAppliedControl control => Message(
                "update_applied_control",
                new JsonObject { ["ref_id"] = control.RefId },
                new JsonObject { ["status"] = StateName(control.Status) }),
            _ => throw new ArgumentOutOfRangeException(nameof(update), update, "Unknown audit update."),
        };
    }

    private static JsonObject Message(string type, JsonObject selector, JsonObject values) =>
        new() { ["message_type"] = type, ["selector"] = selector, ["values"] = values };

    private static string StateName(ControlState state) => state switch
    {
        ControlState.ToDo => "to_do",
        ControlState.InProgress => "in_progress",
        ControlState.OnHold => "on_hold",
        ControlState.Active => "active",
        ControlState.Deprecated => "deprecated",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
