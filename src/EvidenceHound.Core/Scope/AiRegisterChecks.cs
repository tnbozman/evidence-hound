using System.Globalization;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Core.Scope;

/// <summary>An AI system's entry in the register, with the system's name and owner from <c>scope/setup.yaml</c>.</summary>
public sealed record AiRegisterEntry(string System, string? Owner, AiRegistration Ai);

/// <summary>What one register check found, before it becomes a <see cref="CheckResult"/>.</summary>
public sealed record AiRegisterFinding(string CheckId, CheckOutcome Outcome, string Observation)
{
    /// <summary>The AI systems the check fails, by name.</summary>
    public IReadOnlyList<string> Failing { get; init; } = [];
}

/// <summary>
/// The AI register checks (s11-13): pure functions over the register entries in the scope files and today's date. Each observation
/// names the AI6 practice in words and says what to do next.
/// </summary>
public static class AiRegisterChecks
{
    private static readonly string Setup = "scope/setup.yaml";

    private static readonly (string CheckId, string Practice, Func<IReadOnlyList<AiRegisterEntry>, DateOnly, Outcome> Assess)[] Checks =
    [
        ("ai.register_complete", "AI6-4.1 Maintain an AI register", (all, _) => Over(
            all,
            "AI system",
            e => e.Ai.ImpactAssessment is null,
            "has its impact assessment status recorded",
            $"Add 'impact_assessment' to the ai block of each in {Setup} (not_required for a low tier system).")),
        ("ai.owner_assigned", "AI6-1.1 Accountable people", (all, _) => Over(
            all,
            "AI system",
            e => string.IsNullOrWhiteSpace(e.Owner),
            "has an owner",
            $"Add 'owner' to each system in {Setup}.")),
        ("ai.screening_recorded", "AI6-3.2 Assess AI system risks", (all, today) => Over(
            InUse(all, today),
            "AI system in use",
            e => e.Ai.Tier == AiRiskTier.Prohibited || e.Ai.ScreenedOn > e.Ai.InUseFrom,
            "was screened before use and is not prohibited",
            "Stop using a prohibited system and record 'retired_on'; screen a system before it goes into use.",
            e => e.Ai.Tier == AiRiskTier.Prohibited ? $"{e.System} (prohibited)" : $"{e.System} (screened {Day(e.Ai.ScreenedOn)}, after going into use)")),
        ("ai.impact_assessment_before_use", "AI6-3.2 Assess AI system risks", (all, today) => Over(
            [.. InUse(all, today).Where(e => e.Ai.Tier is AiRiskTier.Medium or AiRiskTier.High)],
            "medium or high tier AI system in use",
            e => e.Ai.ImpactAssessment != AiImpactAssessment.Approved || e.Ai.ImpactAssessedOn is not { } on || on > e.Ai.InUseFrom,
            "had its impact assessment approved before going into use",
            $"Approve the impact assessment, then set 'impact_assessment: approved' and 'impact_assessed_on' in {Setup}.",
            e => e.Ai.ImpactAssessedOn is { } on && e.Ai.ImpactAssessment == AiImpactAssessment.Approved
                ? $"{e.System} (approved {Day(on)}, after going into use on {Day(e.Ai.InUseFrom!.Value)})"
                : $"{e.System} (impact assessment {Words(e.Ai.ImpactAssessment)})")),
        ("ai.human_oversight_documented", "AI6-6.1 Maintain human oversight and control", (all, today) => Over(
            InUse(all, today),
            "AI system in use",
            e => e.Ai.Tier == AiRiskTier.High && e.Ai.Oversight == AiOversight.HumanOutOfLoop,
            "has a person in or on the loop where its tier needs one",
            "Put a person in or on the loop of each high tier system, and describe it in 'oversight_note'.",
            e => $"{e.System} (high tier, human out of the loop)")),
        ("ai.tested_before_use", "AI6-5.1 Pre-deployment testing", (all, today) => Over(
            InUse(all, today),
            "AI system in use",
            e => e.Ai.LastTestedOn is null,
            "has been tested",
            $"Test each, keep the result as evidence, and set 'last_tested_on' in {Setup}.")),
        ("ai.test_results_current", "AI6-5.3 Conduct additional testing proportionate to risk", (all, today) => Over(
            InUse(all, today),
            "AI system in use",
            e => e.Ai.LastTestedOn is not { } tested || tested < today.AddMonths(-TestMonths(e.Ai.Tier)),
            "was tested within 6 months (high tier) or 12 months (otherwise)",
            $"Test each again, keep the result as evidence, and update 'last_tested_on' in {Setup}.",
            e => e.Ai.LastTestedOn is { } tested ? $"{e.System} (last tested {Day(tested)})" : $"{e.System} (never tested)")),
    ];

