using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest;
using EvidenceHound.Collectors.Rest.Tests;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Storage;

namespace EvidenceHound.Runtime.Tests;

/// <summary>Each run's manifest records the profiles that judged it, so evidence shows exactly which rules were applied.</summary>
public sealed class RunManifestTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 2, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("hound-manifest-").FullName;

    private readonly string _profiles = Directory.CreateTempSubdirectory("hound-manifest-profiles-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_profiles, recursive: true);
    }

    [Fact]
    public async Task Records_profile_name_version_and_hash()
    {
        var library = Path.Combine(AppContext.BaseDirectory, "files", "profiles", "example.yaml");
        File.Copy(library, Path.Combine(_profiles, "example.yaml"));
        using var api = RecordedExample.Start();
        var instance = new CollectorInstance(
            "rest-example",
            RestCollector.TypeName,
            "0 2 * * *",
            api.Settings(_profiles),
            new Dictionary<string, SecretReference> { ["client_secret"] = SecretReference.Parse("env:EXAMPLE_REST_SECRET").Value() });
        var step = new CollectStep(
            Secrets.From(new Dictionary<string, string> { ["EXAMPLE_REST_SECRET"] = RecordedExample.ClientSecret }),
            new FileEvidenceStore(_root),
            new FixedClock(Now),
            null,
            new CollectorServices(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, RecordedExample.HttpClients));

        var run = (await step.RunAsync(instance, new RestCollector(), TestContext.Current.CancellationToken)).Value();

        var profile = run.Manifest.Profiles.ShouldHaveSingleItem();
        profile.ShouldBe(new ManifestProfile("example", "1.0.0", "evidence-hound/profiles/example.yaml", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(library)))));
        var written = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(run.Stored.Location, Manifests.FileName), TestContext.Current.CancellationToken))!;
        written["profiles"]![0]!["sha256"]!.GetValue<string>().ShouldBe(profile.Sha256);
        written["profiles"]![0]!["source"]!.GetValue<string>().ShouldBe("evidence-hound/profiles/example.yaml");
    }

    [Fact]
    public void Manifest_without_profiles_is_written_as_before()
    {
        var manifest = Manifests.Build("idp", "1.0.0", Now, []).Value();

        Manifests.ToJson(manifest).ShouldNotContain("profiles");
    }
}
