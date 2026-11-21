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

    private static CompositeSecretProvider Composite() => CompositeSecretProvider.Standard(name => Environment.GetValueOrDefault(name));

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

    [Fact]
    public void Secret_check_lists_every_missing_reference_once()
    {
        var yaml = """
            ciso_assistant: {url: "http://localhost:8000/api/", token: "env:HOUND_NOT_SET"}
            evidence: {root: ./evidence}
            collectors:
              - {name: one, type: fake, schedule: "0 2 * * *", secrets: {key: "env:HOUND_CA_TOKEN"}}
              - {name: two, type: fake, schedule: "0 2 * * *", secrets: {key: "env:HOUND_NOT_SET", other: "file:/run/secrets/hound-not-there"}}
            target: {mapping: ./mapping.yaml, compliance_assessment: EXAMPLE-1}
            """;
        var config = Configuration.ConfigLoader.Parse(yaml, "hound.yaml", new HashSet<string>(StringComparer.Ordinal) { "fake" }).Value();

        var missing = SecretCheck.Missing(config, CompositeSecretProvider.Standard(name => Environment.GetValueOrDefault(name)));

        missing.Select(p => p.Message).ShouldBe([
            "Secret env:HOUND_NOT_SET could not be resolved: the environment variable is not set.",
            "Secret file:/run/secrets/hound-not-there could not be resolved: the file does not exist.",
        ]);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
