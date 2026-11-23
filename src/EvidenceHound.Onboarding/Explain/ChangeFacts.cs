using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Guard;

namespace EvidenceHound.Onboarding.Explain;

/// <summary>What a change touches, which decides the skills that guide its review.</summary>
public enum ChangeTopic
{
    ThirdParty,
    NotApplicable,
    Profile,
    Mapping,
    Audit,
}

public sealed record PlanCount(int Value, string Label);

/// <summary>A plan or diff read in code: its counts, the objects it names, what needs a careful look, and the text to show the model.</summary>
public sealed record ChangeFacts(string Source, IReadOnlyList<PlanCount> Counts, IReadOnlyList<string> Objects, IReadOnlyList<string> Flags, IReadOnlyList<ChangeTopic> Topics, string Text)
{
    public string CountsText => throw new NotImplementedException();
}

public static class ChangeReader
{
    public static Result<ChangeFacts> Read(string text, string source) => throw new NotImplementedException();

    public static string Diff(string before, string after, string name) => throw new NotImplementedException();

    public static string Redact(string text, GuardTerms terms) => throw new NotImplementedException();
}
