namespace EvidenceHound.Core.Mapping;

/// <summary>How a source requirement relates to a target requirement, in CISO Assistant's mapping vocabulary.</summary>
public enum CrosswalkRelationship
{
    /// <summary>The two say the same thing.</summary>
    Equal,

    /// <summary>The source asks for more than the target: meeting it meets the target.</summary>
    Superset,

    /// <summary>The source asks for less than the target: it answers part of it.</summary>
    Subset,

    /// <summary>The two overlap.</summary>
    Intersect,
}

/// <summary>One link from a target requirement to a source requirement that may answer it.</summary>
public sealed record CrosswalkLink(string TargetRef, string SourceRef, CrosswalkRelationship Relationship);

/// <summary>A requirement of the target framework, by ref_id and title.</summary>
public sealed record CrosswalkTarget(string RefId, string Title);

/// <summary>A crosswalk between two frameworks: the target's requirements and the links to the source's, and where it came from.</summary>
public sealed record CrosswalkDefinition(string FromUrn, string ToUrn, IReadOnlyList<CrosswalkTarget> Targets, IReadOnlyList<CrosswalkLink> Links, string Origin);

/// <summary>What answers a source requirement today: its checks (from the mapping), which of them people attest, and its current result.</summary>
public sealed record SourceAnswer(string SourceRef, IReadOnlyList<string> Checks, IReadOnlyList<string> ManualChecks, ComplianceResult? Result);

/// <summary>Whether a target requirement is answered by today's evidence.</summary>
public enum CrosswalkStatus
{
    Answered,
    PartlyAnswered,
    Open,
}

/// <summary>A target requirement with its status and the source requirements behind it.</summary>
public sealed record CrosswalkRow(CrosswalkTarget Target, CrosswalkStatus Status, IReadOnlyList<(CrosswalkLink Link, SourceAnswer Answer)> Sources);

/// <summary>
/// s12-08: which target requirements the source framework's evidence already answers. Advisory: nothing is written anywhere. A target is
/// answered when a source that is equal to it or asks for more has checks; partly answered when only overlapping or narrower sources do,
/// or a linked source has no checks; open when nothing links to it.
/// </summary>
public static class Crosswalks
{
    public static IReadOnlyList<CrosswalkRow> Build(CrosswalkDefinition crosswalk, IReadOnlyDictionary<string, SourceAnswer> answers) =>
        throw new NotImplementedException();

    /// <summary>The crosswalk to use: a mapping library loaded in CISO Assistant first, then the reviewed file, else none.</summary>
    public static CrosswalkDefinition? Choose(CrosswalkDefinition? library, CrosswalkDefinition? file) => throw new NotImplementedException();
}
