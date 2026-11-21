using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Manual;

/// <summary>
/// An owner's statement about one dropped file, kept next to it as <c>&lt;file&gt;.attest.yaml</c>: who attested, when, the
/// outcome for each check, and a note.
/// </summary>
public sealed record Attestation(string Who, DateTimeOffset Date, IReadOnlyDictionary<string, CheckOutcome> Outcomes, string? Note)
{
    public const string Suffix = ".attest.yaml";

    /// <summary>The sidecar file name for <paramref name="fileName"/>.</summary>
    public static string SidecarFor(string fileName) => fileName + Suffix;

    public static Result<Attestation> Parse(string yaml, string source) => throw new NotImplementedException();

    public string ToYaml() => throw new NotImplementedException();
}
