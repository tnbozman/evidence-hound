using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Configuration;

namespace EvidenceHound.Runtime.Secrets;

/// <summary>Resolves every secret a deployment references, to find missing ones before a run.</summary>
public static class SecretCheck
{
    /// <summary>Every problem from resolving the CISO Assistant token and each collector's secrets. The secrets themselves are dropped.</summary>
    public static IReadOnlyList<Problem> Missing(DeploymentConfig config, ISecretProvider secrets)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(secrets);

        IEnumerable<SecretReference> references = [config.CisoAssistant.Token, .. config.Collectors.SelectMany(c => c.Secrets.Values)];
        return [.. references.Distinct().SelectMany(reference => secrets.Resolve(reference).Match(_ => [], problems => problems))];
    }
}
