using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Scope;

/// <summary>A programme's shape, from its framework snapshot alone: the assessable requirements in each selected group, by parent node.</summary>
public sealed record ProgrammeOutline(ScopeProgramme Programme, int Version, IReadOnlyList<GroupOutline> Groups)
{
    public static ProgrammeOutline Of(ScopeProgramme programme, FrameworkSnapshot snapshot) => throw new NotImplementedException();
}

/// <summary>One implementation group of a programme: its assessable requirement count, and the same per parent node.</summary>
public sealed record GroupOutline(string RefId, string Name, int Requirements, IReadOnlyList<ParentOutline> Parents);

/// <summary>A parent node (an Essential Eight strategy, an AI6 practice) and how many of its requirements are in the group. A null URN is the top level.</summary>
public sealed record ParentOutline(string? Urn, string Name, int Requirements);
