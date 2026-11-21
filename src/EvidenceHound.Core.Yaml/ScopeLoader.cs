using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml;

/// <summary>One scope file: its path as shown in problems, and its text.</summary>
public sealed record ScopeSource(string Path, string Text);

/// <summary>What parsing found: the scope as far as it could be read, and every problem in every file.</summary>
public sealed record ScopeParse(ScopeDocument Scope, IReadOnlyList<ScopeProblem> Problems);

/// <summary>Parses a compliance repo's scope files (see <c>schemas/scope.schema.json</c>). Every problem names the file and line.</summary>
public static class ScopeLoader
{
    public static ScopeParse Parse(ScopeSource setup, ScopeSource? controls, IReadOnlyList<ScopeSource> audits) =>
        throw new NotImplementedException($"{setup}{controls}{audits}");
}
