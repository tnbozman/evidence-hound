using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceHound.Functional.Tests;

[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class ClientFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddCisoAssistantClient(new CisoAssistantOptions(ciso.ApiUrl, ciso.Token))
        .BuildServiceProvider();

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task Client_updates_requirement_assessment_on_real_instance()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _services.GetRequiredService<ICisoAssistantClient>();

        var row = Value(await client.FindRequirementAssessmentsAsync("EXAMPLE-BASELINE-2026", RequirementKey.Ref("EX-1.2"), ct)).ShouldHaveSingleItem();
        row.RefId.ShouldBe("EX-1.2");
        row.Urn.ShouldStartWith("urn:example:risk:req_node:example-baseline");
        var id = row.Id;
        Value(await client.UpdateRequirementAssessmentAsync(
            id, new RequirementAssessmentValues(ComplianceResult.PartiallyCompliant, "Set by a functional test.", []), ct));

        var stored = await Get($"requirement-assessments/{id}/", ct);
        stored["result"]!.GetValue<string>().ShouldBe("partially_compliant");
        stored["observation"]!.GetValue<string>().ShouldBe("Set by a functional test.");
    }

    private static T Value<T>(Result<T> result)
        where T : notnull =>
        result.Match(value => value, problems => throw new ShouldAssertException(string.Join("; ", problems.Select(p => $"{p.Code}: {p.Message}"))));

    private async Task<JsonNode> Get(string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
    }
}
