using System.Text.Json.Nodes;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Sophos.Tests;

public sealed class SophosAuthTests : IDisposable
{
    private RecordedSophos _sophos = RecordedSophos.Start();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenant_credential_uses_whoami_data_region()
    {
        var session = (await SignInAsync()).Value();

        session.TenantId.ShouldBe(RecordedSophos.TenantId);
        session.DataRegion.ShouldBe(new Uri(_sophos.RegionUrl.ToString().TrimEnd('/') + "/"));
        session.Token.ShouldBe(RecordedSophos.Token);
        _sophos.Api.LogEntries.Select(e => e.RequestMessage!.Path).ShouldBe(["/whoami/v1"]);
    }

    [Fact]
    public async Task Token_request_is_a_client_credentials_grant()
    {
        await SignInAsync();

        var request = _sophos.Id.LogEntries.ShouldHaveSingleItem().RequestMessage!;
        request.Method.ShouldBe("POST");
        var form = System.Web.HttpUtility.ParseQueryString(request.Body!);
        form["grant_type"].ShouldBe("client_credentials");
        form["client_id"].ShouldBe(RecordedSophos.ClientId);
        form["client_secret"].ShouldBe(RecordedSophos.ClientSecret);
        form["scope"].ShouldBe("token");
    }

    [Fact]
    public async Task Whoami_sends_the_bearer_token()
    {
        await SignInAsync();

        _sophos.Api.LogEntries.ShouldHaveSingleItem().RequestMessage!.Headers!["Authorization"].ShouldBe([$"Bearer {RecordedSophos.Token}"]);
    }

    [Fact]
    public async Task Partner_credential_without_tenant_id_fails_clearly()
    {
        Use("partner");

        var problem = (await SignInAsync()).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("sophos.tenant_id_required");
        problem.Message.ShouldContain("partner");
        problem.Message.ShouldContain("tenant_id");
    }

    [Fact]
    public async Task Organization_credential_without_tenant_id_fails_clearly()
    {
        Use("organization");

        var problem = (await SignInAsync()).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("sophos.tenant_id_required");
        problem.Message.ShouldContain("organization");
    }

    [Theory]
    [InlineData("partner", "X-Partner-ID", "00000000-0000-4000-8000-0000000050b1")]
    [InlineData("organization", "X-Organization-ID", "00000000-0000-4000-8000-0000000050c1")]
    public async Task Partner_and_organization_credentials_find_the_tenant_data_region(string credential, string header, string id)
    {
        Use(credential);

        var session = (await SignInAsync(RecordedSophos.TenantId)).Value();

        session.TenantId.ShouldBe(RecordedSophos.TenantId);
        session.DataRegion.Port.ShouldBe(_sophos.RegionUrl.Port);
        var lookup = _sophos.Api.LogEntries.Single(e => e.RequestMessage!.Path.Contains("/tenants/", StringComparison.Ordinal)).RequestMessage!;
        lookup.Headers![header].ShouldBe([id]);
    }

    [Fact]
    public async Task Tenant_credential_for_another_tenant_id_fails()
    {
        var problem = (await SignInAsync("00000000-0000-4000-8000-0000000050ff")).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("sophos.tenant_mismatch");
    }

    [Fact]
    public async Task Refused_credential_is_a_problem()
    {
        _sophos.RefuseToken();

        var problem = (await SignInAsync()).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("sophos.auth_failed");
        problem.Message.ShouldContain("oauth.invalid_client_secret");
        problem.Message.ShouldNotContain(RecordedSophos.ClientSecret);
        _sophos.Api.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Data_region_on_an_untrusted_host_is_refused()
    {
        _sophos.Whoami(new JsonObject
        {
            ["id"] = RecordedSophos.TenantId,
            ["idType"] = "tenant",
            ["apiHosts"] = new JsonObject { ["global"] = _sophos.ApiUrl.ToString(), ["dataRegion"] = "https://api.example.net" },
        });

        var problem = (await SignInAsync()).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("sophos.data_region_untrusted");
        problem.Message.ShouldContain("api.example.net");
    }

    [Theory]
    [InlineData("https://api-eu02.central.sophos.com", true)]
    [InlineData("https://api.central.sophos.com", true)]
    [InlineData("http://api-eu02.central.sophos.com", false)]
    [InlineData("https://api-eu02.central.sophos.com.example.net", false)]
    [InlineData("https://notcentral.sophos.com", false)]
    public void Data_region_must_be_a_sophos_central_host(string region, bool trusted)
    {
        SophosAuth.IsTrustedRegion(new Uri(region), new Uri("https://api.central.sophos.com/")).ShouldBe(trusted);
    }

    public void Dispose() => _sophos.Dispose();

    private void Use(string credential)
    {
        _sophos.Dispose();
        _sophos = RecordedSophos.Start(credential);
    }

    private Task<Core.Results.Result<SophosSession>> SignInAsync(string? tenantId = null) =>
        SophosAuth.SignInAsync(
            _sophos.HttpClients.CreateClient("sophos"),
            new SophosConnection(RecordedSophos.ClientId, new Secret(RecordedSophos.ClientSecret), tenantId, _sophos.IdUrl, _sophos.ApiUrl),
            Token);
}
