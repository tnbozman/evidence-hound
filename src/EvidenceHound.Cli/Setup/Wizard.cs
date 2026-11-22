using System.Text;

namespace EvidenceHound.Cli.Setup;

/// <summary>
/// Asks the questions of a guided command on stderr and reads the answers from the terminal's input. An answer given as an option is
/// used without asking; with <c>--yes</c> nothing is asked and every question takes its default.
/// </summary>
internal sealed class Wizard(Terminal terminal, bool yes)
{
    /// <summary>The answer to <paramref name="question"/>: <paramref name="given"/> when the option was set, else what the person types, else <paramref name="default"/>.</summary>
    public async Task<string> AskAsync(string question, string? given, string @default, CancellationToken cancellationToken)
    {
        if (given is not null)
        {
            return given.Trim();
        }

        if (yes)
        {
            return @default;
        }

        var answer = await PromptAsync(@default.Length > 0 ? $"{question} [{@default}]: " : $"{question}: ", cancellationToken);
        return answer.Length > 0 ? answer : @default;
    }

    /// <summary>A secret's value, read without echo on a terminal. It is asked even with <c>--yes</c>, because a secret has no default.</summary>
    public async Task<string> AskSecretAsync(string question, CancellationToken cancellationToken)
    {
        await terminal.Error.WriteAsync($"{question} (not shown): ".AsMemory(), cancellationToken);
        await terminal.Error.FlushAsync(cancellationToken);
        if (ReferenceEquals(terminal.Input, Console.In) && !Console.IsInputRedirected)
        {
            var typed = ReadHidden();
            await terminal.Error.WriteLineAsync();
            return typed;
        }

        return (await terminal.Input.ReadLineAsync(cancellationToken))?.Trim() ?? "";
    }

    private async Task<string> PromptAsync(string prompt, CancellationToken cancellationToken)
    {
        await terminal.Error.WriteAsync(prompt.AsMemory(), cancellationToken);
        await terminal.Error.FlushAsync(cancellationToken);
        return (await terminal.Input.ReadLineAsync(cancellationToken))?.Trim() ?? "";
    }

    private static string ReadHidden()
    {
        var typed = new StringBuilder();
        while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                typed.Length = Math.Max(0, typed.Length - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }

        return typed.ToString().Trim();
    }

    /// <summary>A comma-separated answer as a list, without blanks.</summary>
    public static IReadOnlyList<string> List(string answer) =>
        [.. answer.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>The text as an ID part: upper case letters and digits, with dashes between words (<c>Example firewall</c> is <c>EXAMPLE-FIREWALL</c>).</summary>
    public static string Slug(string text) =>
        string.Join('-', new string([.. text.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : ' ')]).Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
