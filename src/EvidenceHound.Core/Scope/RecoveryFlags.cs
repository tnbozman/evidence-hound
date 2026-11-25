using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Scope;

/// <summary>An asset assessment as CISO Assistant holds it: its id and its recovery flags by field name.</summary>
public sealed record HeldRecovery(string Id, IReadOnlyDictionary<string, bool> Flags)
{
    /// <summary>The evidence already linked to it, by id.</summary>
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
}

/// <summary>One recovery flag to set on an asset assessment, the evidence of the checks behind it, and why.</summary>
public sealed record RecoveryFlagUpdate(string AssessmentKey, string AssessmentId, string Field, bool Value, IReadOnlyList<string> EvidenceRefs, string Why);

/// <summary>Recovery flags set by each run from the checks a BIA names for them (s15-05). Pure.</summary>
public static class RecoveryFlags
{
    /// <summary>
    /// Each flag named by checks whose value differs from the one CISO Assistant holds: true only when every named check's latest result
    /// passes. An assessment CISO Assistant does not hold yet waits for <c>hound scope apply</c> to create it.
    /// </summary>
    public static IReadOnlyList<RecoveryFlagUpdate> Plan(IReadOnlyList<ScopeBia> bias, IReadOnlyList<CheckResult> latest, IReadOnlyDictionary<string, HeldRecovery> held)
    {
        ArgumentNullException.ThrowIfNull(bias);
        ArgumentNullException.ThrowIfNull(latest);
        ArgumentNullException.ThrowIfNull(held);
        var results = latest.GroupBy(r => r.CheckId.Value, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.MaxBy(r => r.CollectedAt)!, StringComparer.Ordinal);
        return
        [
            .. from bia in bias
               from assessment in bia.Assessments.DistinctBy(a => a.System.Value)
               let key = BiaObjects.AssessmentKey(bia.RefId, assessment.System.Value)
               where held.ContainsKey(key)
               from flag in assessment.Checks.OrderBy(c => c.Key, StringComparer.Ordinal)
               let update = Update(key, held[key], flag.Key, [.. flag.Value.Select(c => c.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], results)
               where update is not null
               select update,
        ];
    }

    private static RecoveryFlagUpdate? Update(string key, HeldRecovery held, string flag, IReadOnlyList<string> checks, Dictionary<string, CheckResult> results)
    {
        var field = "recovery_" + flag;
        var found = checks.Select(c => (Check: c, Result: results.GetValueOrDefault(c))).ToList();
        var value = found.All(f => f.Result?.Outcome == CheckOutcome.Pass);
        if (held.Flags.TryGetValue(field, out var current) && current == value)
        {
            return null;
        }

        var why = value
            ? $"Every named check passed: {string.Join(", ", checks)}."
            : $"Not every named check passed: {string.Join(", ", found.Where(f => f.Result?.Outcome != CheckOutcome.Pass).Select(f => $"{f.Check}: {(f.Result is { } r ? Names.Of(r.Outcome) : "no result")}"))}.";
        var evidence = found.SelectMany(f => f.Result?.EvidenceRefs ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new RecoveryFlagUpdate(key, held.Id, field, value, evidence, why);
    }
}
