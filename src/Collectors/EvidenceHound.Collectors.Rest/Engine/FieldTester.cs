using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EvidenceHound.Collectors.Rest.Profiles;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>The result of one field test on one row: pass, fail, or unknown with the reason (a missing field or a value of the wrong type).</summary>
public abstract record TestOutcome
{
    public static TestOutcome Pass { get; } = new Known(true);

    public static TestOutcome Fail { get; } = new Known(false);

    public static TestOutcome Of(bool passed) => passed ? Pass : Fail;

    public sealed record Known(bool Passed) : TestOutcome;

    public sealed record Unknown(string Reason) : TestOutcome;
}

/// <summary>
/// Applies a <see cref="FieldTest"/> to one row. Operands are rendered with the instance's parameters; a list operator splits each
/// operand on commas, so <c>${supported_os}</c> can hold several values. Times are compared with the run's clock.
/// </summary>
public static partial class FieldTester
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static TestOutcome Test(FieldTest test, JsonNode? row, Renderer renderer, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(renderer);
        if (JsonPath.Parse(test.Field).Match(p => (JsonPath?)p, _ => null) is not { } path)
        {
            return new TestOutcome.Unknown($"{test.Field} is not a path.");
        }

        var match = path.Select(row);
        if (test.Operator == TestOperator.Exists)
        {
            return Negated(test, match.Found && match.Values.Any(v => v is not null));
        }

        if (!match.Found || match.Values is [null])
        {
            return test.Missing switch
            {
                MissingRule.Pass => TestOutcome.Pass,
                MissingRule.Fail => TestOutcome.Fail,
                _ => new TestOutcome.Unknown($"{test.Field} is missing"),
            };
        }

        var value = path.IsMulti ? new JsonArray([.. match.Values.Select(v => v?.DeepClone())]) : match.Values[0];
        var operands = Operands(test, renderer);
        return Apply(test.Operator, value, operands, now) switch
        {
            { } passed => Negated(test, passed),
            null => new TestOutcome.Unknown($"{test.Field} is {Describe(value)}, which {OperatorName(test.Operator)} cannot test"),
        };
    }

    private static TestOutcome Negated(FieldTest test, bool passed) => TestOutcome.Of(passed != test.Negate);

    private static string[] Operands(FieldTest test, Renderer renderer)
    {
        var rendered = test.Operands.Select(o => renderer.Render(o, mode: RenderMode.Evidence).Match(v => v, _ => o));
        return test.Operator is TestOperator.In or TestOperator.VersionIn
            ? [.. rendered.SelectMany(o => o.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))]
            : [.. rendered];
    }

    /// <summary>True or false, or null when the value is the wrong type for the operator.</summary>
    private static bool? Apply(TestOperator op, JsonNode? value, string[] operands, DateTimeOffset now) => op switch
    {
        TestOperator.EqualTo => Same(value, operands[0]),
        TestOperator.NotEqualTo => Same(value, operands[0]) is { } same ? !same : null,
        TestOperator.In => AnyOf(operands, o => Same(value, o)),
        TestOperator.Matches => Text(value) is { } text ? Regex.IsMatch(text, operands[0], RegexOptions.None, RegexTimeout) : null,
        TestOperator.AnyMatches => AnyMatches(value, operands[0]),
        TestOperator.VersionIn => Version(value) is { } version ? operands.Any(o => Version(o) is { } listed && StartsWith(version, listed)) : null,
        TestOperator.VersionAtLeast => Version(value) is { } version && Version(operands[0]) is { } minimum ? Compare(version, minimum) >= 0 : null,
        TestOperator.Within => Age(value, now) is { } age && Durations.TryParse(operands[0], out var limit) ? age <= limit : null,
        TestOperator.OlderThan => Age(value, now) is { } age && Durations.TryParse(operands[0], out var limit) ? age > limit : null,
        TestOperator.AtLeast => Number(value) is { } n && Number(operands[0]) is { } min ? n >= min : null,
        TestOperator.AtMost => Number(value) is { } n && Number(operands[0]) is { } max ? n <= max : null,
        _ => null,
    };

    private static bool? AnyOf(IEnumerable<string> operands, Func<string, bool?> test)
    {
        var results = operands.Select(test).ToArray();
        return results.Any(r => r is null) ? null : results.Any(r => r == true);
    }

    private static bool? Same(JsonNode? value, string operand) => value switch
    {
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => Number(v) is { } n && Number(operand) is { } o ? n == o : false,
        JsonValue v => string.Equals(Renderer.Text(v), operand, StringComparison.Ordinal),
        _ => null,
    };

    private static bool? AnyMatches(JsonNode? value, string pattern) => value switch
    {
        JsonArray array => array.All(e => Text(e) is not null) ? array.Any(e => Regex.IsMatch(Text(e)!, pattern, RegexOptions.None, RegexTimeout)) : null,
        _ => Text(value) is { } text ? Regex.IsMatch(text, pattern, RegexOptions.None, RegexTimeout) : null,
    };

    private static string? Text(JsonNode? value) => value is JsonValue v && v.GetValueKind() is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
        ? Renderer.Text(v)
        : null;

    private static double? Number(JsonNode? value) => Text(value) is { } text ? Number(text) : null;

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static TimeSpan? Age(JsonNode? value, DateTimeOffset now) =>
        value is JsonValue v && v.GetValueKind() == JsonValueKind.String
        && DateTimeOffset.TryParse(Renderer.Text(v), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when)
            ? now - when
            : null;

    private static int[]? Version(JsonNode? value) => Text(value) is { } text ? Version(text) : null;

    /// <summary>The numeric parts of a dotted version such as 15.1.2 or v21.0.1 MR1; null when it has none.</summary>
    private static int[]? Version(string text)
    {
        var parts = VersionParts().Match(text.Trim());
        var numbers = parts.Success ? parts.Value.Split('.').Select(p => int.TryParse(p, CultureInfo.InvariantCulture, out var n) ? n : -1).ToArray() : [];
        return numbers.Length > 0 && numbers.All(n => n >= 0) ? numbers : null;
    }

    private static bool StartsWith(int[] version, int[] listed) => listed.Length <= version.Length && listed.SequenceEqual(version.Take(listed.Length));

    private static int Compare(int[] left, int[] right)
    {
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var difference = left.ElementAtOrDefault(i).CompareTo(right.ElementAtOrDefault(i));
            if (difference != 0)
            {
                return difference;
            }
        }

        return 0;
    }

    private static string Describe(JsonNode? value) => value switch
    {
        JsonArray => "a list",
        JsonObject => "an object",
        JsonValue v => $"'{Renderer.Text(v)}'",
        _ => "null",
    };

    private static string OperatorName(TestOperator op) => ProfileParser.Operators.FirstOrDefault(pair => pair.Value == op).Key ?? op.ToString();

    [GeneratedRegex(@"\d+(\.\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex VersionParts();
}
