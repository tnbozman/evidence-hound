using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>Repository files copied next to the test assembly.</summary>
internal static class RepoFiles
{
    public static string ExampleMapping => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "mappings", "example.yaml"));

    public static string RepeatedLibrary => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "frameworks", "example-repeated-library.yaml"));

    public static string MappingSchema => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "schemas", "mapping.schema.json"));

    public static T Value<T>(this Result<T> result)
        where T : notnull =>
        result.Match(value => value, problems => throw new InvalidOperationException(string.Join("; ", problems.Select(p => p.Message))));

    public static IReadOnlyList<Problem> Problems<T>(this Result<T> result)
        where T : notnull =>
        result.Match(_ => [], problems => problems);
}
