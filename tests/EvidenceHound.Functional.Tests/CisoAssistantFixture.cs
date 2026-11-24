using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// A disposable CISO Assistant backend for functional tests, seeded the same way as <c>make ca-up</c>: the seed script creates a
/// service-account token and the example staff users, then <c>hound scope apply</c> writes the example scope
/// (<c>examples/compliance-repo/scope</c>): the libraries, the Example Pty Ltd folder, perimeter and systems, the audits, documents,
/// applied controls and hand-written assessments.
/// </summary>
public sealed class CisoAssistantFixture : IAsyncLifetime
{
    /// <summary>The CISO Assistant release every functional test and <c>dev/ciso-assistant/compose.yml</c> use.</summary>
    public const string BackendImage = "ghcr.io/intuitem/ciso-assistant-community/backend:v4.3.4";

    private static readonly ushort ApiPort = 8000;
    private static readonly string TokenPrefix = "HOUND_CA_TOKEN=";

    private readonly IContainer _backend = new ContainerBuilder(BackendImage)
        .WithEnvironment("ALLOWED_HOSTS", "*")
        .WithEnvironment("CISO_ASSISTANT_URL", "http://localhost")
        .WithEnvironment("DJANGO_SECRET_KEY", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
        .WithEnvironment("DJANGO_SUPERUSER_EMAIL", "admin@example.com")
        .WithEnvironment("DJANGO_SUPERUSER_PASSWORD", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .WithPortBinding(ApiPort, assignRandomHostPort: true)
        .WithResourceMapping(DirectoryPath.Of(Path.Combine(AppContext.BaseDirectory, "seed")), DirectoryPath.Of("/seed/"))
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
            request => request.ForPort(ApiPort).ForPath("/api/health/"),
            wait => wait.WithTimeout(TimeSpan.FromMinutes(10))))
        .Build();

    private string? _token;

    /// <summary>The API root, ending in <c>/api/</c>.</summary>
    public Uri ApiUrl => new($"http://{_backend.Hostname}:{_backend.GetMappedPublicPort(ApiPort)}/api/");

    /// <summary>The seeded service account's personal access token.</summary>
    public string Token => _token ?? throw new InvalidOperationException("The fixture has not been seeded.");

    /// <summary>The report (JSON) of the <c>hound scope apply</c> that wrote the example scope.</summary>
    public string SeedApplyReport { get; private set; } = "";

    /// <summary>The repository root: the folder above the tests that holds <c>global.json</c>.</summary>
    public static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("No global.json above the test folder.");
        }
    }

    /// <summary>The example scope the seed applies.</summary>
    public static string ExampleScope => Path.Combine(RepoRoot, "examples", "compliance-repo", "scope");

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _backend.StartAsync();
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException($"CISO Assistant did not become healthy. Container log tail:\n{await LogTailAsync()}", ex);
        }

        var seed = await _backend.ExecAsync(["bash", "/seed/seed.sh"]);
        _token = seed.Stdout.Split('\n')
            .Select(line => line.Trim())
            .LastOrDefault(line => line.StartsWith(TokenPrefix, StringComparison.Ordinal))?[TokenPrefix.Length..];
        if (seed.ExitCode != 0 || string.IsNullOrEmpty(_token))
        {
            throw new InvalidOperationException($"Seeding CISO Assistant failed (exit {seed.ExitCode}): {seed.Stderr}{seed.Stdout.Replace(_token ?? "\0", "***", StringComparison.Ordinal)}");
        }

        await ApplyExampleScopeAsync();
    }

    public ValueTask DisposeAsync() => _backend.DisposeAsync();

    /// <summary>What <c>scripts/ca-apply-scope.sh</c> does for <c>make ca-up</c>: apply the example scope with the example repo's own hound.yaml.</summary>
    private async Task ApplyExampleScopeAsync()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            ["scope", "apply", ExampleScope, "--config", Path.Combine(RepoRoot, "examples", "compliance-repo", "hound.yaml")],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = Token },
            CancellationToken.None);
        SeedApplyReport = output.ToString();
        var results = Directory.CreateDirectory(Path.Combine(RepoRoot, "test-results", "scope-plan")).FullName;
        await File.WriteAllTextAsync(Path.Combine(results, "seed-apply.json"), SeedApplyReport);
        if (exitCode != ExitCodes.Ok)
        {
            throw new InvalidOperationException($"hound scope apply on the example scope failed (exit {exitCode}):\n{SeedApplyReport}{error}");
        }
    }

    private async Task<string> LogTailAsync()
    {
        var (stdout, stderr) = await _backend.GetLogsAsync();
        var log = $"{stdout}\n{stderr}";
        return log.Length <= 6000 ? log : log[^6000..];
    }
}
