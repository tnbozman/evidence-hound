using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// A disposable CISO Assistant backend for functional tests, seeded by the same script as <c>make ca-up</c>:
/// a service-account token, the Example Pty Ltd folder, perimeter, audit and applied controls.
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
    }

    public ValueTask DisposeAsync() => _backend.DisposeAsync();

    private async Task<string> LogTailAsync()
    {
        var (stdout, stderr) = await _backend.GetLogsAsync();
        var log = $"{stdout}\n{stderr}";
        return log.Length <= 6000 ? log : log[^6000..];
    }
}
