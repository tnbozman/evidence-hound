namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s19-06: the one way the command tests run <c>hound</c>: arguments, what the person types, extra settings on top of the environment,
/// and whether a person is at the terminal. Returns the exit code and both streams.
/// </summary>
internal static class Hound
{
    public static async Task<(int Exit, string Output, string Error)> RunAsync(
        string[] args, CancellationToken ct, string input = "", IReadOnlyDictionary<string, string?>? settings = null, bool terminal = false)
    {
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await HoundCli.RunAsync(args, reader, output, error, settings ?? new Dictionary<string, string?>(), terminal, ct);
        return (exit, output.ToString(), error.ToString());
    }

    /// <summary>Settings for a fixed day, with the user config folder under <paramref name="home"/> so nothing reads the real one.</summary>
    public static Dictionary<string, string?> At(string home, string today = "2026-11-23") =>
        new(StringComparer.Ordinal) { ["XDG_CONFIG_HOME"] = home, ["HOUND_TODAY"] = today };
}
