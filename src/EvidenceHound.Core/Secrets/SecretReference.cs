using System.Text.RegularExpressions;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Secrets;

/// <summary>Where a secret lives: <c>env:NAME</c> or <c>file:/path</c>. Never the secret itself.</summary>
public sealed partial record SecretReference
{
    private static readonly string EnvPrefix = "env:";
    private static readonly string FilePrefix = "file:";

    private SecretReference(SecretScheme scheme, string name)
    {
        Scheme = scheme;
        Name = name;
    }

    public SecretScheme Scheme { get; }

    /// <summary>The environment variable name or the absolute file path.</summary>
    public string Name { get; }

    /// <summary>Parses a reference. The problem never repeats the text, in case it is a secret pasted in by mistake.</summary>
    public static Result<SecretReference> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text switch
        {
            _ when text.StartsWith(EnvPrefix, StringComparison.Ordinal) && EnvName().IsMatch(text[EnvPrefix.Length..]) =>
                Result.Ok(new SecretReference(SecretScheme.Env, text[EnvPrefix.Length..])),
            _ when text.StartsWith(FilePrefix, StringComparison.Ordinal) && text[FilePrefix.Length..].StartsWith('/') =>
                Result.Ok(new SecretReference(SecretScheme.File, text[FilePrefix.Length..])),
            _ => Result.Fail<SecretReference>(new Problem(
                "secret.reference_invalid",
                "a secret must be a reference such as env:NAME or file:/run/secrets/name, never the value itself.")),
        };
    }

    public override string ToString() => (Scheme == SecretScheme.Env ? EnvPrefix : FilePrefix) + Name;

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvName();
}

/// <summary>The kinds of secret reference.</summary>
public enum SecretScheme
{
    Env,
    File,
}
