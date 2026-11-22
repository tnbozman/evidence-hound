using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>A check's result and its observation.</summary>
public sealed record Finding(CheckOutcome Outcome, string Observation);

/// <summary>
/// Assess phase: one declarative check over the rows of one dataset or query. Whenever the engine cannot be sure (an input failed, a
/// required join left rows unmatched, a field was missing or of the wrong type) the result is unable to assess, never fail or pass.
/// </summary>
public static class CheckEvaluator
{
    /// <summary>How many offending rows an observation names.</summary>
    public const int ListedRows = 20;

    /// <summary>
    /// Evaluates a check over the sources it names: a declarative check reads its one source; a code check gets every source in the
    /// order of its <c>over</c>. <paramref name="fileOf"/> names each source's evidence file, cited in the observation.
    /// </summary>
    public static Finding Evaluate(
        Check check, IReadOnlyDictionary<string, RowSource> sources, Renderer renderer, DateTimeOffset now, Func<string, string?>? fileOf = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(sources);
        fileOf ??= _ => null;
        var inputs = check.Over.Select(name => sources.GetValueOrDefault(name) ?? new RowSource.Failed(name, "It was not read.")).ToArray();
        if (check.Code is null)
        {
            return Evaluate(check, inputs[0], renderer, now, fileOf(inputs[0].Name));
        }

        var files = inputs.Select(i => fileOf(i.Name)).OfType<string>().ToArray();
        return inputs.FirstOrDefault(i => i is not RowSource.Rows { Unmatched: 0 }) is { } blocked
            ? Evaluate(check with { Code = null }, blocked, renderer, now, fileOf(blocked.Name))
            : CodeChecks.All.TryGetValue(check.Code, out var code)
                ? Cite(code(new CodeCheckInput([.. inputs.Cast<RowSource.Rows>().Select(r => r.Items)], With(check, renderer), now)), files)
                : Unable($"code check '{check.Code}' is not built into this version of hound.");
    }

    public static Finding Evaluate(Check check, RowSource source, Renderer renderer, DateTimeOffset now, string? evidenceFile = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(renderer);
        return source switch
        {
            RowSource.Failed failed => Unable($"'{failed.Name}' could not be read. {failed.Reason}"),
            RowSource.Unavailable unavailable => new(CheckOutcome.NotApplicable, $"Not applicable: '{unavailable.Name}' is not available. {unavailable.Reason}"),
            RowSource.Rows { Unmatched: > 0 } rows => Unable(Cite($"{Rows(rows.Unmatched)} of '{rows.Name}' matched nothing in a required join.", evidenceFile)),
            RowSource.Rows rows => Assess(check, rows, renderer, now, evidenceFile),
            _ => Unable($"'{source.Name}' gave nothing to assess."),
        };
    }

    private static Finding Assess(Check check, RowSource.Rows rows, Renderer renderer, DateTimeOffset now, string? evidenceFile)
    {
        var scope = new List<(JsonNode? Row, int Index)>();
        for (var i = 0; i < rows.Items.Count; i++)
        {
            var tests = check.Where.Select(t => FieldTester.Test(t, rows.Items[i], renderer, now)).ToArray();
            if (tests.OfType<TestOutcome.Unknown>().FirstOrDefault() is { } unknown)
            {
                return Unable(Cite($"its filter could not be applied to {Label(rows.Items[i], i)}: {unknown.Reason}.", evidenceFile));
            }

            if (tests.All(t => t == TestOutcome.Pass))
            {
                scope.Add((rows.Items[i], i));
            }
        }

        var counting = check.Require is RequireCountAtMost or RequireCountAtLeast;
        if (counting ? rows.Items.Count == 0 : scope.Count == 0)
        {
            return Empty(check, rows, evidenceFile);
        }

        var results = scope.Select(s => (s.Row, s.Index, Outcome: check.Each is { } each ? FieldTester.Test(each, s.Row, renderer, now) : TestOutcome.Pass)).ToArray();
        var unknowns = results.Where(r => r.Outcome is TestOutcome.Unknown).ToArray();
        if (unknowns.Length > 0)
        {
            var reason = ((TestOutcome.Unknown)unknowns[0].Outcome).Reason;
            return Unable(Cite($"{reason} on {unknowns.Length} of {scope.Count} rows: {List(unknowns.Select(u => Label(u.Row, u.Index)))}.", evidenceFile));
        }

        var passing = results.Where(r => r.Outcome == TestOutcome.Pass).ToArray();
        var failing = results.Where(r => r.Outcome == TestOutcome.Fail).ToArray();
        var counts = new Counts(passing.Length, failing.Length, rows.Items.Count, scope.Count);
        var counted = check.Each is null ? results : passing;
        var (outcome, offending, heading) = check.Require switch
        {
            RequireAll => (Outcome(failing.Length == 0), failing, "Failing"),
            RequireAny => (Outcome(passing.Length > 0), [], ""),
            RequireNone => (Outcome(passing.Length == 0), passing, "Matching"),
            RequireCountAtMost most => (Count(most.Count, renderer) is { } max ? Outcome(counted.Length <= max) : CheckOutcome.Error, counted, "In scope"),
            RequireCountAtLeast least => (Count(least.Count, renderer) is { } min ? Outcome(counted.Length >= min) : CheckOutcome.Error, [], ""),
            RequireRatio ratio => (Ratio(ratio, passing.Length, scope.Count), failing, "Failing"),
            _ => (CheckOutcome.Error, [], ""),
        };
        if (outcome == CheckOutcome.Error)
        {
            return Unable("its requirement could not be read with this instance's parameters.");
        }

        var listed = outcome == CheckOutcome.Pass || offending.Length == 0 ? "" : $" {heading}: {List(offending.Select(o => Label(o.Row, o.Index)))}.";
        return new(outcome, Cite(Describe(check, rows, counts, renderer) + listed, evidenceFile));
    }

