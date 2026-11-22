using System.Text.Json.Nodes;
using EvidenceHound.Collectors;
using EvidenceHound.Collectors.Rest;
using EvidenceHound.Collectors.Sophos;
using EvidenceHound.Collectors.Sophos.Tests;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Runtime.Tests;

/// <summary>
/// The sophos-central profile gives the same check IDs, outcomes and evidence items as the coded Sophos collector on the same
/// recordings, which is what lets the coded collector go (s09-13). The profile hashes <c>geoLocation</c> where the coded
/// collector drops it, so that field is left out of the comparison.
/// </summary>
public sealed class SophosProfileParityTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 2, 0, 0, TimeSpan.Zero);

    private readonly string _profiles = Directory.CreateTempSubdirectory("hound-sophos-parity-").FullName;

    public SophosProfileParityTests() =>
        File.Copy(Path.Combine(AppContext.BaseDirectory, "files", "profiles", "sophos-central.yaml"), Path.Combine(_profiles, "sophos-central.yaml"));

    public void Dispose() => Directory.Delete(_profiles, recursive: true);

    public static TheoryData<string> Recordings() => ["as recorded", "unsupported firmware", "no firmware", "no firewalls", "every admin a super admin", "no super admin role"];

    [Theory]
    [MemberData(nameof(Recordings))]
    public async Task Profile_matches_coded_collector_on_every_recording(string recording)
    {
        using var sophos = RecordedSophos.Start(edit: Edit(recording));
        sophos.Whoami(new JsonObject
        {
            ["id"] = RecordedSophos.TenantId,
            ["idType"] = "tenant",
            ["apiHosts"] = new JsonObject { ["global"] = sophos.ApiUrl.ToString(), ["dataRegion"] = RegionHost },
        });
        var clients = new RegionClients(sophos.RegionUrl);

        var coded = await CollectAsync(new SophosCollector(), sophos.Settings(), clients);
        var profile = await CollectAsync(new RestCollector(), Profile(sophos.Settings()), clients);

        profile.Checks.Select(c => (c.CheckId.Value, c.Outcome)).OrderBy(c => c.Value, StringComparer.Ordinal)
            .ShouldBe(coded.Checks.Select(c => (c.CheckId.Value, c.Outcome)).OrderBy(c => c.Value, StringComparer.Ordinal), string.Join(" | ", profile.Checks.Select(c => c.Observation)));
        foreach (var list in new[] { "firewalls", "admins", "roles" })
        {
            var codedItems = (await Read(coded, list))["value"]!.AsArray();
            var profileItems = (await Read(profile, list))["items"]!.AsArray();
            Comparable(profileItems).ShouldBe(Comparable(codedItems), $"evidence for {list} ({recording})");
        }
    }

    private Dictionary<string, string> Profile(Dictionary<string, string> sophos)
    {
        var settings = new Dictionary<string, string>(sophos, StringComparer.Ordinal)
        {
            ["profile"] = "sophos-central",
            ["profiles_path"] = _profiles,
        };
        return settings;
    }

    private static Func<string, JsonArray, JsonArray>? Edit(string recording) => recording switch
    {
        "unsupported firmware" => (list, items) => list == "firewalls" ? Each(items, i => i["firmwareVersion"] = "SFOS 19.5.3 MR-3-Build652") : items,
        "no firmware" => (list, items) => list == "firewalls" ? Each(items, i => i.Remove("firmwareVersion")) : items,
        "no firewalls" => (list, items) => list == "firewalls" ? [] : items,
        "every admin a super admin" => (list, items) => list == "admins" ? Each(items, i => i["roleAssignments"] = new JsonArray(new JsonObject { ["roleId"] = SuperAdminRole })) : items,
        "no super admin role" => (list, items) => list == "roles" ? new JsonArray([.. items.OfType<JsonObject>().Where(r => r["name"]?.GetValue<string>() != "Super Admin").Select(r => r.DeepClone())]) : items,
        _ => null,
    };

    /// <summary>A real-looking data region, so both collectors trust it; <see cref="RegionClients"/> sends it to the recording.</summary>
    private static readonly string RegionHost = "https://api-eu01.central.sophos.com";

    private static readonly string SuperAdminRole = "00000000-0000-4000-8000-00000000a501";

    private static JsonArray Each(JsonArray items, Action<JsonObject> change)
    {
        var copy = (JsonArray)items.DeepClone();
        foreach (var item in copy.OfType<JsonObject>())
        {
            change(item);
        }

        return copy;
    }

    private static string Comparable(JsonArray items) =>
        new JsonArray([.. items.Select(i => i is JsonObject obj ? Without(obj, "geoLocation") : i?.DeepClone())]).ToJsonString();

    private static JsonObject Without(JsonObject item, string key) => new(item.Where(p => p.Key != key).Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));

    private static async Task<JsonNode> Read(CollectionResult result, string list) =>
        JsonNode.Parse((await result.Files.Single(f => f.FileName == list + ".json").ReadAsync(TestContext.Current.CancellationToken)).Content.Span)!;

    private static async Task<CollectionResult> CollectAsync(ICollector collector, Dictionary<string, string> settings, IHttpClientFactory clients) =>
        (await collector.CollectAsync(
            new CollectorContext(
                "sophos-example",
                new CollectorSettings(settings),
                new Dictionary<string, Secret>(StringComparer.Ordinal) { ["client_secret"] = new(RecordedSophos.ClientSecret) },
                new FixedClock(Now),
                NullLogger.Instance,
                clients),
            TestContext.Current.CancellationToken)).Value();

    /// <summary>Sends requests for the data region host to the recorded region server; everything else goes where it was addressed.</summary>
    private sealed class RegionClients(Uri region) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Rewrite(region), disposeHandler: true);

        private sealed class Rewrite(Uri region) : DelegatingHandler(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri is { Host: "api-eu01.central.sophos.com" } url)
                {
                    request.RequestUri = new Uri(region, url.PathAndQuery);
                }

                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
