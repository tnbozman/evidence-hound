using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Testing;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.M365.Tests;

/// <summary>s09-28: the read-only lookups that pre-fill the capability map, with the collector's own app registration and settings.</summary>
public sealed class EnvironmentLookupTests : IDisposable
{
    private readonly RecordedApi _graph = RecordedApi.Start();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_enabled_licences_and_the_kinds_of_intune_configuration()
    {
        _graph.Respond("GET", "/v1.0/subscribedSkus", 200, Collection(
            new JsonObject { ["skuPartNumber"] = "SPE_E3", ["capabilityStatus"] = "Enabled" },
            new JsonObject { ["skuPartNumber"] = "AAD_PREMIUM_P2", ["capabilityStatus"] = "Warning" },
            new JsonObject { ["skuPartNumber"] = "WIN_DEF_ATP", ["capabilityStatus"] = "Suspended" }));
        _graph.Respond("GET", "/v1.0/deviceManagement/deviceConfigurations", 200, Collection(
            new JsonObject { ["@odata.type"] = "#microsoft.graph.windowsUpdateForBusinessConfiguration", ["id"] = "c1" },
            new JsonObject { ["@odata.type"] = "#microsoft.graph.windowsUpdateForBusinessConfiguration", ["id"] = "c2" },
            new JsonObject { ["@odata.type"] = "#microsoft.graph.windows10EndpointProtectionConfiguration", ["id"] = "c3" }));

        var found = (await Lookup().ReadAsync(Settings(), Secrets, _graph.HttpClients, Token)).Value();

        found.Licences.ShouldBe(["AAD_PREMIUM_P2", "SPE_E3"]);
        found.ConfigurationTypes.ShouldBe(["#microsoft.graph.windows10EndpointProtectionConfiguration", "#microsoft.graph.windowsUpdateForBusinessConfiguration"]);
    }

    [Fact]
    public async Task A_tenant_without_intune_has_no_configuration_kinds()
    {
        _graph.Respond("GET", "/v1.0/subscribedSkus", 200, Collection(new JsonObject { ["skuPartNumber"] = "AAD_PREMIUM", ["capabilityStatus"] = "Enabled" }));
        _graph.Respond("GET", "/v1.0/deviceManagement/deviceConfigurations", 403, GraphPagingTests.GraphError("Forbidden", "Tenant does not have a valid Intune license."));

        var found = (await Lookup().ReadAsync(Settings(), Secrets, _graph.HttpClients, Token)).Value();

        found.Licences.ShouldBe(["AAD_PREMIUM"]);
        found.ConfigurationTypes.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_permission_names_it()
    {
        _graph.Respond("GET", "/v1.0/subscribedSkus", 403, GraphPagingTests.GraphError("Authorization_RequestDenied", "Insufficient privileges to complete the operation."));

        var problem = (await Lookup().ReadAsync(Settings(), Secrets, _graph.HttpClients, Token)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("m365.graph_forbidden");
        problem.Message.ShouldContain(EnvironmentLookup.LicencePermission);
    }

    [Fact]
    public async Task Settings_are_checked_as_the_collector_checks_them()
    {
        var settings = new Dictionary<string, string> { ["client_id"] = "00000000-0000-0000-0000-000000000002", ["supported_windows_builds"] = "10.0.26100" };

        var problems = (await Lookup().ReadAsync(new CollectorSettings(settings), Secrets, _graph.HttpClients, Token)).Problems();

        problems.ShouldContain(p => p.Message.Contains("tenant_id", StringComparison.Ordinal));
        _graph.Server.LogEntries.ShouldBeEmpty();
    }

    public void Dispose() => _graph.Dispose();

    private static IReadOnlyDictionary<string, Secret> Secrets { get; } = new Dictionary<string, Secret> { ["client_secret"] = new("example-client-secret-value-1b8e") };

    private static EnvironmentLookup Lookup() => new(new TestCredentialFactory(), new RecordingWait());

    private CollectorSettings Settings() => new(new Dictionary<string, string>
    {
        ["tenant_id"] = "00000000-0000-0000-0000-000000000001",
        ["client_id"] = "00000000-0000-0000-0000-000000000002",
        ["graph_url"] = _graph.Url.ToString(),
        ["supported_windows_builds"] = "10.0.26100",
    });

    private static string Collection(params JsonObject[] items) => new JsonObject { ["value"] = new JsonArray([.. items]) }.ToJsonString();
}
