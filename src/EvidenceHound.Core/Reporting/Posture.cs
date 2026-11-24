using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Planning;

namespace EvidenceHound.Core.Reporting;

/// <summary>s12-04: builds a programme's posture from its framework snapshot and the audit's rows, and counts results per group and parent.</summary>
public static class Posture
{
    /// <summary>The programme's posture: only rows in its selected groups, each with its parent from the framework snapshot.</summary>
    public static ProgrammePosture Programme(
        string id, string name, FrameworkSnapshot framework, string audit, IReadOnlyList<string> groups, IReadOnlyList<CurrentRow> rows) =>
        throw new NotImplementedException();

    /// <summary>Per selected group: one count over the group, then one per parent node in <paramref name="parents"/> order.</summary>
    public static IReadOnlyList<ResultCount> Count(IReadOnlyList<RequirementPosture> rows, IReadOnlyList<string> groups, IReadOnlyList<ParentNode> parents) =>
        throw new NotImplementedException();
}
