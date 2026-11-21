using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Runtime.Secrets;

/// <summary>The standard provider: <c>env:</c> through a name lookup and <c>file:</c> from disk.</summary>
public static class SecretProvider
{
    public static ISecretProvider Standard(Func<string, string?> lookup) =>
        new CompositeSecretProvider(new Dictionary<SecretScheme, ISecretProvider>
        {
            [SecretScheme.Env] = new EnvironmentSecretProvider(lookup),
            [SecretScheme.File] = new FileSecretProvider(),
        });
}
