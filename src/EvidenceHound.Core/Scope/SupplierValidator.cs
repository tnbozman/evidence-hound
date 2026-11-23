using System.Globalization;

namespace EvidenceHound.Core.Scope;

/// <summary>Checks the third parties a scope declares. Pure.</summary>
public static class SupplierValidator
{
    private static readonly (string Level, int Min, int Max)[] Ranges = [("dependency", 0, 4), ("penetration", 0, 4), ("maturity", 1, 4), ("trust", 1, 4)];

    /// <summary>
    /// The values of each third party: country and currency codes CISO Assistant accepts, levels in their ranges, contracts that end after they
    /// start and name only the entity's own solutions, and an <c>active</c> contract whose end date has passed on <paramref name="today"/>.
    /// </summary>
    public static IEnumerable<ScopeProblem> Validate(ScopeDocument scope, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return
        [
            .. scope.Setup.Organisation is { } organisation ? EntityProblems(organisation, today) : [],
            .. scope.ThirdParties.SelectMany(entity => EntityProblems(entity, today)),
        ];
    }

    /// <summary>Third parties declared once (entities, solutions and contracts by ref_id, representatives by email), and the parents and domains they name.</summary>
    public static IEnumerable<ScopeProblem> References(ScopeDocument scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var entities = scope.ThirdParties.Select(e => e.RefId).ToHashSet(StringComparer.Ordinal);
        var domains = scope.Setup.Domains.Select(d => d.Name).Append(scope.Setup.RootFolder).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. Repeated(scope.ThirdParties.Select(e => (e.RefId, e.At)), "third party"),
            .. Repeated(scope.ThirdParties.SelectMany(e => e.Solutions).Select(s => (s.RefId, s.At)), "solution"),
            .. Repeated(scope.ThirdParties.SelectMany(e => e.Contracts).Select(c => (c.RefId, c.At)), "contract"),
            .. Repeated(scope.ThirdParties.SelectMany(e => e.Representatives).Select(r => (r.Email, r.At)), "representative"),
            .. scope.ThirdParties.Select(e => e.Parent).OfType<ScopeRef>().Where(p => !entities.Contains(p.Value))
                .Select(p => Unknown(p, "third party", "a file of its own under scope/third-parties/")),
            .. scope.ThirdParties.Select(e => e.Domain).OfType<ScopeRef>().Where(d => !domains.Contains(d.Value))
                .Select(d => Unknown(d, "domain", "scope/setup.yaml")),
            .. Repeated(scope.ThirdParties.SelectMany(e => e.Assessments).Select(a => (a.RefId, a.At)), "supplier assessment"),
            .. scope.ThirdParties.SelectMany(e => e.Assessments.SelectMany(a => AssessmentReferences(scope, e, a))),
            .. SystemSuppliers(scope),
        ];
    }

    /// <summary>The perimeter, domain and questionnaire an assessment names: declared in <c>scope/setup.yaml</c>.</summary>
    private static IEnumerable<ScopeProblem> AssessmentReferences(ScopeDocument scope, ScopeEntity entity, ScopeEntityAssessment assessment)
    {
        var domainName = assessment.Domain?.Value ?? entity.Domain?.Value;
        if (domainName is null)
        {
            yield return new ScopeProblem(
                "supplier.assessment_domain_missing", assessment.At, $"assessment '{assessment.RefId}' has no domain, and {entity.RefId} declares none.",
                "Add 'domain' to the assessment or to the supplier.");
        }
        else if (scope.Setup.Domains.FirstOrDefault(d => d.Name == domainName) is not { } domain)
        {
            yield return Unknown(assessment.Domain ?? entity.Domain!, "domain", "scope/setup.yaml");
        }
        else if (domain.Perimeters.All(p => p.Name != assessment.Perimeter.Value))
        {
            yield return Unknown(assessment.Perimeter, $"perimeter of {domainName}", "scope/setup.yaml");
        }

        var frameworks = scope.Setup.Frameworks.Select(f => f.Urn).ToHashSet(StringComparer.Ordinal);
        if (assessment.Questionnaire is { } questionnaire && !frameworks.Contains(questionnaire.Value))
        {
            yield return Unknown(questionnaire, "questionnaire framework", "the frameworks of scope/setup.yaml");
        }
    }

    /// <summary>The solution a system names, and the supplier it comes through: declared under <c>scope/third-parties/</c>.</summary>
    private static IEnumerable<ScopeProblem> SystemSuppliers(ScopeDocument scope)
    {
        var solutions = scope.ThirdParties.SelectMany(e => e.Solutions).Select(s => s.RefId).ToHashSet(StringComparer.Ordinal);
        var entities = scope.ThirdParties.Select(e => e.RefId).ToHashSet(StringComparer.Ordinal);
        var systems = scope.Setup.Domains.SelectMany(d => d.Systems).ToArray();
        return
        [
            .. systems.Select(s => s.Solution).OfType<ScopeRef>().Where(r => !solutions.Contains(r.Value))
                .Select(r => Unknown(r, "solution", "a supplier's file under scope/third-parties/")),
            .. systems.Select(s => s.SuppliedThrough).OfType<ScopeRef>().Where(r => !entities.Contains(r.Value))
                .Select(r => Unknown(r, "third party", "a file of its own under scope/third-parties/")),
        ];
    }

    private static IEnumerable<ScopeProblem> EntityProblems(ScopeEntity entity, DateOnly today)
    {
        var solutions = entity.Solutions.Select(s => s.RefId).ToHashSet(StringComparer.Ordinal);
        int?[] levels = [entity.Dependency, entity.Penetration, entity.Maturity, entity.Trust];
        return
        [
            .. Country(entity.Country, entity.CodesAt.GetValueOrDefault("country") ?? entity.At),
            .. Currency(entity.Currency, entity.CodesAt.GetValueOrDefault("currency") ?? entity.At),
            .. Ranges.Zip(levels).Where(l => l.Second is { } value && (value < l.First.Min || value > l.First.Max))
                .Select(l => Level(l.First.Level, l.Second!.Value, l.First.Min, l.First.Max, entity.LevelsAt.GetValueOrDefault(l.First.Level) ?? entity.At)),
            .. entity.Solutions.Where(s => s.Criticality is < 1 or > 4).Select(s => Level($"criticality of solution {s.RefId}", s.Criticality!.Value, 1, 4, s.At)),
            .. entity.Contracts.SelectMany(c => ContractProblems(c, solutions, entity.RefId, today)),
            .. entity.ForcedTier is not null && string.IsNullOrWhiteSpace(entity.ForcedTierReason)
                ? [new ScopeProblem(
                    "supplier.forced_tier_reason_missing", entity.ForcedTierAt ?? entity.At,
                    $"{entity.RefId} forces tier '{entity.ForcedTier}' without a reason.", "Add 'reason' under forced_tier: why the organisation holds this supplier at that tier.")]
                : Array.Empty<ScopeProblem>(),
            .. entity.Assessments.SelectMany(a => a.Solutions.Where(s => !solutions.Contains(s.Value)).Select(s => new ScopeProblem(
                "supplier.solution_unknown", s.At,
                $"assessment '{a.RefId}' covers solution '{s.Value}', which is not one of {entity.RefId}'s solutions.",
                $"Declare the solution under {entity.RefId}'s solutions, or remove it from the assessment."))),
            .. entity.Assessments.Where(a => a.Criticality is < 1 or > 4).Select(a => Level($"criticality of assessment {a.RefId}", a.Criticality!.Value, 1, 4, a.At)),
            .. entity.Assessments.Where(a => a.ReviewMonths is not null && a.DueDate is null).Select(a => new ScopeProblem(
                "supplier.review_without_due_date", a.At, $"assessment '{a.RefId}' has review_every but no due_date.", "Add the first review's due_date.")),
            .. entity.Assessments.SelectMany(a => a.Conclusions.Where(c => !IsReviewOf(a, c.Review)).Select(c => new ScopeProblem(
                "supplier.conclusion_review_unknown", c.At,
                $"the conclusion names review '{c.Review}', which is not a review of assessment '{a.RefId}'.",
                a.ReviewMonths is null ? $"Name the review {a.RefId}." : $"Name a review as {a.RefId}-yyyy-MM, by the month it is due, or record it with 'hound approve'."))),
        ];
    }

    /// <summary>Whether <paramref name="review"/> is a review of the assessment: its own <c>ref_id</c>, or <c>&lt;ref_id&gt;-yyyy-MM</c> on a cycle.</summary>
    private static bool IsReviewOf(ScopeEntityAssessment assessment, string review) =>
        assessment.ReviewMonths is null
            ? review == assessment.RefId
            : review.StartsWith($"{assessment.RefId}-", StringComparison.Ordinal)
                && DateOnly.TryParseExact(review[(assessment.RefId.Length + 1)..], "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static IEnumerable<ScopeProblem> ContractProblems(ScopeContract contract, HashSet<string> solutions, string entity, DateOnly today)
    {
        var start = Day(contract.Start);
        var end = Day(contract.End);
        return
        [
            .. Currency(contract.Currency, contract.CurrencyAt ?? contract.At),
            .. start is { } from && end is { } to && to < from
                ? [new ScopeProblem(
                    "supplier.contract_dates_invalid", contract.EndAt ?? contract.At,
                    $"contract '{contract.RefId}' ends on {contract.End}, before it starts on {contract.Start}.", "Correct the start or end date.")]
                : Array.Empty<ScopeProblem>(),
            .. contract.Status == "active" && end is { } ended && ended < today
                ? [new ScopeProblem(
                    "supplier.contract_lapsed", contract.EndAt ?? contract.At,
                    $"contract '{contract.RefId}' is active but ended on {contract.End}.",
                    "Renew it and record the new end date, or set its status to expired or terminated.")]
                : Array.Empty<ScopeProblem>(),
            .. contract.Solutions.Where(s => !solutions.Contains(s.Value)).Select(s => new ScopeProblem(
                "supplier.solution_unknown", s.At,
                $"contract '{contract.RefId}' covers solution '{s.Value}', which is not one of {entity}'s solutions.",
                $"Declare the solution under {entity}'s solutions, or remove it from the contract.")),
        ];
    }

    private static IEnumerable<ScopeProblem> Country(string? code, SourceLine at) =>
        code is null || IsoCodes.Countries.Contains(code)
            ? []
            : [new ScopeProblem("supplier.country_invalid", at, $"'{code}' is not a country code CISO Assistant accepts.", "Use the ISO 3166-1 alpha-2 code, such as AU or NZ.")];

    private static IEnumerable<ScopeProblem> Currency(string? code, SourceLine at) =>
        code is null || IsoCodes.Currencies.Contains(code)
            ? []
            : [new ScopeProblem("supplier.currency_invalid", at, $"'{code}' is not a currency code CISO Assistant accepts.", "Use the ISO 4217 code, such as AUD or USD.")];

    private static ScopeProblem Level(string level, int value, int min, int max, SourceLine at) =>
        new("supplier.level_invalid", at, $"{level} is {value}; it must be from {min} to {max}.", $"Use a whole number from {min} to {max}.");

    private static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private static IEnumerable<ScopeProblem> Repeated(IEnumerable<(string Key, SourceLine At)> items, string kind) =>
        items.GroupBy(i => i.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1).Select(i => new ScopeProblem(
            "scope.duplicate", i.At, $"{kind} '{g.Key}' is declared more than once; first at {g.First().At.File} line {g.First().At.Line}.",
            $"Give each {kind} its own ref_id, or remove the copy.")));

    private static ScopeProblem Unknown(ScopeRef reference, string kind, string where) => new(
        "scope.reference_unknown", reference.At, $"{kind} '{reference.Value}' is not declared.", $"Declare it in {where}, or correct the name.");
}
