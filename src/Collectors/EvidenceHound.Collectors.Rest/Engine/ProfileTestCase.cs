using System.Globalization;
using EvidenceHound.Core.Results;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>One recorded answer: the request it answers (method, path, and the query values it requires) and the response.</summary>
public sealed record RecordedResponse(string Method, string Path, IReadOnlyDictionary<string, string> Query, int Status, string? File, string? Body);

/// <summary>
/// One test case of a profile, read from <c>&lt;profile&gt;.tests/&lt;case&gt;.yaml</c>: the instance's settings and (fake) secrets, the
/// run's clock, the recorded answers, and the result each check must give.
/// </summary>
public sealed record ProfileTestCase(
    string Name,
    string Description,
    DateTimeOffset Now,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> Secrets,
    IReadOnlyList<RecordedResponse> Responses,
    IReadOnlyDictionary<string, string> Expect)
{
    /// <summary>The result names a case can expect.</summary>
    public static IReadOnlyList<string> Results { get; } = ["pass", "fail", "partial", "not_applicable", "unable_to_assess"];

    private static readonly string[] Keys = ["description", "now", "settings", "secrets", "responses", "expect"];

    private static readonly string[] Methods = ["get", "post", "put", "patch", "delete"];

    private static readonly string[] ResponseKeys = [.. Methods, "query", "status", "file", "body"];

    public static Result<ProfileTestCase> Parse(string yaml, string name)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            return stream.Documents is [{ RootNode: YamlMappingNode root }]
                ? Read(root, name)
                : Invalid(name, "the file must hold one map of keys.");
        }
        catch (YamlException ex)
        {
            return Invalid(name, $"line {ex.Start.Line}: {ex.Message}");
        }
    }

    private static Result<ProfileTestCase> Read(YamlMappingNode root, string name)
    {
        var problems = new List<string>();
        problems.AddRange(Unknown(root, Keys).Select(k => $"unknown key '{k}'; use {string.Join(", ", Keys)}."));
        var now = Text(root, "now") is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;
        if (now is null)
        {
            problems.Add("'now' is required: the run's clock, for example 2026-11-22T02:00:00Z.");
        }

        var expect = Map(root, "expect", problems);
        problems.AddRange(expect.Where(p => !Results.Contains(p.Value, StringComparer.Ordinal))
            .Select(p => $"expect '{p.Key}: {p.Value}' is not a result; use {string.Join(", ", Results)}."));
        if (expect.Count == 0)
        {
            problems.Add("'expect' must name at least one check and its result.");
        }

        var responses = Child(root, "responses") switch
        {
            null => [],
            YamlSequenceNode list => list.Children.Select(node => Response(node, problems)).OfType<RecordedResponse>().ToList(),
            _ => Fail<List<RecordedResponse>>(problems, "'responses' must be a list.", []),
        };

        return problems.Count > 0
            ? Invalid(name, string.Join(" ", problems))
            : Result.Ok(new ProfileTestCase(name, Text(root, "description") ?? "", now!.Value, Map(root, "settings", problems), Map(root, "secrets", problems), responses, expect));
    }

    private static RecordedResponse? Response(YamlNode node, List<string> problems)
    {
        if (node is not YamlMappingNode map)
        {
            return Fail<RecordedResponse?>(problems, "each response must be a map such as { get: /v1/things, file: recordings/things.json }.", null);
        }

        problems.AddRange(Unknown(map, ResponseKeys).Select(k => $"unknown response key '{k}'; use {string.Join(", ", ResponseKeys)}."));
        var methods = Methods.Where(m => Child(map, m) is not null).ToArray();
        if (methods is not [var method] || Text(map, method) is not { Length: > 0 } path)
        {
            return Fail<RecordedResponse?>(problems, "each response names one method and path, such as get: /v1/things.", null);
        }

        var status = Text(map, "status") is { } s ? int.TryParse(s, CultureInfo.InvariantCulture, out var code) ? code : -1 : 200;
        if (status is < 100 or > 599)
        {
            return Fail<RecordedResponse?>(problems, $"the response to {path} has a status that is not an HTTP status.", null);
        }

        var file = Text(map, "file");
        if (file is not null && (Path.IsPathRooted(file) || file.Split('/', '\\').Contains("..")))
        {
            return Fail<RecordedResponse?>(problems, $"the response file '{file}' must be inside the tests folder.", null);
        }

        return new RecordedResponse(method.ToUpperInvariant(), path, Map(map, "query", problems), status, file, Text(map, "body"));
    }

    private static Dictionary<string, string> Map(YamlMappingNode map, string key, List<string> problems)
    {
        switch (Child(map, key))
        {
            case null:
                return new(StringComparer.Ordinal);
            case YamlMappingNode values when values.Children.All(p => p is { Key: YamlScalarNode, Value: YamlScalarNode })
                && values.Children.All(p => ((YamlScalarNode)p.Key).Value is not null && ((YamlScalarNode)p.Value).Value is not null):
                return values.Children.ToDictionary(p => ((YamlScalarNode)p.Key).Value!, p => ((YamlScalarNode)p.Value).Value!, StringComparer.Ordinal);
            default:
                problems.Add($"'{key}' must be a map of text values.");
                return new(StringComparer.Ordinal);
        }
    }

    private static IEnumerable<string> Unknown(YamlMappingNode map, string[] known) =>
        map.Children.Keys.OfType<YamlScalarNode>().Select(k => k.Value ?? "").Where(k => !known.Contains(k, StringComparer.Ordinal));

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static string? Text(YamlMappingNode map, string key) => Child(map, key) is YamlScalarNode { Value: { } value } ? value : null;

    private static T Fail<T>(List<string> problems, string problem, T value)
    {
        problems.Add(problem);
        return value;
    }

    private static Result<ProfileTestCase> Invalid(string name, string message) =>
        Result.Fail<ProfileTestCase>(new Problem("profile.test_invalid", $"Test case '{name}': {message}"));
}
