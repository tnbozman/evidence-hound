using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Runtime.Secrets;

/// <summary>Resolves <c>env:NAME</c> through a lookup, normally the process environment or .NET configuration.</summary>
public sealed class EnvironmentSecretProvider(Func<string, string?> lookup) : ISecretProvider
{
    public Result<Secret> Resolve(SecretReference reference) => throw new NotImplementedException(lookup.ToString());
}

/// <summary>Resolves <c>file:/path</c> by reading the file, for example a Docker secret, trimming one trailing newline.</summary>
public sealed class FileSecretProvider : ISecretProvider
{
    public Result<Secret> Resolve(SecretReference reference) => throw new NotImplementedException();
}

/// <summary>Resolves each reference with the provider for its scheme.</summary>
public sealed class CompositeSecretProvider(IReadOnlyDictionary<SecretScheme, ISecretProvider> providers) : ISecretProvider
{
    public Result<Secret> Resolve(SecretReference reference) => throw new NotImplementedException(providers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
