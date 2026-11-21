using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using TokenCredential = Azure.Core.TokenCredential;

namespace EvidenceHound.Collectors.M365.Tests;

public sealed class GraphPagingTests : IDisposable
{
    private static readonly GraphRequest Users = new("v1.0/users?$select=id,userPrincipalName", "User.Read.All");

    private readonly RecordedApi _graph = RecordedApi.Start();

    private readonly RecordingWait _wait = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Follows_next_link_until_exhausted()
    {
        Page(skiptoken: null, next: "2", "u1", "u2");
        Page(skiptoken: "2", next: "3", "u3", "u4");
        Page(skiptoken: "3", next: null, "u5");

        var collection = (await Client().GetAllAsync(Users, Token)).Value();

        collection.Pages.ShouldBe(3);
        collection.Items.Select(i => i!["id"]!.GetValue<string>()).ShouldBe(["u1", "u2", "u3", "u4", "u5"]);
        _graph.Server.LogEntries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Sends_the_token_as_a_bearer_header()
    {
        Page(skiptoken: null, next: null, "u1");

        await Client().GetAllAsync(Users, Token);

        var authorization = _graph.Server.LogEntries.ShouldHaveSingleItem().RequestMessage!.Headers!["Authorization"].ShouldHaveSingleItem();
        authorization.ShouldBe(new AuthenticationHeaderValue("Bearer", TestCredentials.Token).ToString());
    }

    [Fact]
    public async Task Waits_retry_after_on_429()
    {
        var request = Request.Create().WithPath("/v1.0/users").UsingGet();
        _graph.Server.Given(request).InScenario("throttle").WillSetStateTo("open")
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "7").WithBody(GraphError("TooManyRequests", "Too many requests.")));
        _graph.Server.Given(request).InScenario("throttle").WhenStateIs("open")
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(Body(null, "u1")));

        var collection = (await Client().GetAllAsync(Users, Token)).Value();

        _wait.Delays.ShouldBe([TimeSpan.FromSeconds(7)]);
        collection.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Waits_retry_after_on_503()
    {
        var request = Request.Create().WithPath("/v1.0/users").UsingGet();
        _graph.Server.Given(request).InScenario("busy").WillSetStateTo("open")
            .RespondWith(Response.Create().WithStatusCode(503).WithHeader("Retry-After", "3"));
        _graph.Server.Given(request).InScenario("busy").WhenStateIs("open")
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(Body(null, "u1")));

        (await Client().GetAllAsync(Users, Token)).Value().Items.Count.ShouldBe(1);
        _wait.Delays.ShouldBe([TimeSpan.FromSeconds(3)]);
    }

    [Fact]
    public async Task Gives_up_after_repeated_throttling()
    {
        _graph.Server.Given(Request.Create().WithPath("/v1.0/users").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "1"));

        var problem = (await Client().GetAllAsync(Users, Token)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("m365.graph_throttled");
        _graph.Server.LogEntries.Count.ShouldBe(GraphClient.MaxAttempts);
        _wait.Delays.Count.ShouldBe(GraphClient.MaxAttempts - 1);
    }

    [Fact]
    public async Task Refuses_a_next_link_to_another_host()
    {
        _graph.Respond("GET", "/v1.0/users", 200, new JsonObject
        {
            ["value"] = new JsonArray(new JsonObject { ["id"] = "u1" }),
            ["@odata.nextLink"] = "https://graph.example.net/v1.0/users?$skiptoken=2",
        }.ToJsonString());

        var problem = (await Client().GetAllAsync(Users, Token)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("m365.graph_next_link_foreign");
        problem.Message.ShouldContain("graph.example.net");
    }

    [Fact]
    public async Task Forbidden_names_the_permission_needed()
    {
        _graph.Respond("GET", "/v1.0/users", 403, GraphError("Authorization_RequestDenied", "Insufficient privileges to complete the operation."));

        var problem = (await Client().GetAllAsync(Users, Token)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("m365.graph_forbidden");
        problem.Message.ShouldContain("User.Read.All");
    }

    [Fact]
    public async Task Unauthorized_is_a_problem()
    {
        _graph.Respond("GET", "/v1.0/users", 401, GraphError("InvalidAuthenticationToken", "Access token validation failure."));

        (await Client().GetAllAsync(Users, Token)).Problems().ShouldHaveSingleItem().Code.ShouldBe("m365.graph_unauthorized");
    }

    [Fact]
    public async Task Missing_licence_is_its_own_problem()
    {
        _graph.Respond("GET", "/v1.0/users", 400, GraphError("AadPremiumLicenseRequired", "The tenant needs an AAD Premium 2 license."));

        (await Client().GetAllAsync(Users, Token)).Problems().ShouldHaveSingleItem().Code.ShouldBe(GraphClient.NotLicensed);
    }

    [Fact]
    public async Task Refused_credentials_are_a_problem_not_an_exception()
    {
        var problem = (await Client(TestCredentials.Refused).GetAllAsync(Users, Token)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("m365.auth_failed");
        _graph.Server.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_json_is_a_problem()
    {
        _graph.Respond("GET", "/v1.0/users", 200, "<html>not json</html>", "text/html");

        (await Client().GetAllAsync(Users, Token)).Problems().ShouldHaveSingleItem().Code.ShouldBe("m365.graph_invalid_response");
    }

    public void Dispose() => _graph.Dispose();

    internal static string GraphError(string code, string message) =>
        new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();

    private GraphClient Client(TokenCredential? credential = null) =>
        new(_graph.HttpClients.CreateClient("m365"), credential ?? TestCredentials.Working, _graph.Url, _wait);

    private void Page(string? skiptoken, string? next, params string[] ids)
    {
        var request = Request.Create().WithPath("/v1.0/users").UsingGet();
        request = skiptoken is null ? request : request.WithParam("$skiptoken", skiptoken);
        _graph.Server.Given(request).AtPriority(skiptoken is null ? 10 : 1)
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(Body(next, ids)));
    }

    private string Body(string? next, params string[] ids)
    {
        var page = new JsonObject { ["value"] = new JsonArray([.. ids.Select(id => (JsonNode)new JsonObject { ["id"] = id })]) };
        if (next is not null)
        {
            page["@odata.nextLink"] = new Uri(_graph.Url, $"v1.0/users?$select=id,userPrincipalName&$skiptoken={next}").ToString();
        }

        return page.ToJsonString();
    }
}
