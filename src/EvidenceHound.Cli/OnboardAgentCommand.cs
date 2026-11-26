using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Sessions;
using EvidenceHound.Onboarding.Skills;
using EvidenceHound.Onboarding.Suppliers;
using Microsoft.Extensions.Logging;
using Settings = EvidenceHound.Cli.OnboardSystemCommand.Settings;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound onboard &lt;system&gt; pending</c> and <c>decide</c>: the same session as the interactive command, driven by options and answering
/// in JSON, so an agent drafts every decision without a prompt. What it records is a draft: nothing is written to the scope until a person
/// runs <c>hound approve</c> in a terminal.
/// </summary>
internal static class OnboardAgentCommand
{
    public const string DraftedBy = "agent";


    public static void Attach(Command onboard, Argument<string?> system, Terminal terminal, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("hound.onboard");
        onboard.Subcommands.Add(Pending(system, terminal, logger));
        onboard.Subcommands.Add(Decide(system, terminal, logger));
    }

    private static Option<DirectoryInfo> Repo() => new("--repo") { Description = "The config repo to work in. Default: the current folder." };

    private static Option<bool> Json() => new("--json") { Description = "Answer in JSON, for an agent." };

    private static Command Pending(Argument<string?> system, Terminal terminal, ILogger logger)
    {
        var repo = Repo();
        var json = Json();
        var framework = new Option<FileInfo>("--framework") { Description = "The framework snapshot to decide against. Default: the session's, else frameworks/essential-eight-2023.json in the repo." };
        var groups = new Option<string[]>("--group") { Description = "An implementation group in scope, such as 2. Repeat for more. Used when the session starts.", AllowMultipleArgumentsPerToken = false };
        var specs = new Option<FileInfo[]>("--spec") { Description = "The system's API specification (OpenAPI, JSON or YAML). Repeat for more." };
        var owner = new Option<string?>("--owner") { Description = "To start the session: the system owner's email, as a CISO Assistant user." };
        var product = new Option<string?>("--product") { Description = "To start the session: the product, as the vendor names it." };
        var solution = new Option<string?>("--solution") { Description = "To start the session: the ref_id of the solution the system is, declared by a third party under scope/third-parties/ (see 'hound supplier draft')." };
        var command = new Command("pending", "List the requirements not yet decided for the system, with the API summary and the skills that apply. Starts the session when given --owner, --product and --solution.")
        {
            repo, json, framework, groups, specs, owner, product, solution,
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            if (parseResult.GetValue(system) is not { Length: > 0 } name)
            {
                Log.Usage(logger, "Name the system: hound onboard <system> pending --json.");
                return ExitCodes.Usage;
            }

            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var start = new Start(parseResult.GetValue(owner), parseResult.GetValue(product), parseResult.GetValue(solution));
            var given = new Given(parseResult.GetValue(framework)?.FullName, parseResult.GetValue(groups) ?? [], [.. (parseResult.GetValue(specs) ?? []).Select(s => s.FullName)]);
            var listed = await PendingAsync(name, root, start, given, cancellationToken);
            return await listed.Match(
                async answer =>
                {
                    await terminal.Output.WriteLineAsync((parseResult.GetValue(json) ? answer.ToJsonString(CommandJson.Readable) : Text(answer)).AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return command;
    }

    private static Command Decide(Argument<string?> system, Terminal terminal, ILogger logger)
    {
        var repo = Repo();
        var json = Json();
        var requirement = new Argument<string>("requirement") { Description = "The requirement, as 'pending' lists it." };
        var kind = new Option<string>("--kind") { Description = "How the requirement is met: api_check, manual, not_applicable or other_system.", Required = true };
        kind.AcceptOnlyFromAmong([.. Enum.GetValues<DecisionKind>().Select(Applicability.Name)]);
        var reasoning = new Option<string>("--reasoning") { Description = "Why, in a sentence or two: the endpoint that reads it, or why none fits.", Required = true };
        var sources = new Option<string[]>("--source") { Description = "What the reasoning rests on, such as an endpoint or a vendor document. Repeat for more.", AllowMultipleArgumentsPerToken = false };
        var other = new Option<string?>("--other-system") { Description = "With other_system: the system that meets the requirement." };
        var justification = new Option<string?>("--justification") { Description = "With not_applicable: the justification, kept for auditors. A person approves it with 'hound approve'." };
        var command = new Command("decide", "Record one decision as a draft by an agent, checked as the interactive flow checks it. A person approves drafts with 'hound approve'.")
        {
            requirement, repo, json, kind, reasoning, sources, other, justification,
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            if (parseResult.GetValue(system) is not { Length: > 0 } name)
            {
                Log.Usage(logger, "Name the system: hound onboard <system> decide <requirement> --kind <kind> --reasoning <text>.");
                return ExitCodes.Usage;
            }

            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var decided = await DecideAsync(
                Of(name, root, null, [], []),
                parseResult.GetRequiredValue(requirement),
                Applicability.KindOf(parseResult.GetRequiredValue(kind))!.Value,
                parseResult.GetRequiredValue(reasoning),
                parseResult.GetValue(sources) ?? [],
                parseResult.GetValue(justification),
                parseResult.GetValue(other),
                cancellationToken);
            return await decided.Match(
                async answer =>
                {
                    await terminal.Output.WriteLineAsync((parseResult.GetValue(json)
                        ? answer.ToJsonString(CommandJson.Readable)
                        : $"Drafted {(string?)answer["requirement"]} as {(string?)answer["kind"]}; {(int)answer["remaining"]!} requirement(s) still to decide. A person approves drafts with 'hound approve'.").AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return command;
    }

    /// <summary>What starts a session without prompts.</summary>
    private sealed record Start(string? Owner, string? Product, string? Solution);

    /// <summary>What the call gave for the framework, groups and specifications; the session's own are used when it gave none.</summary>
    private sealed record Given(string? Framework, IReadOnlyList<string> Groups, IReadOnlyList<string> Specs);

    private static Settings Of(string system, string repo, string? framework, IReadOnlyList<string> groups, IReadOnlyList<string> specs) =>
        new(system, repo, framework ?? Path.Combine(repo, "frameworks", "essential-eight-2023.json"), groups, specs);

    private static async Task<Result<JsonObject>> PendingAsync(string system, string repo, Start start, Given given, CancellationToken cancellationToken)
    {
        var thirdParties = await OnboardSystemCommand.ThirdPartiesAsync(repo, cancellationToken);
        var sessionFile = Of(system, repo, null, [], []).SessionFile;
        var loaded = File.Exists(sessionFile)
            ? OnboardSessionJson.Read(await File.ReadAllTextAsync(sessionFile, cancellationToken))
            : Begin(system, start, thirdParties);
        if (loaded is not Result<OnboardSession>.Ok { Value: var session })
        {
            return Result.Fail<JsonObject>(((Result<OnboardSession>.Fail)loaded).Errors);
        }

        var settings = Of(
            system, repo, given.Framework ?? (session.Framework is { } held ? Path.Combine(repo, held) : null), given.Groups.Count > 0 ? given.Groups : session.Groups,
            given.Specs.Count > 0 ? given.Specs : session.Specs);
        if (session.Proposals.Count == 0)
        {
            if (!File.Exists(settings.Framework))
            {
                return Result.Fail<JsonObject>(new Problem("onboard.framework_missing", $"{settings.Framework} does not exist. Name the framework snapshot with --framework."));
            }

            var framework = FrameworkSnapshotJson.Read(await File.ReadAllTextAsync(settings.Framework, cancellationToken), settings.Framework);
            if (framework is not Result<FrameworkSnapshot>.Ok { Value: var snapshot })
            {
                return Result.Fail<JsonObject>(((Result<FrameworkSnapshot>.Fail)framework).Errors);
            }

            session = session with
            {
                Framework = Path.GetRelativePath(repo, settings.Framework).Replace('\\', '/'),
                Groups = settings.Groups,
                Specs = [.. settings.Specs],
                Proposals = Applicability.Empty(Applicability.InScope(snapshot, settings.Groups)),
            };
        }
        else if (given.Specs.Count > 0)
        {
            session = session with { Specs = [.. given.Specs] };
        }

        var api = await OnboardSystemCommand.ApiAsync(settings.Specs, cancellationToken);
        var skills = SkillCatalogue.Defaults()
            .Bind(defaults => SkillCatalogue.FromFolder(Path.Combine(repo, "skills")).Bind(own => SkillCatalogue.Build(defaults, own)))
            .Bind(catalogue => SkillSelection.Select(catalogue, SkillStep.Applicability, settings.FrameworkName, SkillSelection.DefaultBudgetCharacters));
        if (Result.Combine([api.Map(_ => Unit.Value), skills.Map(_ => Unit.Value)]) is Result<IReadOnlyList<Unit>>.Fail { Errors: var problems })
        {
            return Result.Fail<JsonObject>(problems);
        }

        await OnboardSystemCommand.SaveAsync(settings, session, cancellationToken);
        return Result.Ok(new JsonObject
        {
            ["system"] = system,
            ["framework"] = session.Framework,
            ["groups"] = new JsonArray([.. session.Groups.Select(g => JsonValue.Create(g))]),
            ["supplier"] = session.Supplier is { } supplier
                ? new JsonObject { ["ref_id"] = supplier.EntityRef, ["approved"] = SupplierGate.Check(thirdParties, supplier) == SupplierStatus.Approved }
                : null,
            ["api"] = ((Result<string>.Ok)api).Value,
            ["skills"] = new JsonArray([.. ((Result<IReadOnlyList<Skill>>.Ok)skills).Value.Select(s => new JsonObject
            {
                ["name"] = s.Name,
                ["description"] = s.Description,
                ["source"] = s.Source.ToString().ToLowerInvariant(),
                ["body"] = s.Body,
            })]),
            ["total"] = session.Proposals.Count,
            ["decided"] = session.Decisions.Count,
            ["awaiting_approval"] = new JsonArray([.. session.Drafts.Select(d => JsonValue.Create(d.Key))]),
            ["written"] = session.Written.Count > 0,
            ["pending"] = new JsonArray([.. session.Pending.Select(p => new JsonObject { ["requirement"] = p.Key, ["ref_id"] = p.RefId, ["urn"] = p.Urn, ["text"] = p.Title })]),
        });
    }

    /// <summary>A new session from options: the supplier is the third party that declares the solution, with the contract covering it.</summary>
    private static Result<OnboardSession> Begin(string system, Start start, IReadOnlyList<Core.Scope.ScopeEntity> thirdParties)
    {
        if (string.IsNullOrWhiteSpace(start.Owner) || string.IsNullOrWhiteSpace(start.Product) || string.IsNullOrWhiteSpace(start.Solution))
        {
            return Result.Fail<OnboardSession>(new Problem(
                "onboard.session_not_started",
                $"{system} has no onboarding session yet. Start it with --owner <email> --product <name> --solution <ref_id> (and --framework, --group and --spec as needed)."));
        }

        if (!start.Owner.Contains('@', StringComparison.Ordinal))
        {
            return Result.Fail<OnboardSession>(new Problem("onboard.owner_not_email", $"'{start.Owner}' is not an email. Give the email of a user in CISO Assistant as --owner."));
        }

        var entity = thirdParties.FirstOrDefault(e => e.Solutions.Any(s => s.RefId == start.Solution));
        if (entity is null)
        {
            return Result.Fail<OnboardSession>(new Problem(
                "onboard.supplier_not_drafted",
                $"No third party under scope/third-parties/ provides solution {start.Solution}. Draft the supplier first with 'hound supplier draft'."));
        }

        var contract = entity.Contracts.FirstOrDefault(c => c.Solutions.Any(s => s.Value == start.Solution));
        return contract is null
            ? Result.Fail<OnboardSession>(new Problem("onboard.contract_missing", $"{entity.RefId} has no contract covering {start.Solution}. Add one to its file under scope/third-parties/."))
            : Result.Ok(new OnboardSession(system, OnboardStage.Applicability)
            {
                Owner = start.Owner.Trim(),
                Product = start.Product.Trim(),
                Supplier = new SupplierChoice(entity.RefId, entity.Name, start.Solution, entity.Solutions.First(s => s.RefId == start.Solution).Name, contract.RefId, contract.Name),
            });
    }

    private static async Task<Result<JsonObject>> DecideAsync(
        Settings settings, string requirement, DecisionKind kind, string reasoning, IReadOnlyList<string> sources, string? justification, string? otherSystem, CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.SessionFile))
        {
            return Result.Fail<JsonObject>(new Problem("onboard.session_not_started", $"{settings.System} has no onboarding session yet. Start it with 'hound onboard {settings.System} pending'."));
        }

        return await OnboardSessionJson.Read(await File.ReadAllTextAsync(settings.SessionFile, cancellationToken))
            .Bind(session => Draft(session, requirement, kind, reasoning, sources, justification, otherSystem))
            .BindAsync(async decided =>
            {
                await OnboardSystemCommand.SaveAsync(settings, decided.Session, cancellationToken);
                return Result.Ok(new JsonObject
                {
                    ["requirement"] = decided.Decision.Key,
                    ["kind"] = Applicability.Name(decided.Decision.Kind),
                    ["drafted_by"] = decided.Decision.DraftedBy,
                    ["approved"] = false,
                    ["remaining"] = decided.Session.Pending.Count,
                });
            });
    }

    /// <summary>The session with one more draft. A requirement outside the scope, or one a person already decided, is refused. Pure.</summary>
    private static Result<(OnboardSession Session, Decision Decision)> Draft(
        OnboardSession session, string requirement, DecisionKind kind, string reasoning, IReadOnlyList<string> sources, string? justification, string? otherSystem)
    {
        if (session.Written.Count > 0)
        {
            return Result.Fail<(OnboardSession, Decision)>(new Problem("onboard.already_written", $"{session.System} is onboarded and its files are written; change its audit in a pull request."));
        }

        var named = session.Proposals.Where(p => p.Key == requirement).ToArray() is { Length: > 0 } byKey
            ? byKey
            : [.. session.Proposals.Where(p => p.RefId == requirement || p.Urn == requirement)];
        if (named.Length > 1)
        {
            return Result.Fail<(OnboardSession, Decision)>(new Problem(
                "onboard.requirement_ambiguous", $"{requirement} names {named.Length} requirements in scope for {session.System}. Use the one you mean as 'pending' lists it: {string.Join(", ", named.Select(p => p.Key))}."));
        }

        if (named is not [var proposal])
        {
            return Result.Fail<(OnboardSession, Decision)>(new Problem(
                "onboard.requirement_unknown", $"{requirement} is not a requirement in scope for {session.System}. List them with 'hound onboard {session.System} pending'."));
        }

        if (session.Decisions.FirstOrDefault(d => d.Key == proposal.Key) is { AwaitsApproval: false } accepted)
        {
            return Result.Fail<(OnboardSession, Decision)>(new Problem(
                "onboard.decided_by_person", $"{proposal.Key} was decided by {accepted.AcceptedBy}; an agent's draft does not replace a person's decision."));
        }

        return Applicability.Draft(proposal, kind, reasoning, sources, justification, otherSystem, DraftedBy).Map(decision => (session.Decide(decision), decision));
    }

    /// <summary>The pending list for a person: the same facts as the JSON, as text.</summary>
    private static string Text(JsonObject answer)
    {
        var system = (string?)answer["system"];
        var pending = answer["pending"]!.AsArray();
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{system}: {(int)answer["decided"]!} of {(int)answer["total"]!} decided; {pending.Count} to decide.\n");
        foreach (var requirement in pending)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {(string?)requirement!["requirement"]}: {(string?)requirement["text"]}\n");
        }

        if (answer["awaiting_approval"]!.AsArray().Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"{answer["awaiting_approval"]!.AsArray().Count} draft(s) wait for a person's 'hound approve'.\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"Decide each with: hound onboard {system} decide <requirement> --kind <api_check|manual|not_applicable|other_system> --reasoning <text>");
        return text.ToString();
    }
}
