using System.Net;

namespace EvidenceHound.Collectors.Testing.Tests;

public class RecordedApiTests
{
    private static readonly string RecordedWidgets = Path.Combine(AppContext.BaseDirectory, "recorded", "widgets.json");

    [Fact]
    public async Task Serves_a_recorded_response_file_to_its_http_clients()
    {
        using var api = RecordedApi.Start().RespondWithFile("GET", "/api/widgets", RecordedWidgets);
        using var client = api.HttpClients.CreateClient("widget");

        using var response = await client.GetAsync(new Uri("api/widgets", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(await File.ReadAllTextAsync(RecordedWidgets, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Serves_an_inline_response_with_its_status()
    {
        using var api = RecordedApi.Start().Respond("POST", "/api/login", 401, "{\"detail\":\"Invalid token.\"}");
        using var client = api.HttpClients.CreateClient("widget");

        using var response = await client.PostAsync(new Uri($"{api.Url}api/login"), null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("Invalid token.");
    }

    [Fact]
    public async Task Request_nothing_was_recorded_for_gets_404()
    {
        using var api = RecordedApi.Start();
        using var client = api.HttpClients.CreateClient("widget");

        using var response = await client.GetAsync(new Uri("api/unknown", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Offline_clients_fail_every_request()
    {
        using var client = OfflineHttpClients.Instance.CreateClient("widget");

        var failure = await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync(new Uri("https://widgets.example.com/api"), TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("offline");
    }
}
