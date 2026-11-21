using Azure.Core;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.M365;

/// <summary>The Entra app registration the collector signs in as: tenant, client and one of a certificate (preferred) or a client secret.</summary>
public sealed record GraphConnection(string TenantId, string ClientId, Secret? ClientCertificate, Secret? ClientSecret);

/// <summary>Turns an app registration into a token credential. The default uses Azure.Identity; tests substitute a fake.</summary>
public interface IGraphCredentialFactory
{
    Result<TokenCredential> Create(GraphConnection connection);
}

/// <summary>
/// Client credentials through Azure.Identity: <c>ClientCertificateCredential</c> when a PEM certificate with its private key is
/// given, otherwise <c>ClientSecretCredential</c>.
/// </summary>
public sealed class AzureIdentityCredentials : IGraphCredentialFactory
{
    public Result<TokenCredential> Create(GraphConnection connection) => throw new NotImplementedException();
}
