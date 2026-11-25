namespace EvidenceHound.Core.Scope;

/// <summary>
/// The CMMC asset categories of 32 CFR 170.19(c)(1), Table 1 (s17-03). A system carries its category as the <c>cmmc_asset_category</c> tag in
/// <c>scope/setup.yaml</c>, because CISO Assistant's asset custom fields are an Enterprise feature. Pure.
/// </summary>
public static class CmmcAssetCategory
{
    public static readonly string Tag = "cmmc_asset_category";

    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["cui"] = "CUI asset",
        ["security_protection"] = "Security protection asset",
        ["contractor_risk_managed"] = "Contractor risk managed asset",
        ["specialized"] = "Specialized asset",
        ["out_of_scope"] = "Out-of-scope asset",
    };

    /// <summary>The tag's allowed values, in the regulation's order.</summary>
    public static IReadOnlyList<string> Values { get; } = [.. Names.Keys];

    /// <summary>The category's name in the regulation's terms, such as "CUI asset"; null for a value that is not a category.</summary>
    public static string? Name(string value) => Names.GetValueOrDefault(value);
}
