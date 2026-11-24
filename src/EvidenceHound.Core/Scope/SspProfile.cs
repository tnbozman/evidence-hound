namespace EvidenceHound.Core.Scope;

/// <summary>The layouts a system security plan can take, the roles every plan names, and the hand-written sections each layout needs.</summary>
public static class SspProfile
{
    public static readonly string Ism = "ism";
    public static readonly string Nist800171 = "nist-800-171";

    public static IReadOnlyList<string> Names { get; } = [Ism, Nist800171];

    /// <summary>The roles every plan names, in the order a plan lists them.</summary>
    public static IReadOnlyList<string> Roles { get; } = ["system_owner", "authorising_official", "security_contact"];

    /// <summary>The parts of the plan that cannot be generated from records, so a person writes them as Markdown files.</summary>
    public static IReadOnlyList<string> RequiredSections(string profile) => ["environment", "physical", "personnel"];
}
