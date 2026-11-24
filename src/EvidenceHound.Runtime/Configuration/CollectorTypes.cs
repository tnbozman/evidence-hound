namespace EvidenceHound.Runtime.Configuration;

/// <summary>The type names of the collectors this build ships.</summary>
public static class CollectorTypes
{
    /// <summary>Fixed, fictional results for tests, demos and dry runs.</summary>
    public const string Fake = "fake";

    /// <summary>The AI register entries in the compliance repo's scope files (s11-13).</summary>
    public const string AiRegister = "ai-register";
}
