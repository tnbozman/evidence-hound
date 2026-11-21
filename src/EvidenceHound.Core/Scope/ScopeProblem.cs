using System.Globalization;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>A problem in the scope: a stable code, the file and line, what is wrong and how to fix it.</summary>
public sealed record ScopeProblem(string Code, SourceLine At, string Message, string Hint)
{
    public Problem ToProblem() => new(Code, $"{At.File} line {At.Line.ToString(CultureInfo.InvariantCulture)}: {Message}");
}
