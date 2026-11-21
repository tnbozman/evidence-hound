using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Publishing;

/// <summary>Which of an audit's rows for a requirement get its result. Pure.</summary>
public static class RequirementRows
{
    /// <summary>The note on an update whose rows are all outside the audit's selected implementation groups.</summary>
    public const string OutsideSelectedGroups = "outside selected groups";

    /// <summary>
    /// The rows to write: those in the audit's selected groups (all, when it selects none), as CISO Assistant decides it.
    /// A ref matching several rows fails with <c>mapping.ref_ambiguous</c> unless the key says <c>rows: all</c>. Empty when every row is outside the groups.
    /// </summary>
    public static Result<IReadOnlyList<RemoteRequirementAssessment>> InScope(
        RequirementKey key, IReadOnlyList<RemoteRequirementAssessment> rows, IReadOnlyList<string> selectedGroups)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(selectedGroups);

        if (rows.Count > 1 && !key.AllRows)
        {
            return Result.Fail<IReadOnlyList<RemoteRequirementAssessment>>(new Problem(
                "mapping.ref_ambiguous",
                $"Requirement ref '{key.Value}' matches {rows.Count} rows: {string.Join(", ", rows.Select(r => r.Urn))}. "
                + "Name one row by 'urn' in the mapping, or add 'rows: all' to write the same result to every row."));
        }

        return Result.Ok<IReadOnlyList<RemoteRequirementAssessment>>([.. rows.Where(row => InGroups(row.Groups, selectedGroups))]);
    }

    /// <summary>CISO Assistant's rule: no selection takes everything; otherwise a requirement needs a selected group, and one with none is out.</summary>
    public static bool InGroups(IReadOnlyList<string> requirementGroups, IReadOnlyList<string> selectedGroups)
    {
        ArgumentNullException.ThrowIfNull(requirementGroups);
        ArgumentNullException.ThrowIfNull(selectedGroups);
        return selectedGroups.Count == 0 || requirementGroups.Any(selectedGroups.Contains);
    }
}
