using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;
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
    public Result<TokenCredential> Create(GraphConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.ClientCertificate is { } certificate)
        {
            return Certificate(certificate).Map(cert => (TokenCredential)new ClientCertificateCredential(connection.TenantId, connection.ClientId, cert));
        }

        return connection.ClientSecret is { } secret
            ? Result.Ok<TokenCredential>(new ClientSecretCredential(connection.TenantId, connection.ClientId, secret.Reveal()))
            : Result.Fail<TokenCredential>(new Problem(
                "m365.credentials_missing", "The Microsoft 365 collector needs the secret client_certificate (preferred) or client_secret."));
    }

    private static Result<X509Certificate2> Certificate(Secret pem)
    {
        try
        {
            var text = pem.Reveal();
            return Result.Ok(X509Certificate2.CreateFromPem(text, text));
        }
        catch (CryptographicException)
        {
            return Result.Fail<X509Certificate2>(new Problem(
                "m365.certificate_invalid", "The client_certificate secret must hold a PEM certificate and its private key (BEGIN CERTIFICATE and BEGIN PRIVATE KEY)."));
        }
        catch (ArgumentException)
        {
            return Result.Fail<X509Certificate2>(new Problem(
                "m365.certificate_invalid", "The client_certificate secret must hold a PEM certificate and its private key (BEGIN CERTIFICATE and BEGIN PRIVATE KEY)."));
        }
    }
}
