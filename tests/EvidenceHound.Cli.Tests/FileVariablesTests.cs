namespace EvidenceHound.Cli.Tests;

/// <summary>Docker's <c>_FILE</c> convention: <c>NAME_FILE=/run/secrets/x</c> sets <c>NAME</c> from the file, so secrets never sit in the environment.</summary>
public sealed class FileVariablesTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("hound-file-vars-");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Name_FILE_sets_Name_from_the_file_without_its_trailing_newline()
    {
        var token = Write("hound_ca_token", "token-value\n");

        var values = FileVariables.Resolve(new Dictionary<string, string?> { ["HOUND_CA_TOKEN_FILE"] = token });

        values.ShouldBe(new Dictionary<string, string?> { ["HOUND_CA_TOKEN"] = "token-value" });
    }

    [Fact]
    public void A_value_set_directly_wins_over_the_file()
    {
        var token = Write("hound_ca_token", "from-file");

        var values = FileVariables.Resolve(new Dictionary<string, string?> { ["HOUND_CA_TOKEN_FILE"] = token, ["HOUND_CA_TOKEN"] = "direct" });

        values.ShouldBeEmpty();
    }

    [Fact]
    public void A_missing_file_sets_nothing_so_the_secret_is_reported_missing_where_it_is_used()
    {
        var values = FileVariables.Resolve(new Dictionary<string, string?> { ["HOUND_CA_TOKEN_FILE"] = Path.Combine(_dir.FullName, "absent") });

        values.ShouldBeEmpty();
    }

    [Fact]
    public void A_file_this_user_cannot_read_sets_nothing_instead_of_stopping_hound()
    {
        // s14-03 rehearsal: another tool's NAME_FILE variable pointed at a file the user could not read, and every command crashed.
        var unreadable = Write("someone_elses.log", "x");

        var values = FileVariables.Resolve(
            new Dictionary<string, string?> { ["OTHER_TOOL_DIAGNOSTICS_FILE"] = unreadable, ["HOUND_CA_TOKEN_FILE"] = Write("token", "t") },
            path => path == unreadable ? throw new UnauthorizedAccessException("denied") : File.ReadAllText(path));

        values.ShouldBe(new Dictionary<string, string?> { ["HOUND_CA_TOKEN"] = "t" });
    }

    [Fact]
    public void Variables_without_the_suffix_are_ignored()
    {
        var values = FileVariables.Resolve(new Dictionary<string, string?> { ["PATH"] = "/usr/bin", ["_FILE"] = Write("x", "y") });

        values.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_cli_resolves_a_secret_reference_through_a_FILE_variable()
    {
        var token = Write("hound_ca_token", "token-value\n");
        var config = Write("hound.yaml", """
            ciso_assistant:
              url: http://localhost:8000/api/
              token: env:HOUND_CA_TOKEN
            evidence:
              root: evidence
            collectors:
              - name: example-fake
                type: fake
                schedule: "0 2 * * *"
                settings: {}
            target:
              mapping: mapping.yaml
              compliance_assessment: EXAMPLE-BASELINE-2026
            """);
        Write("mapping.yaml", """
            framework:
              urn: urn:example:risk:framework:example-baseline
              version: 1
            mappings: []
            """);

        var withoutFile = await ValidateAsync(config, new Dictionary<string, string?>());
        var withFile = await ValidateAsync(config, new Dictionary<string, string?> { ["HOUND_CA_TOKEN_FILE"] = token });

        withoutFile.ShouldContain("env:HOUND_CA_TOKEN");
        withFile.ShouldNotContain("env:HOUND_CA_TOKEN");
    }

    private static async Task<string> ValidateAsync(string config, Dictionary<string, string?> settings)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        await HoundCli.RunAsync(["config", "validate", config, "--check-secrets"], output, error, null, settings, TestContext.Current.CancellationToken);
        return error.ToString();
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir.FullName, name);
        File.WriteAllText(path, text);
        return path;
    }
}
