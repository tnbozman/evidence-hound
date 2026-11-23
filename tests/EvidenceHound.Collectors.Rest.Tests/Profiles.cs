using EvidenceHound.Collectors.Rest.Profiles;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>The library profiles copied next to the tests, and small hand-written profiles for focused tests.</summary>
internal static class Profiles
{
    public static string LibraryFolder => Path.Combine(AppContext.BaseDirectory, "profiles");

    public static string ExampleYaml => File.ReadAllText(Path.Combine(LibraryFolder, "example.yaml"));

    public static Profile Example => ProfileParser.Parse(ExampleYaml, "example.yaml").Value();

    /// <summary>A minimal valid profile with one query and one check; <paramref name="extra"/> is appended as more YAML.</summary>
    public static string Minimal(string extra = "", string checks = DefaultChecks) => $$"""
        profile: minimal
        version: "1.0.0"
        parameters:
          base_url: { type: url, default: "https://api.example.com/" }
        secrets:
          token: { required: true }
        connection:
          base_url: ${base_url}
          allowed_hosts: [api.example.com]
          auth: { type: bearer, token: "${secrets.token}" }
        queries:
          things:
            get: v1/things
            items: $.items
        {{checks}}
        {{extra}}
        """;

    public const string DefaultChecks = """
        checks:
          - id: example.thing.enabled
            over: things
            each: { field: $.enabled, equals: true }
            require: all
            when_empty: not_applicable
        """;
}
