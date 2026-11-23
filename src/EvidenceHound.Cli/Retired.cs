using System.CommandLine;

namespace EvidenceHound.Cli;

/// <summary>
/// The options that only served hound's built-in model, removed with it. They are still taken, hidden, so an older script keeps working;
/// each does nothing and says where model help now comes from: a coding assistant set up with <c>hound agent setup</c>.
/// </summary>
internal sealed class Retired
{
    public const string Message =
        "hound no longer has a built-in model. For model help, set up your coding assistant with 'hound agent setup' and ask it: it drafts through hound, and you approve.";

    private readonly Option[] _options;

    private Retired(Option[] options) => _options = options;

    /// <summary>Adds the retired options to <paramref name="command"/>; <paramref name="config"/> when it took the onboarding config, and any <paramref name="more"/> of its own.</summary>
    public static Retired On(Command command, bool config = true, params string[] more)
    {
        Option[] options =
        [
            new Option<bool>("--no-model") { Hidden = true },
            new Option<bool>("--show-requests") { Hidden = true },
            .. config ? [new Option<string?>("--config") { Hidden = true }] : Array.Empty<Option>(),
            .. more.Select(name => new Option<string?>(name) { Hidden = true }),
        ];
        foreach (var option in options)
        {
            command.Options.Add(option);
        }

        return new Retired(options);
    }

    /// <summary>Says, on the error stream, that each retired option given does nothing now.</summary>
    public async Task NoteAsync(ParseResult parseResult, Terminal terminal, CancellationToken cancellationToken)
    {
        foreach (var option in _options.Where(o => parseResult.GetResult(o) is { Implicit: false }))
        {
            await terminal.Error.WriteLineAsync($"{option.Name} does nothing now: hound has no built-in model. For model help, set up your coding assistant with 'hound agent setup'.".AsMemory(), cancellationToken);
        }
    }
}
