using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Planning;

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

/// <summary>Who checked every link of a crosswalk, and when.</summary>
public sealed record CrosswalkReview(string By, DateOnly On);

/// <summary>A crosswalk between two frameworks: the target's requirements and the links to the source's, and where it came from.</summary>
public sealed record CrosswalkDefinition(string FromUrn, string ToUrn, IReadOnlyList<CrosswalkTarget> Targets, IReadOnlyList<CrosswalkLink> Links, string Origin)
{
    /// <summary>The review of every link, or null while nobody has reviewed it (the crosswalk is then advisory only).</summary>
    public CrosswalkReview? Review { get; init; }
}

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
    public static IReadOnlyList<CrosswalkRow> Build(CrosswalkDefinition crosswalk, IReadOnlyDictionary<string, SourceAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(crosswalk);
        ArgumentNullException.ThrowIfNull(answers);
        var listed = crosswalk.Targets.Select(t => t.RefId).ToHashSet(StringComparer.Ordinal);
        var targets = crosswalk.Targets
            .Concat(crosswalk.Links.Select(l => l.TargetRef).Where(r => !listed.Contains(r)).Distinct(StringComparer.Ordinal).Select(r => new CrosswalkTarget(r, "")));
        return [.. targets.Select(target => Row(target, crosswalk.Links.Where(l => l.TargetRef == target.RefId), answers))];
    }

    /// <summary>What answers each source requirement today, by ref_id: the mapping's checks and the worst current result of its audit rows.</summary>
    public static IReadOnlyDictionary<string, SourceAnswer> Answers(
        FrameworkMapping mapping, FrameworkSnapshot snapshot, IReadOnlySet<CheckId> manualChecks, IReadOnlyList<CurrentRow> rows)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(manualChecks);
        ArgumentNullException.ThrowIfNull(rows);
        var checks = mapping.Requirements
            .SelectMany(r => RefsOf(r.Key, snapshot).Select(refId => (RefId: refId, r.Checks)))
            .GroupBy(r => r.RefId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.SelectMany(r => r.Checks).Distinct().ToList(), StringComparer.Ordinal);
        var results = rows
            .Where(r => r.RefId is not null)
            .GroupBy(r => r.RefId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Result).MinBy(Severity), StringComparer.Ordinal);
        return checks.Keys.Union(results.Keys, StringComparer.Ordinal).ToDictionary(
            refId => refId,
            refId =>
            {
                var mapped = checks.GetValueOrDefault(refId) ?? [];
                return new SourceAnswer(
                    refId,
                    [.. mapped.Select(c => c.Value)],
                    [.. mapped.Where(manualChecks.Contains).Select(c => c.Value)],
                    results.TryGetValue(refId, out var result) ? result : null);
            },
            StringComparer.Ordinal);
    }

    /// <summary>Worst first: a gap, then an unknown, then a partial answer.</summary>
    private static int Severity(ComplianceResult result) => result switch
    {
        ComplianceResult.NonCompliant => 0,
        ComplianceResult.NotAssessed => 1,
        ComplianceResult.PartiallyCompliant => 2,
        ComplianceResult.Compliant => 3,
        _ => 4,
    };

    private static IEnumerable<string> RefsOf(RequirementKey key, FrameworkSnapshot snapshot) =>
        key.Kind == RequirementKeyKind.Ref
            ? [key.Value]
            : snapshot.Resolve(key).Select(n => n.RefId).OfType<string>();

    /// <summary>The crosswalk to use: a mapping library loaded in CISO Assistant first, then the reviewed file, else none.</summary>
    public static CrosswalkDefinition? Choose(CrosswalkDefinition? library, CrosswalkDefinition? file) => library ?? file;

    private static CrosswalkRow Row(CrosswalkTarget target, IEnumerable<CrosswalkLink> links, IReadOnlyDictionary<string, SourceAnswer> answers)
    {
        var sources = links
            .Select(l => (Link: l, Answer: answers.GetValueOrDefault(l.SourceRef) ?? new SourceAnswer(l.SourceRef, [], [], null)))
            .ToList();
        var status = sources.Count == 0
            ? CrosswalkStatus.Open
            : sources.Any(s => s.Link.Relationship is CrosswalkRelationship.Equal or CrosswalkRelationship.Superset && s.Answer.Checks.Count > 0)
                ? CrosswalkStatus.Answered
                : CrosswalkStatus.PartlyAnswered;
        return new CrosswalkRow(target, status, sources);
    }
}