    private static CheckOutcome Outcome(bool passed) => passed ? CheckOutcome.Pass : CheckOutcome.Fail;

    private static CheckOutcome Ratio(RequireRatio ratio, int passed, int total)
    {
        var share = (double)passed / total;
        return share >= ratio.Pass ? CheckOutcome.Pass : ratio.PartialAt is { } partial && share >= partial ? CheckOutcome.Partial : CheckOutcome.Fail;
    }

    private static Finding Empty(Check check, RowSource.Rows rows, string? evidenceFile)
    {
        var what = rows.Items.Count == 0 ? $"'{rows.Name}' has no rows." : $"No rows of '{rows.Name}' are in scope ({rows.Items.Count} read).";
        return check.WhenEmpty switch
        {
            EmptyResult.NotApplicable => new(CheckOutcome.NotApplicable, Cite($"Not applicable: {what}", evidenceFile)),
            EmptyResult.Fail => new(CheckOutcome.Fail, Cite(what, evidenceFile)),
            _ => Unable(Cite(what, evidenceFile)),
        };
    }

    private static int? Count(string text, Renderer renderer) =>
        renderer.Render(text, mode: RenderMode.Evidence).Match(v => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null, _ => null);

    private static string Describe(Check check, RowSource.Rows rows, Counts counts, Renderer renderer)
    {
        var template = check.Describe ?? (check.Each is null
            ? $"{{matched}} of {{total}} rows of '{rows.Name}' are in scope."
            : $"{{passed}} of {{matched}} rows of '{rows.Name}' pass.");
        var text = renderer.Render(template, mode: RenderMode.Evidence).Match(v => v, _ => template);
        return text
            .Replace("{passed}", Number(counts.Passed), StringComparison.Ordinal)
            .Replace("{failed}", Number(counts.Failed), StringComparison.Ordinal)
            .Replace("{total}", Number(counts.Total), StringComparison.Ordinal)
            .Replace("{matched}", Number(counts.Matched), StringComparison.Ordinal);
    }

    /// <summary>A row's ID or name when it has one, else its position.</summary>
    private static string Label(JsonNode? row, int index) =>
        (row as JsonObject)?["id"] is JsonValue id ? Renderer.Text(id)
        : (row as JsonObject)?["name"] is JsonValue name ? Renderer.Text(name)
        : $"row {Number(index + 1)}";

    /// <summary>"a, b, c" with at most <see cref="ListedRows"/> names and how many more.</summary>
    private static string List(IEnumerable<string> labels)
    {
        var all = labels.ToArray();
        var shown = string.Join(", ", all.Take(ListedRows));
        return all.Length > ListedRows ? $"{shown} and {Number(all.Length - ListedRows)} more" : shown;
    }

    private static string Rows(int count) => count == 1 ? "1 row" : $"{Number(count)} rows";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Cite(string observation, string? evidenceFile) => evidenceFile is null ? observation : $"{observation} Evidence: {evidenceFile}.";

    private static Finding Cite(Finding finding, string[] files) =>
        files.Length == 0 ? finding : finding with { Observation = $"{finding.Observation} Evidence: {string.Join(", ", files)}." };

    private static Dictionary<string, string> With(Check check, Renderer renderer) =>
        check.With.ToDictionary(p => p.Key, p => renderer.Render(p.Value, mode: RenderMode.Evidence).Match(v => v, _ => p.Value), StringComparer.Ordinal);

    private static Finding Unable(string reason) => new(CheckOutcome.Error, $"Unable to assess: {reason}");

    private sealed record Counts(int Passed, int Failed, int Total, int Matched);
}
