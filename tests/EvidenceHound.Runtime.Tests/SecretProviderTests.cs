using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Secrets;

namespace EvidenceHound.Runtime.Tests;

public sealed class SecretProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hound-secrets-").FullName;

    private static readonly Dictionary<string, string?> Environment = new(StringComparer.Ordinal)
    {
        ["HOUND_CA_TOKEN"] = "hunter2",
        ["HOUND_EMPTY"] = "",
    };

    private static CompositeSecretProvider Composite() => new(new Dictionary<SecretScheme, ISecretProvider>
    {
        [SecretScheme.Env] = new EnvironmentSecretProvider(name => Environment.GetValueOrDefault(name)),
        [SecretScheme.File] = new FileSecretProvider(),
    });

    private static SecretReference Ref(string text) => SecretReference.Parse(text).Value();

    [Fact]
    public void Resolves_env_reference()
    {
        Composite().Resolve(Ref("env:HOUND_CA_TOKEN")).Value().Reveal().ShouldBe("hunter2");
    }

    [Theory]
    [InlineData("hunter2\n", "hunter2")]
    [InlineData("hunter2\r\n", "hunter2")]
    [InlineData("hunter2", "hunter2")]
    [InlineData("line one\nline two\n\n", "line one\nline two\n")]
    public void Resolves_file_reference_trimming_newline(string content, string expected)
    {
        var path = Path.Combine(_dir, "token");
        File.WriteAllText(path, content);

        Composite().Resolve(Ref("file:" + path)).Value().Reveal().ShouldBe(expected);
    }

    [Theory]
    [InlineData("env:HOUND_NOT_SET")]
    [InlineData("env:HOUND_EMPTY")]
    [InlineData("file:/run/secrets/hound-not-there")]
    public void Missing_reports_reference_only(string reference)
    {
        var problem = Composite().Resolve(Ref(reference)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("secret.missing");
        problem.Message.ShouldContain(reference);
    }

    [Fact]
    public void A_scheme_without_a_provider_is_missing()
    {
        var envOnly = new CompositeSecretProvider(new Dictionary<SecretScheme, ISecretProvider>
        {
            [SecretScheme.Env] = new EnvironmentSecretProvider(_ => "hunter2"),
        });

        var problem = envOnly.Resolve(Ref("file:/run/secrets/x")).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("secret.missing");
        problem.Message.ShouldNotContain("hunter2");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
