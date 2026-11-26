using System.CommandLine;
using EvidenceHound.Collectors.Rest;
using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Sessions;
using EvidenceHound.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound onboard profile &lt;system&gt;</c>: after applicability, checks the profile written for the system's accepted API checks (by a
/// person, or by their coding assistant with the write-a-profile skill). The profile is validated; with the person's go-ahead it also runs
/// once, read-only, and the person confirms each check's result, which is kept with the hashed recordings as a profile test. Nothing is
/// scheduled: the profile is for review.
/// </summary>
internal static class OnboardProfileCommand
{
    public const string TestName = "onboarding";

    public static Command Build(Terminal terminal, ILoggerFactory loggers, IConfiguration configuration)
    {
        var logger = loggers.CreateLogger("hound.onboard");
        var system = new Argument<string>("system") { Description = "The system onboarded with 'hound onboard <system>', whose accepted API checks the profile covers." };
        var repo = CommonOptions.Repo();
        var profiles = new Option<DirectoryInfo>("--profiles") { Description = "The profiles folder. Default: profiles in the repo." };
        var name = new Option<string>("--name") { Description = "The profile's name. Default: the system's name in lower case." };
        var set = new Option<string[]>("--set") { Description = "An instance setting for the sample run, as name=value. Repeat for more.", AllowMultipleArgumentsPerToken = false };
        var secret = new Option<string[]>("--secret") { Description = "A secret for the sample run, as name=<env:VAR or file:/path>. Repeat for more." };
        var sample = new Option<bool>("--sample") { Description = "Run the profile once, read-only, against the API without asking first." };
        var command = new Command("profile", "Validate, sample and test the profile written for a system's accepted API checks, for review.")
        {
            system, repo, profiles, name, set, secret, sample,
        };
        var retired = Retired.On(command, config: true, "--rounds");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            await retired.NoteAsync(parseResult, terminal, cancellationToken);
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var systemName = parseResult.GetRequiredValue(system);
            var options = new Options(
                systemName,
                root,
                parseResult.GetValue(profiles)?.FullName ?? Path.Combine(root, "profiles"),
                parseResult.GetValue(name) ?? systemName.ToLowerInvariant(),
                Pairs(parseResult.GetValue(set)),
                Pairs(parseResult.GetValue(secret)),
                parseResult.GetValue(sample));
            var result = await RunAsync(options, terminal, configuration, cancellationToken);
            return result.Match(code => code, problems => Log.Problems(logger, problems));
        });
        return command;
    }

    private static async Task<Result<int>> RunAsync(Options options, Terminal terminal, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var sessionFile = Path.Combine(options.Repo, "onboarding", options.System, "session.json");
        if (!File.Exists(sessionFile))
        {
            return Result.Fail<int>(new Problem("onboard.session_missing", $"{options.System} has no onboarding session; run 'hound onboard {options.System}' first."));
        }

        var read = OnboardSessionJson.Read(await File.ReadAllTextAsync(sessionFile, cancellationToken));
        if (read is not Result<OnboardSession>.Ok { Value: var session })
        {
            return Result.Fail<int>(((Result<OnboardSession>.Fail)read).Errors);
        }

        if (session.Stage != OnboardStage.Decided || !session.Decisions.Any(d => d.Kind == DecisionKind.ApiCheck))
        {
            return Result.Fail<int>(new Problem(
                "onboard.no_api_checks", $"{options.System} has no accepted API checks yet; finish 'hound onboard {options.System}' and accept the requirements an API check reads."));
        }

        var file = Path.GetRelativePath(options.Repo, Path.Combine(options.Profiles, options.Name + ".yaml")).Replace('\\', '/');
        if (!File.Exists(Path.Combine(options.Profiles, options.Name + ".yaml")))
        {
            return Result.Fail<int>(new Problem(
                "onboard.profile_missing",
                $"{file} does not exist. hound no longer drafts a profile itself: write it, or ask your coding assistant (set up with 'hound agent setup', skill write-a-profile), then run this again to sample and test it."));
        }

        var prompt = new OnboardSystemCommand.Prompt(terminal);
        var sampling = options.Sample;
        if (!sampling)
        {
            var answer = await prompt.AskAsync("Run the profile once, read-only, against the API with the settings and secrets given, to record its answers? (y/N)", cancellationToken);
            sampling = answer is "y" or "Y" or "yes";
        }

        var secrets = new Dictionary<string, Secret>(StringComparer.Ordinal);
        if (sampling)
        {
            var provider = DeploymentFile.Secrets(configuration);
            foreach (var (key, reference) in options.Secrets)
            {
                var resolved = SecretReference.Parse(reference).Bind(provider.Resolve);
                if (resolved is not Result<Secret>.Ok { Value: var value })
                {
                    return Result.Fail<int>(((Result<Secret>.Fail)resolved).Errors);
                }

                secrets[key] = value;
            }
        }

        var state = new ProfileCheck(options, sampling, secrets, prompt, terminal);
        var remaining = await state.CheckAsync(cancellationToken);
        var lines = new List<string> { $"Checked {file}." };
        lines.Add(sampling
            ? state.Tested ? $"Sampled once, read-only; the results you confirmed are the profile test {options.Name}{ProfileTestRunner.FolderSuffix}/{TestName}.yaml, with hashed recordings." : "Sampled, but no test was saved."
            : "The profile was not sampled, so it has no test from the API yet: run 'hound onboard profile' again with --sample, or add a test by hand.");
        lines.AddRange(remaining.Select(p => $"Still to fix: {p.Code}: {p.Message}"));
        lines.Add($"Next: review the profile in a pull request, then run 'hound profile validate {options.Name}' and 'hound profile test {options.Name}'.");
        await terminal.Output.WriteLineAsync(string.Join('\n', lines).AsMemory(), cancellationToken);
        return Result.Ok(remaining.Count == 0 ? ExitCodes.Ok : ExitCodes.ProfileTestsFailed);
    }

    private static IReadOnlyList<(string Key, string Value)> Pairs(string[]? values) =>
        [.. (values ?? []).Select(v => v.Split('=', 2)).Where(p => p.Length == 2).Select(p => (p[0].Trim(), p[1].Trim()))];

    private sealed record Options(
        string System,
        string Repo,
        string Profiles,
        string Name,
        IReadOnlyList<(string Key, string Value)> Settings,
        IReadOnlyList<(string Key, string Value)> Secrets,
        bool Sample);

    /// <summary>One check of the profile: valid, and when sampling, run once against the API with each result confirmed by the person.</summary>
    private sealed class ProfileCheck(Options options, bool sampling, IReadOnlyDictionary<string, Secret> secrets, OnboardSystemCommand.Prompt prompt, Terminal terminal)
    {
        private readonly Dictionary<string, string> _expect = new(StringComparer.Ordinal);

        public bool Tested { get; private set; }

        private string TestsFolder => ProfileTestRunner.TestsFolder(options.Profiles, options.Name);

        public async Task<IReadOnlyList<Problem>> CheckAsync(CancellationToken cancellationToken)
        {
            var loaded = ProfileFolder.Load(options.Profiles, options.Name);
            if (loaded is not Result<LoadedProfile>.Ok { Value: var profile })
            {
                return ((Result<LoadedProfile>.Fail)loaded).Errors;
            }

            if (ProfileValidator.Validate(profile.Profile, RestCollector.Validation) is { Count: > 0 } invalid)
            {
                return invalid;
            }

            if (!sampling)
            {
                return [];
            }

            var settings = options.Settings.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
            var sampled = await SampleRun.RunAsync(profile.Profile, new InstanceValues(settings, secrets, IncludeIdentities: false), handler, new SystemClock(), cancellationToken);
            if (sampled is not Result<SampleRecording>.Ok { Value: var recording })
            {
                return ((Result<SampleRecording>.Fail)sampled).Errors;
            }

            var now = DateTimeOffset.UtcNow;
            var ids = profile.Profile.Checks.Select(c => c.Id).ToArray();
            if (Directory.Exists(TestsFolder))
            {
                Directory.Delete(TestsFolder, recursive: true);
            }

            if (ids.Any(id => !_expect.ContainsKey(id)))
            {
                // Run once with a placeholder to read each check's result, then ask the person to confirm it.
                SampleRun.WriteTestCase(recording, TestsFolder, TestName, settings, [.. secrets.Keys], now, ids.ToDictionary(id => id, _ => "pass", StringComparer.Ordinal));
                var first = await ProfileTestRunner.RunAsync(options.Profiles, options.Name, cancellationToken);
                if (first is not Result<IReadOnlyList<CaseReport>>.Ok { Value: [var report, ..] })
                {
                    return first is Result<IReadOnlyList<CaseReport>>.Fail { Errors: var runProblems } ? runProblems : [];
                }

                foreach (var check in report.Checks.Where(c => !_expect.ContainsKey(c.CheckId)))
                {
                    await terminal.Error.WriteLineAsync($"{check.CheckId}: {check.Actual}. {check.Observation}".AsMemory(), cancellationToken);
                    var answer = await prompt.AskAsync($"Is {check.Actual} right? y, or type the result it should give ({string.Join(", ", ProfileTestCase.Results)})", cancellationToken);
                    if (answer is null)
                    {
                        return [new Problem("onboard.input_ended", "The input ended before every check's result was confirmed.")];
                    }

                    _expect[check.CheckId] = answer is "y" or "Y" or "" ? check.Actual : answer;
                }

                Directory.Delete(TestsFolder, recursive: true);
            }

            SampleRun.WriteTestCase(recording, TestsFolder, TestName, settings, [.. secrets.Keys], now, ids.ToDictionary(id => id, id => _expect[id], StringComparer.Ordinal));
            var run = await ProfileTestRunner.RunAsync(options.Profiles, options.Name, cancellationToken);
            Tested = true;
            return run switch
            {
                Result<IReadOnlyList<CaseReport>>.Ok { Value: var reports } => [.. reports.SelectMany(r => r.Checks.Where(c => !c.Passed)
                    .Select(c => new Problem("profile.test_failed", $"{c.CheckId}: the reviewer expects {c.Expected}, the profile gives {c.Actual}. {c.Observation}"))
                    .Concat(r.Unrecorded.Select(u => new Problem("profile.test_unrecorded", $"no recording for {u}"))))],
                Result<IReadOnlyList<CaseReport>>.Fail { Errors: var errors } => errors,
                _ => [],
            };
        }
    }
}
