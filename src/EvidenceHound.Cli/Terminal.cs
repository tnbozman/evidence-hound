namespace EvidenceHound.Cli;

/// <summary>
/// The command line's streams. <see cref="Output"/> carries only command output; prompts and logs go to <see cref="Error"/>, so
/// redirected output stays clean. <see cref="Input"/> answers prompts.
/// </summary>
internal sealed record Terminal(TextReader Input, TextWriter Output, TextWriter Error);
