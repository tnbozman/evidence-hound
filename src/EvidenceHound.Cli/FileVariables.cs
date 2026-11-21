namespace EvidenceHound.Cli;

/// <summary>Docker's <c>_FILE</c> convention: <c>NAME_FILE=/path</c> gives <c>NAME</c> the file's contents.</summary>
internal static class FileVariables
{
    public static IReadOnlyDictionary<string, string?> Resolve(IReadOnlyDictionary<string, string?> variables) =>
        throw new NotImplementedException();
}
