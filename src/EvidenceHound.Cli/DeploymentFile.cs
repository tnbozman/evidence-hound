using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Secrets;
using Microsoft.Extensions.Configuration;

namespace EvidenceHound.Cli;

/// <summary>How a command reads the deployment config (<c>hound.yaml</c>) and the secrets it references.</summary>
internal static class DeploymentFile
{
    /// <summary>Loads a deployment config, checking each collector against what its registered type describes.</summary>
    internal static Task<Result<DeploymentConfig>> LoadAsync(FileInfo file, CollectorRegistry registry, CancellationToken cancellationToken) =>
        ConfigLoader.LoadAsync(file.FullName, registry.Descriptions, cancellationToken);

    /// <summary>The secret provider for this run: <c>env:</c> reads .NET configuration (environment variables and test settings).</summary>
    internal static ISecretProvider Secrets(IConfiguration configuration) => CompositeSecretProvider.Standard(name => configuration[name]);
}
