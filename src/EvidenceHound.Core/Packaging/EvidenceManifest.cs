namespace EvidenceHound.Core.Packaging;

/// <summary>A file a collector captured, before it is stored. <paramref name="FileName"/> is a plain file name.</summary>
public sealed record CollectedFile(string FileName, string MediaType, ReadOnlyMemory<byte> Content);

/// <summary>One stored file in a run's manifest.</summary>
public sealed record ManifestEntry(string FileName, long SizeBytes, string Sha256, string MediaType, DateTimeOffset CollectedAt);

/// <summary>What one collector run stored, file by file in file name order. Written last, as <c>_manifest.json</c>, so a run with a manifest is complete.</summary>
public sealed record EvidenceManifest(string Collector, string CollectorVersion, DateTimeOffset CollectedAt, IReadOnlyList<ManifestEntry> Files);
