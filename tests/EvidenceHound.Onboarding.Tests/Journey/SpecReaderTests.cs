using EvidenceHound.Onboarding.Specs;

namespace EvidenceHound.Onboarding.Tests.Journey;

/// <summary>An API specification is read into a short summary: endpoints that read, sign-in and paging. Only the summary goes to a model.</summary>
public sealed class SpecReaderTests
{
    internal static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("No global.json above the test folder.");
        }
    }

    internal static string ExampleSpec => Path.Combine(RepoRoot, "examples", "apis", "example-asset-api.openapi.yaml");

    [Fact]
    public void Reads_openapi_paths_and_security_schemes()
    {
        var summary = SpecReader.Read(File.ReadAllText(ExampleSpec), "example-asset-api.openapi.yaml").Value();

        summary.Title.ShouldBe("Example Asset API");
        summary.Servers.ShouldBe(["https://api.example.com/"]);
        summary.Endpoints.Select(e => $"{e.Method} {e.Path}").ShouldBe(["GET /v1/devices", "GET /v1/devices/{id}", "GET /v1/users", "GET /v1/roles"]);
        summary.Endpoints.Single(e => e.Path == "/v1/devices").Paging.ShouldBe("pageToken");
        summary.Endpoints.Single(e => e.Path == "/v1/users").Paging.ShouldBe("limit, offset");
        summary.Endpoints.Single(e => e.Path == "/v1/roles").Paging.ShouldBeNull();
        summary.Endpoints[0].Summary.ShouldBe("List devices with their operating system and last check-in.");
        summary.Writes.ShouldBe(1);
        var scheme = summary.SignIn.ShouldHaveSingleItem();
        scheme.Name.ShouldBe("clientCredentials");
        scheme.Description.ShouldBe("oauth2 client credentials, token at https://login.example.com/oauth2/token");
    }

    [Fact]
    public void Reads_a_json_openapi_document()
    {
        var spec = """{"openapi":"3.1.0","info":{"title":"Example JSON API"},"paths":{"/items":{"get":{"summary":"Items."}}},"components":{"securitySchemes":{"key":{"type":"apiKey","in":"header","name":"X-Api-Key"}}}}""";

        var summary = SpecReader.Read(spec, "api.json").Value();

        summary.Endpoints.ShouldHaveSingleItem().Path.ShouldBe("/items");
        summary.SignIn.ShouldHaveSingleItem().Description.ShouldBe("apiKey in header X-Api-Key");
    }

    [Fact]
    public void A_file_that_is_not_openapi_is_a_problem()
    {
        SpecReader.Read("title: not an api\n", "notes.yaml").Problems().ShouldHaveSingleItem().Code.ShouldBe("spec.not_openapi");
        SpecReader.Read("{ not: [valid", "broken.yaml").Problems().ShouldHaveSingleItem().Code.ShouldBe("spec.unreadable");
    }

    [Fact]
    public void Summary_text_lists_each_endpoint_once()
    {
        var text = SpecReader.Read(File.ReadAllText(ExampleSpec), "example").Value().Text();

        text.ShouldContain("GET /v1/devices (paged by pageToken): List devices");
        text.ShouldContain("Sign-in: clientCredentials: oauth2 client credentials");
        text.ShouldNotContain("POST");
    }
}
