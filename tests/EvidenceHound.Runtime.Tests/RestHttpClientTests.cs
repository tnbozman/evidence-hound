using System.Net;
using EvidenceHound.Collectors.Rest;
using EvidenceHound.Collectors.Rest.Tests;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.Runtime.Tests;

/// <summary>The rest collector checks every redirect itself, so the client the runtime gives it must not follow redirects.</summary>
public sealed class RestHttpClientTests
{
    [Fact]
    public async Task The_rest_client_hands_redirects_back_to_the_collector()
    {
        using var api = RecordedExample.Start();
        api.Server.Given(Request.Create().WithPath("/moved")).RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", "https://elsewhere.example.com/"));
        await using var services = new ServiceCollection().AddBuiltInCollectors().BuildServiceProvider();
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(RestCollector.TypeName);

        using var response = await client.GetAsync(new Uri(api.Url, "/moved"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }
}
