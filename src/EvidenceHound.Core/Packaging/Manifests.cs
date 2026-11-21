using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Packaging;

/// <summary>Builds and serialises evidence manifests. Pure.</summary>
public static class Manifests
{
    /// <summary>The manifest's file name in a run folder.</summary>
    public const string FileName = "_manifest.json";

    public static Result<EvidenceManifest> Build(string collector, string collectorVersion, DateTimeOffset collectedAt, IReadOnlyList<CollectedFile> files) =>
        throw new NotImplementedException();

    /// <summary>Indented JSON with snake_case keys; the same manifest always gives the same bytes.</summary>
    public static string ToJson(EvidenceManifest manifest) => throw new NotImplementedException();
}