    public static IReadOnlyList<string> CheckIds { get; } = [.. Checks.Select(c => c.CheckId)];

    /// <summary>The systems with an <c>ai</c> block, in file order.</summary>
    public static IReadOnlyList<AiRegisterEntry> Entries(ScopeSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return [.. setup.Domains.SelectMany(d => d.Systems).Where(s => s.Ai is not null).Select(s => new AiRegisterEntry(s.Name, s.Owner, s.Ai!))];
    }

    /// <summary>The AI6 sub-practice a check serves, as its ref and name, such as <c>AI6-1.1 Accountable people</c>.</summary>
    public static string Practice(string checkId) => Checks.Single(c => c.CheckId == checkId).Practice;

    public static IReadOnlyList<AiRegisterFinding> Assess(IEnumerable<AiRegisterEntry> entries, DateOnly today)
    {
        IReadOnlyList<AiRegisterEntry> all = [.. entries];
        return [.. Checks.Select(c => Finding(c.CheckId, c.Practice, c.Assess(all, today)))];
    }

    /// <summary>Whether the system is in use today: gone live, and not retired.</summary>
    public static bool IsInUse(AiRegistration ai, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(ai);
        return ai.InUseFrom is { } from && from <= today && !(ai.RetiredOn is { } retired && retired <= today);
    }

    private static AiRegisterFinding Finding(string checkId, string practice, Outcome outcome) =>
        new(checkId, outcome.Result, $"{practice}: {outcome.Text}");

    private static AiRegisterEntry[] InUse(IReadOnlyList<AiRegisterEntry> all, DateOnly today) => [.. all.Where(e => IsInUse(e.Ai, today))];

    private static Outcome Over(
        IReadOnlyList<AiRegisterEntry> covered,
        string noun,
        Func<AiRegisterEntry, bool> fails,
        string passes,
        string fix,
        Func<AiRegisterEntry, string>? describe = null)
    {
        if (covered.Count == 0)
        {
            return new(CheckOutcome.NotApplicable, $"no {noun} is registered.");
        }

        var failing = covered.Where(fails).Select(describe ?? (e => e.System)).ToArray();
        return failing.Length == 0
            ? new(CheckOutcome.Pass, covered.Count == 1 ? $"the one {noun} {passes}." : $"each of the {covered.Count} {Plural(noun, covered.Count)} {passes}.")
            : new(CheckOutcome.Fail, $"{failing.Length} of {covered.Count} {Plural(noun, covered.Count)} fail: {string.Join("; ", failing)}. {fix}");
    }

    private static string Plural(string noun, int count) =>
        count == 1 ? noun : noun.Replace("AI system", "AI systems", StringComparison.Ordinal);

    private static int TestMonths(AiRiskTier tier) => tier == AiRiskTier.High ? 6 : 12;

    private static string Words(AiImpactAssessment? status) =>
        status is { } s ? AiRegister.Code(s).Replace('-', ' ') : "not recorded";

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Outcome(CheckOutcome Result, string Text);
}
