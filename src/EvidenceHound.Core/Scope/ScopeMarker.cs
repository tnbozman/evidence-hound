namespace EvidenceHound.Core.Scope;

/// <summary>The marker <c>hound</c> appends to the description of an object it manages, recording the state it wrote. Pure.</summary>
public static class ScopeMarker
{
    /// <summary>A fingerprint of the fields, in any order: 16 hex characters of a SHA-256.</summary>
    public static string Fingerprint(IReadOnlyDictionary<string, string> fields) => throw new NotImplementedException();

    /// <summary>The description as written to CISO Assistant: the text, then the marker on its own line.</summary>
    public static string Mark(string? description, string fingerprint) => throw new NotImplementedException();

    /// <summary>A description read from CISO Assistant split into its text (null when empty) and its marker's fingerprint (null when none).</summary>
    public static (string? Text, string? Fingerprint) Read(string? description) => throw new NotImplementedException();
}
