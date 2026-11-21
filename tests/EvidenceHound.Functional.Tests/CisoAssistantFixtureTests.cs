using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace EvidenceHound.Functional.Tests;

[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class CisoAssistantFixtureTests(CisoAssistantFixture ciso)
{
    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        using var http = new HttpClient();

        using var response = await http.GetAsync(new Uri(ciso.ApiUrl, "health/"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument.Parse(body).RootElement.GetProperty("status").GetString().ShouldBe("ok");
    }

    [Fact]
    public async Task Token_can_list_folders()
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, "folders/"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);

        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var names = JsonDocument.Parse(body).RootElement.GetProperty("results").EnumerateArray()
            .Select(folder => folder.GetProperty("name").GetString());
        names.ShouldContain("Example Pty Ltd");
    }
}
