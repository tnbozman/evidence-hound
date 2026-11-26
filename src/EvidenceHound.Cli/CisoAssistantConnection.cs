using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;

namespace EvidenceHound.Cli;

/// <summary>How a command finds the CISO Assistant it talks to: the address and token from the environment, or from the deployment config.</summary>
internal static class CisoAssistantConnection
{
    public const string UrlSetting = "HOUND_CA_URL";
    public const string TokenSetting = "HOUND_CA_TOKEN";

    /// <summary>The connection from <c>HOUND_CA_URL</c> and <c>HOUND_CA_TOKEN</c> alone.</summary>
    internal static Result<CisoAssistantOptions> FromSettings(IConfiguration configuration)
    {
        var url = configuration[UrlSetting];
        var token = configuration[TokenSetting];
        return Validation.Validate(
            () => new CisoAssistantOptions(new Uri(url!.EndsWith('/') ? url : url + "/"), token!),
            new Invariant(Uri.TryCreate(url, UriKind.Absolute, out _), new Problem("config.ca_url_missing", $"Set {UrlSetting} to the CISO Assistant API address, for example http://localhost:8000/api/.")),
            new Invariant(!string.IsNullOrWhiteSpace(token), new Problem("config.ca_token_missing", $"Set {TokenSetting} to a CISO Assistant personal access token.")));
    }

    /// <summary>
    /// The connection for a command given a deployment config: <c>HOUND_CA_URL</c> and <c>HOUND_CA_TOKEN</c> when set, otherwise the
    /// config's <c>ciso_assistant</c> address and token reference. Without a config, both variables are needed.
    /// </summary>
    internal static async Task<Result<CisoAssistantOptions>> ResolveAsync(
        IConfiguration configuration, FileInfo? configFile, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        if (configFile is null || (configuration[UrlSetting] is { Length: > 0 } && configuration[TokenSetting] is { Length: > 0 }))
        {
            return FromSettings(configuration);
        }

        var deployment = await DeploymentFile.LoadAsync(configFile, registry, cancellationToken);
        return deployment.Bind(config =>
        {
            var url = configuration[UrlSetting] is { Length: > 0 } overridden ? new Uri(overridden.EndsWith('/') ? overridden : overridden + "/") : config.CisoAssistant.Url;
            return configuration[TokenSetting] is { Length: > 0 } token
                ? Result.Ok(new CisoAssistantOptions(url, token))
                : DeploymentFile.Secrets(configuration).Resolve(config.CisoAssistant.Token).Map(secret => new CisoAssistantOptions(url, secret.Reveal()));
        });
    }
}
