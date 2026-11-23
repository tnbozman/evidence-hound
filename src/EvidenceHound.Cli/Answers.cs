namespace EvidenceHound.Cli;

/// <summary>What a person typed at a prompt, cleaned: control characters a terminal can pass through on paste (such as Ctrl+V) are dropped, then the ends trimmed. Pure.</summary>
internal static class Answers
{
    public static string Clean(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);
        return new string([.. typed.Where(c => !char.IsControl(c))]).Trim();
    }
}
