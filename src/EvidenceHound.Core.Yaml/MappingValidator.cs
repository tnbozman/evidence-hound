using System.Globalization;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Yaml;

/// <summary>Checks a loaded mapping against a framework snapshot, offline. Every problem names the file and line.</summary>
public static class MappingValidator
{
    public static IReadOnlyList<Problem> Validate(LoadedMapping loaded, FrameworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(snapshot);

        var mapping = loaded.Mapping;
        if (mapping.FrameworkUrn != snapshot.FrameworkUrn)
        {
            return [new Problem(
                "mapping.framework_mismatch",
                $"{loaded.Source}: the mapping is for {mapping.FrameworkUrn} but the snapshot is of {snapshot.FrameworkUrn}.")];
        }

        return [.. mapping.Requirements
            .Select((requirement, index) => Check(requirement.Key, snapshot))
            .Select((problem, index) => problem is null ? null : At(loaded, index, problem))
            .OfType<Problem>()];
    }

    private static Problem? Check(RequirementKey key, FrameworkSnapshot snapshot)
    {
        var nodes = snapshot.Resolve(key);
        var kind = key.Kind == RequirementKeyKind.Urn ? "urn" : "ref";
        return nodes.Count switch
        {
            0 => new Problem($"mapping.{kind}_unknown", $"requirement {kind} '{key.Value}' is not in framework {snapshot.FrameworkUrn}."),
            > 1 when !key.AllRows => new Problem(
                "mapping.ref_ambiguous",
                $"ref '{key.Value}' matches {nodes.Count} rows: {string.Join(", ", nodes.Select(n => n.Urn))}. "
                + "Name one row by 'urn', or add 'rows: all' to write the same result to every row."),
            _ => null,
        };
    }

    private static Problem At(LoadedMapping loaded, int index, Problem problem) =>
        new(problem.Code, index < loaded.Lines.Count
            ? $"{loaded.Source} line {loaded.Lines[index].ToString(CultureInfo.InvariantCulture)}: {problem.Message}"
            : $"{loaded.Source}: {problem.Message}");
}
