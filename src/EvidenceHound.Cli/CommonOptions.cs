using System.CommandLine;

namespace EvidenceHound.Cli;

/// <summary>The options many commands share, declared once so their names, defaults and help read the same everywhere.</summary>
internal static class CommonOptions
{
    /// <summary><c>--repo</c>: the compliance repo the command works in.</summary>
    public static Option<DirectoryInfo> Repo(string description = "The compliance repo to work in. Default: the current folder.") =>
        new("--repo") { Description = description };

    /// <summary><c>--json</c>: answer in JSON for an agent; a command whose JSON says more than this names it.</summary>
    public static Option<bool> Json(string description = "Answer in JSON, for an agent.") => new("--json") { Description = description };
}
