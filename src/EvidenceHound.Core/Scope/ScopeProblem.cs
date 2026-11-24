using System.Globalization;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// A problem in the scope: a stable code, the file and line, what is wrong and how to fix it. A <see cref="Warning"/> is a finding about the
/// organisation, not the files (such as sensitive data held where nothing approves it): it is reported and reviewed, and blocks nothing.
/// </summary>
public sealed record ScopeProblem(string Code, SourceLine At, string Message, string Hint)
{
    public bool Warning { get; init; }

    public Problem ToProblem() => new(Code, $"{At.File} line {At.Line.ToString(CultureInfo.InvariantCulture)}: {Message}");
}
