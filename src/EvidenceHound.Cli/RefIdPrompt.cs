using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Cli;

/// <summary>
/// Asks for a ref ID with its kind's prefix printed as part of the prompt, so only the rest is typed, and asks again with the rule until
/// the answer fits. An option's value is completed the same way, without asking.
/// </summary>
internal static class RefIdPrompt
{
    /// <summary>The ref ID an option gave: kept when it has its prefix, given its prefix when it has not, a problem when it does not fit.</summary>
    public static Result<string> Given(RefIdKind kind, string value, string code) =>
        RefIds.Complete(kind, value) is { } refId
            ? Result.Ok(refId)
            : Result.Fail<string>(new Problem(code, $"'{value}' is not a ref ID. {RefIds.Rule(kind)}"));

    /// <summary>
    /// What the person typed after the prefix, completed; <paramref name="defaultRest"/> on an empty answer when there is one. Null once the
    /// input has ended, so a closed input stops rather than loops.
    /// </summary>
    public static async Task<string?> AskAsync(Terminal terminal, RefIdKind kind, string question, string defaultRest, CancellationToken cancellationToken)
    {
        var prompt = defaultRest.Length > 0 ? $"{question} [{defaultRest}]: {RefIds.Prefix(kind)}" : $"{question}: {RefIds.Prefix(kind)}";
        while (true)
        {
            await terminal.Error.WriteAsync(prompt.AsMemory(), cancellationToken);
            await terminal.Error.FlushAsync(cancellationToken);
            if (await terminal.Input.ReadLineAsync(cancellationToken) is not { } line)
            {
                return null;
            }

            var typed = Answers.Clean(line) is { Length: > 0 } answer ? answer : defaultRest;
            if (RefIds.Complete(kind, typed) is { } refId)
            {
                return refId;
            }

            await terminal.Error.WriteLineAsync((typed.Length > 0 ? $"'{typed}' does not fit. {RefIds.Rule(kind)}" : RefIds.Rule(kind)).AsMemory(), cancellationToken);
        }
    }
}
