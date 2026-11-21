using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Secrets;

/// <summary>Where a secret lives: <c>env:NAME</c> or <c>file:/path</c>. Never the secret itself.</summary>
public sealed record SecretReference
{
    private SecretReference(SecretScheme scheme, string name)
    {
        Scheme = scheme;
        Name = name;
    }

    public SecretScheme Scheme { get; }

    /// <summary>The environment variable name or the file path.</summary>
    public string Name { get; }

    public static Result<SecretReference> Parse(string text) => throw new NotImplementedException();

    public override string ToString() => throw new NotImplementedException();
}

/// <summary>The kinds of secret reference.</summary>
public enum SecretScheme
{
    Env,
    File,
}
