using Microsoft.Extensions.Configuration;

namespace EvidenceHound.Cli;

/// <summary>
/// Docker's <c>_FILE</c> convention: <c>NAME_FILE=/run/secrets/x</c> gives <c>NAME</c> the file's contents (one trailing
/// newline trimmed), so a secret such as <c>HOUND_CA_TOKEN</c> comes from a Docker secret and never sits in the environment.
/// A value set directly wins; a missing file sets nothing, so the secret is reported missing where it is used.
/// </summary>
internal static class FileVariables
{
    private static readonly string Suffix = "_FILE";

    public static IReadOnlyDictionary<string, string?> Resolve(IReadOnlyDictionary<string, string?> variables) =>
        variables
            .Where(pair => pair.Key.Length > Suffix.Length && pair.Key.EndsWith(Suffix, StringComparison.Ordinal))
            .Select(pair => (Name: pair.Key[..^Suffix.Length], Path: pair.Value))
            .Where(file => !variables.ContainsKey(file.Name) && File.Exists(file.Path))
            .ToDictionary(file => file.Name, file => (string?)TrimOneNewline(File.ReadAllText(file.Path!)));

    /// <summary>Adds the variables the configuration's <c>_FILE</c> entries point at.</summary>
    public static IConfigurationBuilder AddFileVariables(this IConfigurationBuilder builder)
    {
        var current = builder.Build().AsEnumerable().ToDictionary(pair => pair.Key, pair => pair.Value);
        return builder.AddInMemoryCollection(Resolve(current));
    }

    private static string TrimOneNewline(string text) =>
        text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2]
        : text.EndsWith('\n') ? text[..^1]
        : text;
}
