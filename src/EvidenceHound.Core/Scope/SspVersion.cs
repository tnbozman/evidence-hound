using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>What a plan was built from: the model, with every value and its source, and the hand-written sections' text (s11-05).</summary>
public sealed record SspSnapshot(SspDocument Document, IReadOnlyDictionary<string, string> Sections);

/// <summary>Draft hashes and signing (s11-05). Pure.</summary>
public static class SspVersion
{
    public static string Hash(string text) => throw new NotImplementedException();

    public static Result<string> Sign(SspSnapshot snapshot, SspSignOff signOff) => throw new NotImplementedException();
}

/// <summary>The snapshot file: the plan's model and section texts as JSON, so a signed version can be rebuilt without CISO Assistant.</summary>
public static class SspSnapshotJson
{
    public static string Write(SspSnapshot snapshot) => throw new NotImplementedException();

    public static Result<SspSnapshot> Read(string json, string source) => throw new NotImplementedException();
}
