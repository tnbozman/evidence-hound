using EvidenceHound.Collectors.Testing;

namespace EvidenceHound.Collectors.Sophos.Tests;

public sealed class SophosPagingTests : IDisposable
{
    private readonly RecordedSophos _sophos = RecordedSophos.Start();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_all_pages()
    {
        var endpoints = (await Client().GetAllAsync(SophosSources.Endpoints, Token)).Value();

        endpoints.Pages.ShouldBe(2);
        endpoints.Items.Count.ShouldBe(5);
        _sophos.Region.LogEntries.Count(e => e.RequestMessage!.Path == "/endpoint/v1/endpoints").ShouldBe(2);
        _sophos.Region.LogEntries[^1].RequestMessage!.Query!["pageFromKey"].ShouldBe(["page2-key-7f1c"]);
    }

    [Fact]
    public async Task Reads_numbered_pages_until_the_total()
    {
        using var api = RecordedApi.Start();
        api.Server.Given(WireMock.RequestBuilders.Request.Create().WithPath("/firewall/v1/firewalls").UsingGet()).AtPriority(10)
            .RespondWith(Page("""{"items":[{"id":"f1"},{"id":"f2"}],"pages":{"current":1,"size":2,"total":2,"items":3,"maxSize":100}}"""));
        api.Server.Given(WireMock.RequestBuilders.Request.Create().WithPath("/firewall/v1/firewalls").WithParam("page", "2").UsingGet()).AtPriority(1)
            .RespondWith(Page("""{"items":[{"id":"f3"}],"pages":{"current":2,"size":2,"total":2,"items":3,"maxSize":100}}"""));

        var firewalls = (await new SophosClient(new HttpClient(), Session(api.Url)).GetAllAsync(SophosSources.Firewalls, Token)).Value();

        firewalls.Pages.ShouldBe(2);
        firewalls.Items.Select(i => i!["id"]!.GetValue<string>()).ShouldBe(["f1", "f2", "f3"]);
        api.Server.LogEntries[0].RequestMessage!.Query!["pageTotal"].ShouldBe(["true"]);
    }

    [Fact]
    public async Task Sends_the_token_and_tenant_on_every_request()
    {
        await Client().GetAllAsync(SophosSources.Endpoints, Token);

        _sophos.Region.LogEntries.ShouldAllBe(e =>
            e.RequestMessage!.Headers!["X-Tenant-ID"].Single() == RecordedSophos.TenantId
            && e.RequestMessage.Headers["Authorization"].Single() == $"Bearer {RecordedSophos.Token}");
    }

    [Fact]
    public async Task Forbidden_names_the_role_needed()
    {
        _sophos.FailRegion("/endpoint/v1/endpoints", 403, "forbidden", "Forbidden");

        var problem = (await Client().GetAllAsync(SophosSources.Endpoints, Token)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("sophos.forbidden");
        problem.Message.ShouldContain("Service Principal Read-Only");
    }

    [Fact]
    public async Task Other_errors_are_a_problem()
    {
        _sophos.FailRegion("/firewall/v1/firewalls", 500, "internalServerError", "Something went wrong");

        (await Client().GetAllAsync(SophosSources.Firewalls, Token)).Problems().ShouldHaveSingleItem().Code.ShouldBe("sophos.request_failed");
    }

    [Fact]
    public async Task Invalid_json_is_a_problem()
    {
        _sophos.Region.Given(WireMock.RequestBuilders.Request.Create().WithPath("/endpoint/v1/endpoints").UsingGet()).AtPriority(0)
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithStatusCode(200).WithBody("<html>not json</html>"));

        (await Client().GetAllAsync(SophosSources.Endpoints, Token)).Problems().ShouldHaveSingleItem().Code.ShouldBe("sophos.invalid_response");
    }

    public void Dispose() => _sophos.Dispose();

    private static WireMock.ResponseBuilders.IResponseBuilder Page(string body) =>
        WireMock.ResponseBuilders.Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body);

    private static SophosSession Session(Uri region) => new(RecordedSophos.Token, RecordedSophos.TenantId, region);

    private SophosClient Client() => new(new HttpClient(), Session(_sophos.RegionUrl));
}
