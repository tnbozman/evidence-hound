namespace EvidenceHound.Core.Publishing;

/// <summary>Finds CISO Assistant objects by field values, with the same fields as the dispatcher's selectors.</summary>
public sealed record Selector
{
    public static Selector ByRefId(string refId) => throw new NotImplementedException();

    public static Selector ByName(string name) => throw new NotImplementedException();

    public static Selector RequirementInAudit(string complianceAssessmentRefId, string requirementRefId) => throw new NotImplementedException();

    public string ToQueryString() => throw new NotImplementedException();
}
