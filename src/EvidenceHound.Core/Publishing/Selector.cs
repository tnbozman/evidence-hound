namespace EvidenceHound.Core.Publishing;

/// <summary>Finds CISO Assistant objects by field values, with the same fields as the dispatcher's selectors.</summary>
public sealed class Selector
{
    private readonly KeyValuePair<string, string>[] _fields;

    private Selector(params KeyValuePair<string, string>[] fields) => _fields = fields;

    public static Selector ByRefId(string refId) => new(KeyValuePair.Create("ref_id", refId));

    public static Selector ByName(string name) => new(KeyValuePair.Create("name", name));

    public static Selector RequirementInAudit(string complianceAssessmentRefId, string requirementRefId) =>
        new(KeyValuePair.Create("compliance_assessment__ref_id", complianceAssessmentRefId), KeyValuePair.Create("requirement__ref_id", requirementRefId));

    /// <summary>The fields as a URL query string without the leading <c>?</c>, in selector order.</summary>
    public string ToQueryString() =>
        string.Join("&", _fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));

    public override string ToString() => string.Join(", ", _fields.Select(f => $"{f.Key}={f.Value}"));
}
