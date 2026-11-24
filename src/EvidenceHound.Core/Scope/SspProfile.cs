namespace EvidenceHound.Core.Scope;

/// <summary>The layouts a system security plan can take, the roles every plan names, and the hand-written sections each layout needs.</summary>
public static class SspProfile
{
    public static readonly string Ism = "ism";
    public static readonly string Nist800171 = "nist-800-171";

    public static IReadOnlyList<string> Names { get; } = [Ism, Nist800171];

    /// <summary>The roles every plan names, in the order a plan lists them.</summary>
    public static IReadOnlyList<string> Roles { get; } = ["system_owner", "authorising_official", "security_contact"];

    /// <summary>
    /// The parts of the plan that cannot be generated from records, so a person writes them as Markdown files: SP 800-18 Rev 1's description
    /// and purpose, system environment, applicable laws and policies, and plan maintenance (how the system is managed). The same for every
    /// profile, as the layout is one (s11-04).
    /// </summary>
    public static IReadOnlyList<string> RequiredSections(string profile) => Sections;

    private static readonly string[] Sections = ["purpose", "environment", "management", "laws_and_policies"];
}
