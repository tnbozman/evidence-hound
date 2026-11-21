using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Tests;

/// <summary>Repository files copied next to the test assembly, and result helpers.</summary>
internal static class TestFiles
{
    public static string ExampleConfigPath => Path.Combine(AppContext.BaseDirectory, "files", "examples", "example-pty-ltd.yaml");

    public static string ConfigSchema => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "schemas", "config.schema.json"));

    public static T Value<T>(this Result<T> result)
        where T : notnull =>
        result.Match(value => value, problems => throw new InvalidOperationException(string.Join("; ", problems.Select(p => p.Message))));

    public static IReadOnlyList<Problem> Problems<T>(this Result<T> result)
        where T : notnull =>
        result.Match(_ => [], problems => problems);
}
