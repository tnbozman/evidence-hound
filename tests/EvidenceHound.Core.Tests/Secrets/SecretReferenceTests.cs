using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Core.Tests.Secrets;

public class SecretReferenceTests
{
    [Theory]
    [InlineData("env:HOUND_CA_TOKEN", SecretScheme.Env, "HOUND_CA_TOKEN")]
    [InlineData("file:/run/secrets/ca_token", SecretScheme.File, "/run/secrets/ca_token")]
    public void Parses_references(string text, SecretScheme scheme, string name)
    {
        var reference = SecretReference.Parse(text).Match(r => r, _ => throw new InvalidOperationException());

        reference.Scheme.ShouldBe(scheme);
        reference.Name.ShouldBe(name);
        reference.ToString().ShouldBe(text);
    }

    [Theory]
    [InlineData("hunter2")]
    [InlineData("env:1BAD")]
    [InlineData("env:has space")]
    [InlineData("file:relative/path")]
    [InlineData("vault:kv/ca")]
    public void Rejects_anything_else_without_echoing_it(string text)
    {
        var problem = SecretReference.Parse(text).Match(_ => throw new InvalidOperationException(), problems => problems).ShouldHaveSingleItem();

        problem.Code.ShouldBe("secret.reference_invalid");
        problem.Message.ShouldNotContain(text);
    }
}
