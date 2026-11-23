namespace EvidenceHound.Cli;

/// <summary>
/// The command line's streams. <see cref="Output"/> carries only command output; prompts and logs go to <see cref="Error"/>, so
/// redirected output stays clean. <see cref="Input"/> answers prompts. <see cref="Interactive"/> is true only when a person is typing at a
/// terminal: approvals need it, so piped answers never approve anything.
/// </summary>
internal sealed record Terminal(TextReader Input, TextWriter Output, TextWriter Error, bool Interactive = false);
