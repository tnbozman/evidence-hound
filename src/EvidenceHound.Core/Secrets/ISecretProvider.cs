using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Secrets;

/// <summary>Resolves a secret reference at run time. A missing secret is the problem <c>secret.missing</c>, naming the reference only.</summary>
public interface ISecretProvider
{
    Result<Secret> Resolve(SecretReference reference);
}
