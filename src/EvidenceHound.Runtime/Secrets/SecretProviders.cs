using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Runtime.Secrets;

/// <summary>Resolves <c>env:NAME</c> through a lookup, normally the process environment or .NET configuration. An empty value is missing.</summary>
public sealed class EnvironmentSecretProvider(Func<string, string?> lookup) : ISecretProvider
{
    public Result<Secret> Resolve(SecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return lookup(reference.Name) is { Length: > 0 } value
            ? Result.Ok(new Secret(value))
            : SecretProblems.Missing(reference, "the environment variable is not set");
    }
}

/// <summary>Resolves <c>file:/path</c> by reading the file, for example a Docker secret, trimming one trailing newline.</summary>
public sealed class FileSecretProvider : ISecretProvider
{
    public Result<Secret> Resolve(SecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return File.Exists(reference.Name)
            ? Result.Ok(new Secret(TrimOneNewline(File.ReadAllText(reference.Name))))
            : SecretProblems.Missing(reference, "the file does not exist");
    }

    private static string TrimOneNewline(string text) =>
        text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2]
        : text.EndsWith('\n') ? text[..^1]
        : text;
}

/// <summary>Resolves each reference with the provider for its scheme.</summary>
public sealed class CompositeSecretProvider(IReadOnlyDictionary<SecretScheme, ISecretProvider> providers) : ISecretProvider
{
    /// <summary>The standard provider: <c>env:</c> through a name lookup and <c>file:</c> from disk.</summary>
    public static CompositeSecretProvider Standard(Func<string, string?> lookup) =>
        new(new Dictionary<SecretScheme, ISecretProvider>
        {
            [SecretScheme.Env] = new EnvironmentSecretProvider(lookup),
            [SecretScheme.File] = new FileSecretProvider(),
        });

    public Result<Secret> Resolve(SecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return providers.TryGetValue(reference.Scheme, out var provider)
            ? provider.Resolve(reference)
            : SecretProblems.Missing(reference, "this deployment has no provider for that kind of reference");
    }
}

internal static class SecretProblems
{
    public static Result<Secret> Missing(SecretReference reference, string why) =>
        Result.Fail<Secret>(new Problem("secret.missing", $"Secret {reference} could not be resolved: {why}."));
}
