using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Onboarding.Specs;

/// <summary>What an API specification says a profile can use: the endpoints that read, how to sign in, and how lists are paged.</summary>
public sealed record ApiSummary(string Title, IReadOnlyList<string> Servers, IReadOnlyList<ApiEndpoint> Endpoints, IReadOnlyList<ApiSignIn> SignIn, int Writes)
{
    /// <summary>The summary as plain lines: what a model reads and the person sees. The specification itself is never sent.</summary>
    public string Text()
    {
        var text = new StringBuilder();
        text.Append(Title).Append('\n');
        foreach (var server in Servers)
        {
            text.Append("Server: ").Append(server).Append('\n');
        }

        foreach (var signIn in SignIn)
        {
            text.Append("Sign-in: ").Append(signIn.Name).Append(": ").Append(signIn.Description).Append('\n');
        }

        foreach (var endpoint in Endpoints)
        {
            text.Append(endpoint.Method).Append(' ').Append(endpoint.Path);
            if (endpoint.Paging is not null)
            {
                text.Append(" (paged by ").Append(endpoint.Paging).Append(')');
            }

            text.Append(endpoint.Summary is null ? "" : $": {endpoint.Summary}").Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }
}

/// <summary>An endpoint that reads, with its paging parameters when it has any.</summary>
public sealed record ApiEndpoint(string Method, string Path, string? Summary, string? Paging);

/// <summary>A way to sign in, from the specification's security schemes.</summary>
public sealed record ApiSignIn(string Name, string Description);

/// <summary>Reads an OpenAPI document (JSON or YAML, version 3) into an <see cref="ApiSummary"/>. Pure.</summary>
public static class SpecReader
{
    private static readonly string[] Reads = ["get"];
    private static readonly string[] Writing = ["put", "post", "delete", "patch"];
    private static readonly string[] PagingNames =
        ["page", "pagesize", "page_size", "pagetoken", "page_token", "pagefromkey", "nextpagetoken", "cursor", "after", "limit", "offset", "skip", "top", "$top", "$skip", "per_page"];

    public static Result<ApiSummary> Read(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);
        JsonObject? root;
        try
        {
            root = ToJson(text) as JsonObject;
        }
        catch (YamlException e)
        {
            return Result.Fail<ApiSummary>(new Problem("spec.unreadable", $"{source} is neither JSON nor YAML: {e.Message}"));
        }

        if (root?["openapi"] is null || root["paths"] is not JsonObject paths)
        {
            return Result.Fail<ApiSummary>(new Problem("spec.not_openapi", $"{source} is not an OpenAPI 3 document: it has no 'openapi' version and 'paths'. Save the vendor's OpenAPI document, or describe the API in the session."));
        }

        var endpoints = paths
            .Where(p => p.Value is JsonObject)
            .SelectMany(p => ((JsonObject)p.Value!).Where(o => Reads.Contains(o.Key.ToLowerInvariant()) && o.Value is JsonObject)
                .Select(o => Endpoint(o.Key, p.Key, (JsonObject)o.Value!, (JsonObject)p.Value!)))
            .ToArray();
        var writes = paths.Where(p => p.Value is JsonObject).Sum(p => ((JsonObject)p.Value!).Count(o => Writing.Contains(o.Key.ToLowerInvariant())));
        return Result.Ok(new ApiSummary(
            Text(root["info"]?["title"]) ?? source,
            [.. (root["servers"] as JsonArray ?? []).Select(s => Text(s?["url"])).OfType<string>()],
            endpoints,
            [.. (root["components"]?["securitySchemes"] as JsonObject ?? []).Select(s => new ApiSignIn(s.Key, Describe(s.Value as JsonObject ?? [])))],
            writes));
    }

    private static ApiEndpoint Endpoint(string method, string path, JsonObject operation, JsonObject pathItem)
    {
        var parameters = (operation["parameters"] as JsonArray ?? []).Concat(pathItem["parameters"] as JsonArray ?? [])
            .Where(p => Text(p?["in"]) == "query")
            .Select(p => Text(p?["name"]))
            .OfType<string>()
            .Where(name => PagingNames.Contains(name.ToLowerInvariant()))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new ApiEndpoint(method.ToUpperInvariant(), path, Text(operation["summary"]) ?? Text(operation["description"]), parameters.Length == 0 ? null : string.Join(", ", parameters));
    }

    private static string Describe(JsonObject scheme)
    {
        var type = Text(scheme["type"]) ?? "unknown";
        return type switch
        {
            "oauth2" when scheme["flows"] is JsonObject flows => string.Join("; ", flows.Select(f => $"oauth2 {Flow(f.Key)}{(Text(f.Value?["tokenUrl"]) is { } token ? $", token at {token}" : "")}")),
            "apiKey" => $"apiKey in {Text(scheme["in"])} {Text(scheme["name"])}",
            "http" => $"http {Text(scheme["scheme"])}",
            _ => type,
        };
    }

    private static string Flow(string flow) => flow switch
    {
        "clientCredentials" => "client credentials",
        "authorizationCode" => "authorization code",
        _ => flow,
    };

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text.Trim() : null;

    /// <summary>JSON is YAML, so one parser reads both; scalars stay strings.</summary>
    private static JsonNode? ToJson(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        return stream.Documents.Count == 0 ? null : ToJson(stream.Documents[0].RootNode);
    }

    private static JsonNode? ToJson(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children
            .Where(c => c.Key is YamlScalarNode { Value: not null })
            .GroupBy(c => ((YamlScalarNode)c.Key).Value!, StringComparer.Ordinal)
            .Select(g => KeyValuePair.Create(g.Key, ToJson(g.First().Value)))),
        YamlSequenceNode list => new JsonArray([.. list.Children.Select(ToJson)]),
        YamlScalarNode { Value: { } value } => JsonValue.Create(value),
        _ => null,
    };
}
