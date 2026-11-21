using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Configuration;

/// <summary>Parses and validates a deployment config file (see <c>schemas/config.schema.json</c>), reporting every problem at once.</summary>
public static class ConfigLoader
{
    public static Result<DeploymentConfig> Parse(string yaml, string source, IReadOnlySet<string> knownCollectorTypes) =>
        throw new NotImplementedException();

    public static Task<Result<DeploymentConfig>> LoadAsync(string path, IReadOnlySet<string> knownCollectorTypes, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
