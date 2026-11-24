using EvidenceHound.Core.Checks;

namespace EvidenceHound.Core.Scope;

/// <summary>An AI system's entry in the register, with the system's name and owner from <c>scope/setup.yaml</c>.</summary>
public sealed record AiRegisterEntry(string System, string? Owner, AiRegistration Ai);

/// <summary>What one register check found, before it becomes a <see cref="CheckResult"/>.</summary>
public sealed record AiRegisterFinding(string CheckId, CheckOutcome Outcome, string Observation);

/// <summary>The AI register checks (s11-13): pure functions over the register entries and today's date.</summary>
public static class AiRegisterChecks
{
    public static IReadOnlyList<string> CheckIds { get; } = [];

    public static IReadOnlyList<AiRegisterEntry> Entries(ScopeSetup setup) => [];

    public static string Practice(string checkId) => "";

    public static IReadOnlyList<AiRegisterFinding> Assess(IEnumerable<AiRegisterEntry> entries, DateOnly today) => [];
}
