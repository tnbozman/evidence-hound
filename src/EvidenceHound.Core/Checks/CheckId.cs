using System.Text.RegularExpressions;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Checks;

/// <summary>A stable, framework-neutral check identifier: lowercase dot-separated segments such as <c>identity.mfa.admins</c>.</summary>
public sealed partial record CheckId
{
    public const int MaxLength = 128;

    private CheckId(string value) => Value = value;

    public string Value { get; }

    public static Result<CheckId> Create(string value) =>
        value is { Length: <= MaxLength } && Pattern().IsMatch(value)
            ? Result.Ok(new CheckId(value))
            : Result.Fail<CheckId>(new Problem(
                "check_id.invalid",
                $"Check ID '{value}' must be lowercase dot-separated segments that start with a letter, at most {MaxLength} characters."));

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
