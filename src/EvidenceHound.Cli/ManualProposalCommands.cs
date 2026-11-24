using System.CommandLine;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Manual;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Review;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// Manual evidence with an agent. <c>hound manual propose</c> records what the agent proposes for a file, beside the file and pending;
/// <c>hound manual approve</c> walks a person at a terminal through the pending proposals. Only what the person attests becomes an
/// attestation, and only attestations are published by a run.
/// </summary>
internal static class ManualProposalCommands
{
    public static Command BuildPropose(Terminal terminal, ILogger logger, CollectorRegistry registry, IClock clock)
    {
        var source = new Argument<string>("source") { Description = "The manual source: its collector name in the config." };
        var file = new Argument<string>("file") { Description = "The evidence file: a path, or the name of a file already in the source's drop folder." };
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var result = new Option<string?>("--result") { Description = "The proposed result for the source's checks: pass, fail or partial." };
        var check = new Option<string[]>("--check") { Description = "A different result for one check, as <check id>=<result>. Repeatable." };
        var observation = new Option<string?>("--observation") { Description = "What the file shows, and what is missing, in a sentence or two." };
        var supports = new Option<string[]>("--supports") { Description = "An extract or a location in the file that supports the result. Repeatable; at least one." };
        var json = new Option<bool>("--json") { Description = "Answer in JSON, for an agent." };

        var propose = new Command("propose", "Record a proposed result for a manual evidence file. It waits for a person to attest it with 'hound manual approve'; no run publishes a proposal.")
        {
            source, file, config, result, check, observation, supports, json,
        };
        propose.SetAction(async (parseResult, cancellationToken) =>
        {
            var name = parseResult.GetRequiredValue(source);
            var found = (await ConfigCommands.LoadAsync(parseResult.GetRequiredValue(config), registry, cancellationToken)).Bind(deployment => ManualCommand.Find(deployment, name));
            var proposed = await found.BindAsync(manual => ProposeAsync(
                manual, parseResult.GetRequiredValue(file), new Given(parseResult.GetValue(result), parseResult.GetValue(check) ?? [], parseResult.GetValue(observation), parseResult.GetValue(supports) ?? []),
                clock, cancellationToken));
            return await proposed.Match(
                async kept =>
                {
                    var answer = parseResult.GetValue(json)
                        ? new JsonObject { ["source"] = name, ["file"] = kept.File, ["sha256"] = kept.Sha256, ["pending"] = true }.ToJsonString(OnboardAgentCommand.Indented)
                        : $"Proposed a result for {kept.File} ({name}). It is pending: a person attests it with 'hound manual approve' in a terminal.";
                    await terminal.Output.WriteLineAsync(answer.AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return propose;
    }

    public static Command BuildApprove(Terminal terminal, ILogger logger, CollectorRegistry registry, IClock clock)
    {
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var only = new Option<string?>("--source") { Description = "Only this manual source's proposals. Default: every manual source." };
        var approve = new Command("approve", "Attest, change or reject each pending proposal for manual evidence. Needs a person at a terminal; an agent or a pipe is refused.") { config, only };
        approve.SetAction(async (parseResult, cancellationToken) =>
        {
            if (!terminal.Interactive)
            {
                Log.Usage(logger, "hound manual approve needs a person at a terminal: evidence is never attested through a pipe or by an agent. Run it yourself, for example in the VS Code terminal.");
                return ExitCodes.Usage;
            }

            var sources = (await ConfigCommands.LoadAsync(parseResult.GetRequiredValue(config), registry, cancellationToken)).Bind(deployment => Sources(deployment, parseResult.GetValue(only)));
            var waiting = await sources.BindAsync(found => WaitingAsync(found, cancellationToken));
            return await waiting.Match(
                proposals => new Walk(terminal, clock).RunAsync(proposals, cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return approve;
    }

    /// <summary>Every pending proposal as an item of the review queue, read from the deployment config's manual sources.</summary>
    internal static async Task<Result<IReadOnlyList<ReviewItem>>> PendingAsync(FileInfo config, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var sources = (await ConfigCommands.LoadAsync(config, registry, cancellationToken)).Bind(deployment => Sources(deployment, null));
        var waiting = await sources.BindAsync(found => WaitingAsync(found, cancellationToken));
        return waiting.Map(all => (IReadOnlyList<ReviewItem>)[.. all.Select(Item)]);
    }

    /// <summary>
    /// Attests each pending proposal whose review key is in <paramref name="keys"/>, as proposed, in <paramref name="who"/>'s name. A file that
    /// changed after the proposal is left, as <c>hound manual approve</c> leaves it. Returns the keys attested.
    /// </summary>
    internal static async Task<Result<IReadOnlyList<string>>> AttestAsync(
        FileInfo config, CollectorRegistry registry, IReadOnlySet<string> keys, string who, IClock clock, CancellationToken cancellationToken)
    {
        var sources = (await ConfigCommands.LoadAsync(config, registry, cancellationToken)).Bind(deployment => Sources(deployment, null));
        var waiting = await sources.BindAsync(found => WaitingAsync(found, cancellationToken));
        return await waiting.BindAsync(async all =>
        {
            var attested = new List<string>();
            foreach (var one in all.Where(w => keys.Contains(Item(w).Key)))
            {
                var proposal = one.Proposal;
                if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(one.Path, cancellationToken))), proposal.FileSha256, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await File.WriteAllTextAsync(one.Path + Attestation.Suffix, proposal.Attest(who, clock.UtcNow, proposal.Outcomes, proposal.Observation).ToYaml(), cancellationToken);
                File.Delete(one.Path + Proposal.Suffix);
                attested.Add(Item(one).Key);
            }

            return Result.Ok<IReadOnlyList<string>>(attested);
        });
    }

    private static ReviewItem Item(Waiting waiting) => new(
        ReviewKind.ManualEvidence,
        $"manual_evidence:{waiting.Source}/{waiting.File}",
        $"[manual evidence] {waiting.Source} {waiting.File}: proposed by {waiting.Proposal.ProposedBy}: {Outcomes(waiting.Proposal.Outcomes)}. {waiting.Proposal.Observation}",
        "Attested evidence is published as the requirement's result on the next run; only a person attests it, so the result can be trusted.",
        "The file is attested as proposed, in your name, and the next 'hound run' publishes it.",
        5)
    {
        Recommendation = new ReviewRecommendation("attest", waiting.Proposal.Observation, waiting.Proposal.Supports),
        File = waiting.Path,
    };

    private static string Outcomes(IReadOnlyDictionary<string, CheckOutcome> outcomes) => outcomes.Values.Distinct().ToArray() is [var one]
        ? Attestation.NameOf(one)
        : string.Join(", ", outcomes.Select(o => $"{o.Key} {Attestation.NameOf(o.Value)}"));

    /// <summary>What the agent gave on the command line.</summary>
    private sealed record Given(string? Result, string[] Checks, string? Observation, string[] Supports);

    private sealed record Kept(string File, string Sha256);

    /// <summary>One pending proposal: its source, the file's path in the drop folder, and what was proposed.</summary>
    private sealed record Waiting(string Source, ManualSource Manual, string Path, Proposal Proposal)
    {
        public string File => System.IO.Path.GetFileName(Path);
    }

    private static async Task<Result<Kept>> ProposeAsync(ManualSource source, string file, Given given, IClock clock, CancellationToken cancellationToken)
    {
        if (given.Supports.Length == 0 || string.IsNullOrWhiteSpace(given.Observation))
        {
            return Fail<Kept>("manual.proposal_unsupported", "A proposal needs --observation and at least one --supports: an extract or a location in the file that shows the result.");
        }

        // A bare name is a file already in the drop folder; anything else is a path to copy in.
        var name = Path.GetFileName(file);
        var kept = Path.Combine(source.DropFolder, name);
        var path = name == file && File.Exists(kept) ? kept : Path.GetFullPath(file);
        if (!File.Exists(path))
        {
            return Fail<Kept>("manual.file_missing", $"File '{file}' does not exist, here or in {source.DropFolder}.");
        }

        if (!source.Accepts(name))
        {
            return Fail<Kept>("manual.file_not_accepted", $"'{name}' does not match this source's patterns: {string.Join(", ", source.Patterns)}.");
        }

        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        if (File.Exists(kept) && !(await File.ReadAllBytesAsync(kept, cancellationToken)).SequenceEqual(content))
        {
            return Fail<Kept>("manual.file_exists", $"A different '{name}' is already in {source.DropFolder}. Rename the new file and propose again.");
        }

        if (File.Exists(kept + Attestation.Suffix))
        {
            return Fail<Kept>("manual.already_attested", $"'{name}' is already attested by a person ({name}{Attestation.Suffix}). A proposal cannot change an attestation.");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        return await Outcomes(source, given).BindAsync(async outcomes =>
        {
            Directory.CreateDirectory(source.DropFolder);
            if (!File.Exists(kept))
            {
                await File.WriteAllBytesAsync(kept, content, cancellationToken);
            }

            await File.WriteAllTextAsync(kept + Proposal.Suffix, new Proposal("agent", clock.UtcNow, hash, outcomes, given.Observation.Trim(), given.Supports).ToYaml(), cancellationToken);
            return Result.Ok(new Kept(name, hash));
        });
    }

    /// <summary>The proposed outcome of every check of the source: <c>--result</c> for all, with each <c>--check</c> in its place.</summary>
    private static Result<IReadOnlyDictionary<string, CheckOutcome>> Outcomes(ManualSource source, Given given)
    {
        var outcomes = new Dictionary<string, CheckOutcome>(StringComparer.Ordinal);
        var problems = new List<Problem>();
        if (given.Result is not null)
        {
            if (Proposable(given.Result, out var all))
            {
                outcomes = source.Checks.ToDictionary(c => c.Value, _ => all, StringComparer.Ordinal);
            }
            else
            {
                problems.Add(Invalid($"--result '{given.Result}' must be pass, fail or partial."));
            }
        }

        foreach (var pair in given.Checks)
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !source.Checks.Any(c => c.Value == parts[0]) || !Proposable(parts[1], out var one))
            {
                problems.Add(Invalid($"--check '{pair}' must be <check id>=<pass|fail|partial>, for one of this source's checks: {string.Join(", ", source.Checks)}."));
                continue;
            }

            outcomes[parts[0]] = one;
        }

        if (problems.Count == 0 && source.Checks.Where(c => !outcomes.ContainsKey(c.Value)).ToArray() is [_, ..] missing)
        {
            problems.Add(Invalid($"No result is proposed for {string.Join(", ", missing)}. Give --result for every check, or --check for each."));
        }

        return problems.Count > 0 ? Result.Fail<IReadOnlyDictionary<string, CheckOutcome>>([.. problems]) : Result.Ok<IReadOnlyDictionary<string, CheckOutcome>>(outcomes);
    }

    private static bool Proposable(string text, out CheckOutcome outcome) => Attestation.TryParseOutcome(text, out outcome) && outcome != CheckOutcome.NotApplicable;

    private static Result<IReadOnlyList<(string Name, ManualSource Manual)>> Sources(DeploymentConfig deployment, string? only) => only is not null
        ? ManualCommand.Find(deployment, only).Map(manual => (IReadOnlyList<(string, ManualSource)>)[(only, manual)])
        : Result.Combine(deployment.Collectors.Where(c => c.Type == ManualCollector.TypeName).Select(c => ManualCommand.Find(deployment, c.Name).Map(manual => (c.Name, manual))));

    /// <summary>Every proposal a person has still to attest or reject, by source in config order and then by file name.</summary>
    private static async Task<Result<IReadOnlyList<Waiting>>> WaitingAsync(IReadOnlyList<(string Name, ManualSource Manual)> sources, CancellationToken cancellationToken)
    {
        var waiting = new List<Result<Waiting>>();
        foreach (var (name, manual) in sources.Where(s => Directory.Exists(s.Manual.DropFolder)))
        {
            foreach (var path in Directory.EnumerateFiles(manual.DropFolder, "*" + Proposal.Suffix).Order(StringComparer.Ordinal))
            {
                var file = path[..^Proposal.Suffix.Length];
                waiting.Add(Proposal.Parse(await File.ReadAllTextAsync(path, cancellationToken), Path.GetFileName(path)).Map(p => new Waiting(name, manual, file, p)));
            }
        }

        return Result.Combine(waiting).Map(all => (IReadOnlyList<Waiting>)[.. all.Where(w => w.Proposal.Pending && File.Exists(w.Path) && !File.Exists(w.Path + Attestation.Suffix))]);
    }

    private static Result<T> Fail<T>(string code, string message)
        where T : notnull => Result.Fail<T>(new Problem(code, message));

    private static Problem Invalid(string message) => new("manual.proposal_invalid", message);

    /// <summary>One run through the pending proposals with a person at the terminal.</summary>
    private sealed class Walk(Terminal terminal, IClock clock)
    {
        private int _attested;
        private int _rejected;

        public async Task<int> RunAsync(IReadOnlyList<Waiting> proposals, CancellationToken cancellationToken)
        {
            if (proposals.Count == 0)
            {
                await terminal.Output.WriteLineAsync("No proposals are waiting.".AsMemory(), cancellationToken);
                return ExitCodes.Ok;
            }

            var who = await AskAsync("Your name, recorded as the attester: ", cancellationToken);
            for (var i = 0; i < proposals.Count && who is { Length: > 0 }; i++)
            {
                if (!await OneAsync(proposals[i], $"[{i + 1}/{proposals.Count}]", who, cancellationToken))
                {
                    break;
                }
            }

            var rejected = _rejected > 0 ? $"; {_rejected} rejected" : "";
            await terminal.Output.WriteLineAsync($"Attested {_attested} of {proposals.Count} proposals{rejected}. The next 'hound run' publishes what you attested.".AsMemory(), cancellationToken);
            return ExitCodes.Ok;
        }

        /// <summary>Shows one proposal and acts on the person's answer. False when they stop, or the input ends.</summary>
        private async Task<bool> OneAsync(Waiting waiting, string position, string who, CancellationToken cancellationToken)
        {
            var proposal = waiting.Proposal;
            await SayAsync($"\n{position} {waiting.Source} {waiting.File}", cancellationToken);
            await SayAsync($"  File: {waiting.Path}", cancellationToken);
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(waiting.Path, cancellationToken))), proposal.FileSha256, StringComparison.OrdinalIgnoreCase))
            {
                await SayAsync("  The file changed after the agent proposed. Not attested: ask the agent to propose again from the file as it is now.", cancellationToken);
                return true;
            }

            await SayAsync($"  Proposed by {proposal.ProposedBy}: {Summary(proposal.Outcomes)}", cancellationToken);
            await SayAsync($"  {proposal.Observation}", cancellationToken);
            foreach (var support in proposal.Supports)
            {
                await SayAsync($"  Supports: {support}", cancellationToken);
            }

            while (true)
            {
                switch (await AskAsync("  Attest (a), change (c), reject (r) or stop (s): ", cancellationToken))
                {
                    case "a":
                        await AttestAsync(waiting, proposal.Attest(who, clock.UtcNow, proposal.Outcomes, proposal.Observation), cancellationToken);
                        return true;
                    case "c":
                        if (await ChangedAsync(waiting, who, cancellationToken) is { } changed)
                        {
                            await AttestAsync(waiting, changed, cancellationToken);
                        }

                        return true;
                    case "r":
                        var reason = await AskAsync("  Why is it rejected: ", cancellationToken) ?? "";
                        await File.WriteAllTextAsync(waiting.Path + Proposal.Suffix, proposal.Reject(who, clock.UtcNow, reason).ToYaml(), cancellationToken);
                        _rejected++;
                        return true;
                    case "s" or null:
                        return false;
                    default:
                        await SayAsync("  Answer a, c, r or s.", cancellationToken);
                        break;
                }
            }
        }

        /// <summary>The person's own outcome for each check and their observation; Enter keeps what was proposed. Null when an answer is not an outcome.</summary>
        private async Task<Attestation?> ChangedAsync(Waiting waiting, string who, CancellationToken cancellationToken)
        {
            var outcomes = new Dictionary<string, CheckOutcome>(StringComparer.Ordinal);
            foreach (var (check, proposed) in waiting.Manual.Checks.Select(c => (c.Value, waiting.Proposal.Outcomes.GetValueOrDefault(c.Value, CheckOutcome.Partial))))
            {
                var text = await AskAsync($"  Outcome for {check} ({string.Join(", ", Attestation.OutcomeNames)}) [{Attestation.NameOf(proposed)}]: ", cancellationToken);
                var outcome = proposed;
                if (text is { Length: > 0 } && !Attestation.TryParseOutcome(text, out outcome))
                {
                    await SayAsync($"  '{text}' is not an outcome. Not attested; it waits for the next 'hound manual approve'.", cancellationToken);
                    return null;
                }

                outcomes[check] = outcome;
            }

            var observation = await AskAsync("  Observation (Enter keeps the proposed one): ", cancellationToken) is { Length: > 0 } own ? own : waiting.Proposal.Observation;
            return waiting.Proposal.Attest(who, clock.UtcNow, outcomes, observation);
        }

        private async Task AttestAsync(Waiting waiting, Attestation attestation, CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(waiting.Path + Attestation.Suffix, attestation.ToYaml(), cancellationToken);
            File.Delete(waiting.Path + Proposal.Suffix);
            _attested++;
        }

        private static string Summary(IReadOnlyDictionary<string, CheckOutcome> outcomes) => Outcomes(outcomes);

        private Task SayAsync(string line, CancellationToken cancellationToken) => terminal.Error.WriteLineAsync(line.AsMemory(), cancellationToken);

        /// <summary>The person's answer, trimmed; null when the input has ended.</summary>
        private async Task<string?> AskAsync(string question, CancellationToken cancellationToken)
        {
            await terminal.Error.WriteAsync(question.AsMemory(), cancellationToken);
            await terminal.Error.FlushAsync(cancellationToken);
            return (await terminal.Input.ReadLineAsync(cancellationToken))?.Trim();
        }
    }
}
