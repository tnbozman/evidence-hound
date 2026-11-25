using EvidenceHound.Runtime.Secrets;
using Microsoft.Extensions.Configuration;

namespace EvidenceHound.Cli;

/// <summary>
/// Docker's <c>_FILE</c> convention: <c>NAME_FILE=/run/secrets/x</c> gives <c>NAME</c> the file's contents (one trailing
/// newline trimmed), so a secret such as <c>HOUND_CA_TOKEN</c> comes from a Docker secret and never sits in the environment.
/// A value set directly wins; a missing or unreadable file sets nothing, so the secret is reported missing where it is used.
/// Other tools use the same suffix (<c>COMPOSE_FILE</c>, diagnostics logs), so their files must never stop hound.
/// </summary>
internal static class FileVariables
{
    private static readonly string Suffix = "_FILE";

    public static IReadOnlyDictionary<string, string?> Resolve(IReadOnlyDictionary<string, string?> variables) =>
        Resolve(variables, FileSecretProvider.ReadSecretFile);

    internal static IReadOnlyDictionary<string, string?> Resolve(IReadOnlyDictionary<string, string?> variables, Func<string, string> read) =>
        variables
            .Where(pair => pair.Key.Length > Suffix.Length && pair.Key.EndsWith(Suffix, StringComparison.Ordinal))
            .Select(pair => (Name: pair.Key[..^Suffix.Length], Path: pair.Value))
            .Where(file => !variables.ContainsKey(file.Name) && File.Exists(file.Path))
            .Select(file => (file.Name, Value: TryRead(file.Path!, read)))
            .Where(file => file.Value is not null)
            .ToDictionary(file => file.Name, file => file.Value);

    private static string? TryRead(string path, Func<string, string> read)
    {
        try
        {
            return read(path);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Adds the variables the configuration's <c>_FILE</c> entries point at.</summary>
    public static IConfigurationBuilder AddFileVariables(this IConfigurationBuilder builder)
    {
        var current = builder.Build().AsEnumerable().ToDictionary(pair => pair.Key, pair => pair.Value);
        return builder.AddInMemoryCollection(Resolve(current));
    }
}
