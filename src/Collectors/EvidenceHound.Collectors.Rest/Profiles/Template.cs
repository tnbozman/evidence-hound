using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Profiles;

/// <summary>What a <c>${...}</c> placeholder names.</summary>
public enum ReferenceKind
{
    /// <summary><c>${name}</c>: a parameter, or a variable set by a query.</summary>
    Value,

    /// <summary><c>${secrets.name}</c>.</summary>
    Secret,

    /// <summary><c>${item.path}</c>: a field of the item a <c>for_each</c> query is called for.</summary>
    Item,
}

/// <summary>One part of a template: literal text, or a placeholder.</summary>
public abstract record TemplatePart;

public sealed record LiteralPart(string Text) : TemplatePart;

/// <summary>A placeholder. For <see cref="ReferenceKind.Item"/>, <paramref name="Name"/> is a path into the item, such as <c>$.id</c>.</summary>
public sealed record ReferencePart(ReferenceKind Kind, string Name) : TemplatePart;

/// <summary>Text with <c>${name}</c>, <c>${secrets.name}</c> and <c>${item.field}</c> placeholders.</summary>
public static class Template
{
    public static Result<IReadOnlyList<TemplatePart>> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<TemplatePart>();
        var i = 0;
        while (i < text.Length)
        {
            var open = text.IndexOf("${", i, StringComparison.Ordinal);
            if (open < 0)
            {
                parts.Add(new LiteralPart(text[i..]));
                break;
            }

            if (open > i)
            {
                parts.Add(new LiteralPart(text[i..open]));
            }

            var close = text.IndexOf('}', open);
            if (close < 0)
            {
                return Invalid(text);
            }

            if (Reference(text[(open + 2)..close]) is not { } reference)
            {
                return Invalid(text);
            }

            parts.Add(reference);
            i = close + 1;
        }

        return Result.Ok<IReadOnlyList<TemplatePart>>(parts);
    }

    /// <summary>The placeholders in <paramref name="text"/>, ignoring text that is not a valid template.</summary>
    public static IEnumerable<ReferencePart> References(string text) =>
        Parse(text).Match(parts => parts.OfType<ReferencePart>(), _ => []);

    /// <summary>True when the text has a placeholder.</summary>
    public static bool HasReferences(string text) => text.Contains("${", StringComparison.Ordinal);

    private static ReferencePart? Reference(string inside) => inside switch
    {
        _ when inside.StartsWith("secrets.", StringComparison.Ordinal) && IsName(inside[8..]) => new ReferencePart(ReferenceKind.Secret, inside[8..]),
        "item" => new ReferencePart(ReferenceKind.Item, "$"),
        _ when inside.StartsWith("item.", StringComparison.Ordinal) && JsonPath.Parse("$" + inside[4..]) is Result<JsonPath>.Ok => new ReferencePart(ReferenceKind.Item, "$" + inside[4..]),
        _ when IsName(inside) => new ReferencePart(ReferenceKind.Value, inside),
        _ => null,
    };

    private static bool IsName(string text) => text.Length > 0 && char.IsAsciiLetter(text[0]) && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static Result<IReadOnlyList<TemplatePart>> Invalid(string text) =>
        Result.Fail<IReadOnlyList<TemplatePart>>(new Problem(
            "profile.template_invalid", $"'{text}' has a placeholder that is not ${{name}}, ${{secrets.name}} or ${{item.field}}."));
}
