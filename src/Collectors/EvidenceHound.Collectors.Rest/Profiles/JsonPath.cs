using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Profiles;

/// <summary>
/// A small, fixed path language over the JSON tree: <c>$</c>, then <c>.name</c>, <c>['any name']</c>, <c>[2]</c> or <c>[*]</c>
/// (every element of a list). Nothing else, so every path can be checked before a run.
/// </summary>
public sealed record JsonPath
{
    private JsonPath(string text, IReadOnlyList<PathSegment> segments)
    {
        Text = text;
        Segments = segments;
    }

    public string Text { get; }

    public IReadOnlyList<PathSegment> Segments { get; }

    /// <summary>True when the path can match more than one value.</summary>
    public bool IsMulti => Segments.Any(s => s is WildcardSegment);

    public static JsonPath Root { get; } = new("$", []);

    public static Result<JsonPath> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Read(text) is { } segments
            ? Result.Ok(new JsonPath(text, segments))
            : Result.Fail<JsonPath>(new Problem("profile.path_invalid", $"'{text}' is not a path; use $, .name, ['name'], [0] or [*], for example $.items[*].id."));
    }

    /// <summary>
    /// The values at this path. Not found when any step is missing on any value it was applied to, or when a step meets the wrong
    /// kind of value (a name on a list, an index on an object): a missing field is never read as an empty list.
    /// </summary>
    public PathMatch Select(JsonNode? root)
    {
        IReadOnlyList<JsonNode?> current = [root];
        foreach (var segment in Segments)
        {
            var next = new List<JsonNode?>();
            foreach (var node in current)
            {
                if (!segment.Step(node, next))
                {
                    return PathMatch.Missing;
                }
            }

            current = next;
        }

        return new PathMatch(true, current);
    }

    public override string ToString() => Text;

    private static List<PathSegment>? Read(string text)
    {
        if (!text.StartsWith('$'))
        {
            return null;
        }

        var segments = new List<PathSegment>();
        var i = 1;
        while (i < text.Length)
        {
            var segment = text[i] switch
            {
                '.' => ReadName(text, ref i),
                '[' => ReadBracket(text, ref i),
                _ => null,
            };
            if (segment is null)
            {
                return null;
            }

            segments.Add(segment);
        }

        return segments;
    }

    private static NameSegment? ReadName(string text, ref int i)
    {
        var start = ++i;
        while (i < text.Length && IsNameChar(text[i]))
        {
            i++;
        }

        return i > start ? new NameSegment(text[start..i]) : null;
    }

    private static PathSegment? ReadBracket(string text, ref int i)
    {
        var close = text.IndexOf(']', i);
        if (close < 0)
        {
            return null;
        }

        var inside = text[(i + 1)..close];
        i = close + 1;
        return inside switch
        {
            "*" => new WildcardSegment(),
            ['\'', .., '\''] when inside.Length >= 3 && !inside[1..^1].Contains('\'', StringComparison.Ordinal) => new NameSegment(inside[1..^1]),
            _ when inside.Length > 0 && inside.All(char.IsAsciiDigit) && int.TryParse(inside, out var index) => new IndexSegment(index),
            _ => null,
        };
    }

    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '@' or '#' or ':';

    /// <summary>The path written out again, for messages.</summary>
    internal static string Describe(IEnumerable<PathSegment> segments)
    {
        var builder = new StringBuilder("$");
        foreach (var segment in segments)
        {
            builder.Append(segment);
        }

        return builder.ToString();
    }
}

/// <summary>The values a path matched, or not found.</summary>
public sealed record PathMatch(bool Found, IReadOnlyList<JsonNode?> Values)
{
    public static PathMatch Missing { get; } = new(false, []);
}

/// <summary>One step of a path.</summary>
public abstract record PathSegment
{
    /// <summary>Adds what this step finds in <paramref name="node"/> to <paramref name="found"/>; false when the step is missing.</summary>
    internal abstract bool Step(JsonNode? node, List<JsonNode?> found);
}

public sealed record NameSegment(string Name) : PathSegment
{
    internal override bool Step(JsonNode? node, List<JsonNode?> found)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue(Name, out var value))
        {
            found.Add(value);
            return true;
        }

        return false;
    }

    public override string ToString() => Name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? "." + Name : $"['{Name}']";
}

public sealed record IndexSegment(int Index) : PathSegment
{
    internal override bool Step(JsonNode? node, List<JsonNode?> found)
    {
        if (node is JsonArray array && Index < array.Count)
        {
            found.Add(array[Index]);
            return true;
        }

        return false;
    }

    public override string ToString() => $"[{Index}]";
}

public sealed record WildcardSegment : PathSegment
{
    internal override bool Step(JsonNode? node, List<JsonNode?> found)
    {
        if (node is JsonArray array)
        {
            found.AddRange(array);
            return true;
        }

        return false;
    }

    public override string ToString() => "[*]";
}
