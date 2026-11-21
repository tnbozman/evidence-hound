using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.M365.Tests;

public class CredentialTests
{
    private static readonly string Tenant = "00000000-0000-4000-8000-00000000e001";

    private static readonly string Client = "00000000-0000-4000-8000-00000000c001";

    [Fact]
    public void Certificate_is_preferred()
    {
        var connection = new GraphConnection(Tenant, Client, new Secret(SelfSignedPem()), new Secret("example-client-secret-value"));

        new AzureIdentityCredentials().Create(connection).Value().ShouldBeOfType<ClientCertificateCredential>();
    }

    [Fact]
    public void Client_secret_is_used_without_a_certificate()
    {
        var connection = new GraphConnection(Tenant, Client, null, new Secret("example-client-secret-value"));

        new AzureIdentityCredentials().Create(connection).Value().ShouldBeOfType<ClientSecretCredential>();
    }

    [Fact]
    public void Neither_is_a_problem()
    {
        var problem = new AzureIdentityCredentials().Create(new GraphConnection(Tenant, Client, null, null)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("m365.credentials_missing");
    }

    [Fact]
    public void A_certificate_that_is_not_pem_is_a_problem_that_does_not_echo_it()
    {
        var problem = new AzureIdentityCredentials().Create(new GraphConnection(Tenant, Client, new Secret("not-a-pem-certificate-9d2b"), null)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("m365.certificate_invalid");
        problem.Message.ShouldNotContain("not-a-pem-certificate-9d2b");
    }

    private static string SelfSignedPem()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=hound-test.example.com", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return certificate.ExportCertificatePem() + "\n" + key.ExportPkcs8PrivateKeyPem();
    }
}
