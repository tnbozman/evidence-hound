using System.CommandLine;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Cli.Setup;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Sprs;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound ssp build</c> and <c>hound ssp poam</c>: a programme's system security plan and its POA&amp;M, assembled from one read of CISO
/// Assistant and the repo (s11-02, s11-03). Neither writes anything to CISO Assistant.
/// </summary>
internal static class SspCommands
{
    private static readonly string DraftedWithAi = "drafted with AI: review before merging";

    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.ssp");
        var programme = new Option<string>("--programme") { Description = "The programme's id from scope/setup.yaml, such as essential-eight.", Required = true };
        var scope = new Option<DirectoryInfo>("--scope") { Description = "The scope folder. Default: scope/ in the current folder." };
        var config = new Option<FileInfo?>("--config") { Description = "The deployment config, for the CISO Assistant connection when HOUND_CA_URL and HOUND_CA_TOKEN are not set." };
        var json = new Option<bool>("--json") { Description = "Answer in JSON, each value with its source." };
        var strict = new Option<bool>("--strict") { Description = "Exit non-zero while any gap remains: a requirement with no statement, no owner, or not applicable without a justification." };
        var snapshotOut = new Option<FileInfo?>("--snapshot-out") { Description = "Also write what the plan was built from to this file, and log the draft's SHA-256 for the sign-off." };
        var fromSnapshot = new Option<FileInfo?>("--from-snapshot") { Description = "Build from a snapshot file instead of CISO Assistant: the signed version when its sign-off matches, else an error." };
        var format = new Option<string>("--format") { Description = "markdown (the default) or docx: docx also writes the plan as a Word file to --out.", DefaultValueFactory = _ => "markdown" };
        format.AcceptOnlyFromAmong("markdown", "docx");
        var outFile = new Option<FileInfo?>("--out") { Description = "With --format docx, the Word file to write. Keep it out of the repo: it holds the organisation's system details." };
        var build = new Command("build", "Assemble a programme's system security plan from CISO Assistant and the repo, gaps first. Reads only; writes nothing to CISO Assistant.")
        {
            programme, scope, config, json, strict, snapshotOut, fromSnapshot, format, outFile,
        };
        build.SetAction(async (parseResult, cancellationToken) =>
        {
            var docx = parseResult.GetValue(format) == "docx";
            if (docx && parseResult.GetValue(outFile) is null)
            {
                return Log.Problems(logger, [new Problem("ssp.out_required", "--format docx needs --out <file> for the Word file.")]);
            }

            var (exit, checkedScope, ssp) = await CheckAsync(parseResult, programme, scope, config, configuration, registry, logger, cancellationToken);
            if (ssp is null)
            {
                return exit;
            }

            var from = parseResult.GetValue(fromSnapshot);
            var (readExit, snapshot) = from is not null
                ? await ReadSnapshotAsync(from, logger, cancellationToken)
                : await LiveSnapshotAsync(parseResult, scope, config, configuration, registry, checkedScope!, ssp, loggers, logger, cancellationToken);
            if (snapshot is null)
            {
                return readExit;
            }

            var draft = SspLayout.Markdown(snapshot.Document, snapshot.Sections);
            if (parseResult.GetValue(snapshotOut) is { } file)
            {
                var text = SspSnapshotJson.Write(snapshot);
                await File.WriteAllTextAsync(file.FullName, text, cancellationToken);
                var (draftHash, snapshotHash) = (SspVersion.Hash(draft), SspVersion.Hash(text));
                Log.SspSnapshotWritten(logger, file.FullName, draftHash, snapshotHash);
            }

            var plan = draft;
            var approved = false;
            if (ssp.SignOffs.FirstOrDefault(s => s.Version == snapshot.Document.Version.Value) is { } signOff)
            {
                var signed = SspVersion.Sign(snapshot, signOff);
                if (from is not null && signed is Result<string>.Fail failed)
                {
                    return Log.Problems(logger, failed.Errors);
                }

                approved = signed is Result<string>.Ok;
                plan = signed.Match(text => text, _ =>
                {
                    Log.SspSignedButChanged(logger, signOff.Version);
                    return draft;
                });
            }

            if (docx)
            {
                await File.WriteAllBytesAsync(parseResult.GetValue(outFile)!.FullName, SspWord.Docx(plan, draft: !approved), cancellationToken);
            }

            await output.WriteAsync(parseResult.GetValue(json) ? SspJson.Write(snapshot.Document) : plan);
            return parseResult.GetValue(strict) && snapshot.Document.Gaps.Count > 0 ? ExitCodes.SspGaps : ExitCodes.Ok;
        });
        var poam = new Command("poam", "The plan of action and milestones for a programme: every requirement not yet met, with its control, owner, ETA and status. Reads only.")
        {
            programme, scope, config, json,
        };
        poam.SetAction(async (parseResult, cancellationToken) =>
        {
            var (exit, checkedScope, ssp) = await CheckAsync(parseResult, programme, scope, config, configuration, registry, logger, cancellationToken);
            if (ssp is null)
            {
                return exit;
            }

            var (readExit, document) = await DocumentAsync(parseResult, config, configuration, registry, checkedScope!, ssp, loggers, logger, cancellationToken);
            if (document is null)
            {
                return readExit;
            }

            var items = Poam.Of(document);
            await output.WriteAsync(parseResult.GetValue(json) ? SspJson.Write(items) : Poam.Markdown(items));
            return ExitCodes.Ok;
        });
        return new Command("ssp", "System security plans: one per programme, assembled from CISO Assistant and the repo.")
        {
            build, poam, Diff(output, logger), Publish(loggers, logger, configuration, registry, programme, scope, config),
            Suggest(output, loggers, logger, configuration, registry, programme, scope, config, fromSnapshot), Describe(registry, configuration, logger, scope, config),
        };
    }

    /// <summary>
    /// <c>hound ssp suggest</c> (s11-06): the brief for the person's own agent to draft control descriptions from. hound has no model and
    /// sends nothing; the brief holds names only, never evidence content.
    /// </summary>
    private static Command Suggest(
        TextWriter output, ILoggerFactory loggers, ILogger logger, IConfiguration configuration, CollectorRegistry registry,
        Option<string> programme, Option<DirectoryInfo> scope, Option<FileInfo?> config, Option<FileInfo?> fromSnapshot)
    {
        var suggest = new Command("suggest", "List, as JSON for your own agent, the controls with no description that leave an implemented requirement without a statement. No model; sends nothing.")
        {
            programme, scope, config, fromSnapshot,
        };
        suggest.SetAction(async (parseResult, cancellationToken) =>
        {
            var (exit, checkedScope, ssp) = await CheckAsync(parseResult, programme, scope, config, configuration, registry, logger, cancellationToken);
            if (ssp is null)
            {
                return exit;
            }

            var (readExit, snapshot) = parseResult.GetValue(fromSnapshot) is { } from
                ? await ReadSnapshotAsync(from, logger, cancellationToken)
                : await LiveSnapshotAsync(parseResult, scope, config, configuration, registry, checkedScope!, ssp, loggers, logger, cancellationToken);
            if (snapshot is null)
            {
                return readExit;
            }

            await output.WriteAsync(SspSuggest.Json(ssp.Programme.Value, SspSuggest.Brief(snapshot.Document)));
            return ExitCodes.Ok;
        });
        return suggest;
    }

    /// <summary>
    /// <c>hound ssp describe</c> (s11-06): writes a control's description into the scope file that declares it, under a
    /// <c># drafted with AI</c> comment, for review in a pull request. Never writes to CISO Assistant.
    /// </summary>
    private static Command Describe(CollectorRegistry registry, IConfiguration configuration, ILogger logger, Option<DirectoryInfo> scope, Option<FileInfo?> config)
    {
        var control = new Option<string>("--control") { Description = "The applied control's ref_id, declared in scope/controls.yaml.", Required = true };
        var text = new Option<string>("--text") { Description = "The description: what the control does and on which systems.", Required = true };
        var describe = new Command("describe", "Write a control description drafted with AI into scope/controls.yaml, marked for review in the pull request. Writes nothing to CISO Assistant.")
        {
            control, text, scope, config,
        };
        describe.SetAction(async (parseResult, cancellationToken) =>
        {
            var checkedScope = await ScopeCommands.ValidateAsync(Scope(parseResult, scope), parseResult.GetValue(config), registry, AuditSeries.Today(configuration), cancellationToken);
            var refId = parseResult.GetRequiredValue(control);
            if (checkedScope.Scope.Controls.FirstOrDefault(c => c.RefId == refId) is not { } declared)
            {
                return Log.Problems(logger, [new Problem("ssp.control_not_in_scope", $"control '{refId}' is not declared in the scope; describe a control from scope/controls.yaml.")]);
            }

            var file = Path.GetFullPath(declared.At.File, Scope(parseResult, scope).FullName);
            var description = string.Join(' ', parseResult.GetRequiredValue(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var edited = YamlEdit.WriteScalar(await File.ReadAllTextAsync(file, cancellationToken), $"controls[ref_id={refId}].description", description, DraftedWithAi);
            if (edited is Result<string>.Fail failed)
            {
                return Log.Problems(logger, failed.Errors);
            }

            await File.WriteAllTextAsync(file, ((Result<string>.Ok)edited).Value, cancellationToken);
            return ExitCodes.Ok;
        });
        return describe;
    }

    /// <summary><c>hound ssp diff</c>: what changed between two versions' snapshots, boundary and inventory first. Reads only the two files.</summary>
    private static Command Diff(TextWriter output, ILogger logger)
    {
        var before = new Argument<FileInfo>("before") { Description = "The earlier version's snapshot, from 'hound ssp build --snapshot-out'." };
        var after = new Argument<FileInfo>("after") { Description = "The later version's snapshot." };
        var diff = new Command("diff", "List what changed between two versions of a plan, from their snapshots: boundary, inventory, suppliers, statuses, statements and sections.")
        {
            before, after,
        };
        diff.SetAction(async (parseResult, cancellationToken) =>
        {
            var (beforeExit, a) = await ReadSnapshotAsync(parseResult.GetRequiredValue(before), logger, cancellationToken);
            if (a is null)
            {
                return beforeExit;
            }

            var (afterExit, b) = await ReadSnapshotAsync(parseResult.GetRequiredValue(after), logger, cancellationToken);
            if (b is null)
            {
                return afterExit;
            }

            await output.WriteAsync(SspDiff.Markdown(a.Document.Version.Value ?? "", b.Document.Version.Value ?? "", SspDiff.Of(a, b)));
            return ExitCodes.Ok;
        });
        return diff;
    }

    /// <summary>
    /// <c>hound ssp publish</c>: uploads a signed version (plan, POA&amp;M, snapshot and CISO Assistant's audit report, zipped) as a new revision
    /// of the programme's SSP evidence. Refuses a version with no sign-off, or one whose snapshot is not the reviewed draft.
    /// </summary>
    private static Command Publish(
        ILoggerFactory loggers, ILogger logger, IConfiguration configuration, CollectorRegistry registry, Option<string> programme, Option<DirectoryInfo> scope, Option<FileInfo?> config)
    {
        var snapshotFile = new Option<FileInfo>("--snapshot") { Description = "The signed version's snapshot, from 'hound ssp build --snapshot-out'.", Required = true };
        var publish = new Command("publish", "Upload a signed version of the plan to CISO Assistant as a new evidence revision, with its POA&M, snapshot and the audit's Word report.")
        {
            programme, scope, config, snapshotFile,
        };
        publish.SetAction(async (parseResult, cancellationToken) =>
        {
            var (exit, _, ssp) = await CheckAsync(parseResult, programme, scope, config, configuration, registry, logger, cancellationToken);
            if (ssp is null)
            {
                return exit;
            }

            var file = parseResult.GetRequiredValue(snapshotFile);
            var (readExit, snapshot) = await ReadSnapshotAsync(file, logger, cancellationToken);
            if (snapshot is null)
            {
                return readExit;
            }

            var version = snapshot.Document.Version.Value ?? "";
            if (ssp.SignOffs.FirstOrDefault(s => s.Version == version) is not { } signOff)
            {
                return Log.Problems(logger, [new Problem(
                    "ssp.not_signed", $"version {version} of the plan for programme '{ssp.Programme.Value}' has no sign-off in scope/ssp.yaml; only a signed version is published.")]);
            }

            var signed = SspVersion.Sign(snapshot, signOff);
            if (signed is Result<string>.Fail failed)
            {
                return Log.Problems(logger, failed.Errors);
            }

            var plan = ((Result<string>.Ok)signed).Value;
            var snapshotText = await File.ReadAllTextAsync(file.FullName, cancellationToken);
            var connection = await PublishCommand.ConnectionAsync(configuration, parseResult.GetValue(config), registry, cancellationToken);
            return await connection.Match(
                c => UploadAsync(snapshot, plan, snapshotText, c, loggers, logger, cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return publish;
    }

    private static async Task<int> UploadAsync(
        SspSnapshot snapshot, string plan, string snapshotText, CisoAssistantOptions connection, ILoggerFactory loggers, ILogger logger, CancellationToken cancellationToken)
    {
        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(connection).BuildServiceProvider();
        var client = services.GetRequiredService<ICisoAssistantClient>();
        var document = snapshot.Document;
        var version = document.Version.Value ?? "";
        var report = (await client.DownloadAuditReportAsync(document.Audit.Value ?? "", cancellationToken)).Match<EvidenceFile?>(
            r => r,
            problems =>
            {
                Log.SspReportMissing(logger, string.Join("; ", problems.Select(p => p.Message)));
                return null;
            });
        var package = SspPackage.Zip(document.Programme, version, plan, Poam.Markdown(Poam.Of(document)), snapshotText, report);
        var refId = "SSP-" + document.Programme.ToUpperInvariant();
        var evidence = await client.FindOrCreateEvidenceAsync(refId, $"System security plan: {document.SystemName.Value}", cancellationToken);
        var sent = await evidence.Match(
            e => client.UploadEvidenceRevisionAsync(e.Id, package, cancellationToken),
            problems => Task.FromResult(Result.Fail<Unit>(problems)));
        return sent.Match(
            _ =>
            {
                Log.SspPublished(logger, version, refId, package.FileName);
                return ExitCodes.Ok;
            },
            problems => Log.Problems(logger, problems));
    }

    /// <summary>Checks the scope and finds the programme's plan; on a problem, the exit code and no plan.</summary>
    private static async Task<(int Exit, ScopeCommands.CheckedScope? Scope, ScopeSsp? Ssp)> CheckAsync(
        ParseResult parseResult, Option<string> programme, Option<DirectoryInfo> scope, Option<FileInfo?> config, IConfiguration configuration, CollectorRegistry registry,
        ILogger logger, CancellationToken cancellationToken)
    {
        var checkedScope = await ScopeCommands.ValidateAsync(Scope(parseResult, scope), parseResult.GetValue(config), registry, AuditSeries.Today(configuration), cancellationToken);
        if (checkedScope.Problems.Count > 0)
        {
            return (Log.Problems(logger, checkedScope.Problems.Select(p => p.ToProblem())), null, null);
        }

        var id = parseResult.GetRequiredValue(programme);
        return checkedScope.Scope.Ssps.FirstOrDefault(s => s.Programme.Value == id) is { } ssp
            ? (ExitCodes.Ok, checkedScope, ssp)
            : (Log.Problems(logger, [new Problem("ssp.not_declared", $"no plan is declared for programme '{id}'; declare it in scope/ssp.yaml.")]), null, null);
    }

    private static async Task<(int Exit, SspSnapshot? Snapshot)> ReadSnapshotAsync(FileInfo file, ILogger logger, CancellationToken cancellationToken)
    {
        if (!file.Exists)
        {
            return (Log.Problems(logger, [new Problem("ssp.snapshot_missing", $"{file.FullName}: no such snapshot file.")]), null);
        }

        return SspSnapshotJson.Read(await File.ReadAllTextAsync(file.FullName, cancellationToken), file.Name).Match<(int, SspSnapshot?)>(
            s => (ExitCodes.Ok, s),
            problems => (Log.Problems(logger, problems), null));
    }

    private static async Task<(int Exit, SspSnapshot? Snapshot)> LiveSnapshotAsync(
        ParseResult parseResult, Option<DirectoryInfo> scope, Option<FileInfo?> config, IConfiguration configuration, CollectorRegistry registry,
        ScopeCommands.CheckedScope checkedScope, ScopeSsp ssp, ILoggerFactory loggers, ILogger logger, CancellationToken cancellationToken)
    {
        var (exit, document) = await DocumentAsync(parseResult, config, configuration, registry, checkedScope, ssp, loggers, logger, cancellationToken);
        if (document is null)
        {
            return (exit, null);
        }

        var repo = Path.GetDirectoryName(Path.GetFullPath(Scope(parseResult, scope).FullName))!;
        return (ExitCodes.Ok, new SspSnapshot(document, await SectionsAsync(document, repo, cancellationToken)));
    }

    /// <summary>Reads CISO Assistant once and builds the plan; on a problem, the exit code and no plan.</summary>
    private static async Task<(int Exit, SspDocument? Document)> DocumentAsync(
        ParseResult parseResult, Option<FileInfo?> config, IConfiguration configuration, CollectorRegistry registry, ScopeCommands.CheckedScope checkedScope, ScopeSsp ssp,
        ILoggerFactory loggers, ILogger logger, CancellationToken cancellationToken)
    {
        var connection = await PublishCommand.ConnectionAsync(configuration, parseResult.GetValue(config), registry, cancellationToken);
        return await connection.Match(
            c => ReadAsync(ssp, checkedScope, c, loggers, logger, cancellationToken),
            problems => Task.FromResult<(int, SspDocument?)>((Log.Problems(logger, problems), null)));
    }

    private static async Task<(int Exit, SspDocument? Document)> ReadAsync(
        ScopeSsp ssp, ScopeCommands.CheckedScope checkedScope, CisoAssistantOptions connection, ILoggerFactory loggers, ILogger logger, CancellationToken cancellationToken)
    {
        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(connection).BuildServiceProvider();
        var programme = checkedScope.Scope.Setup.Programmes.Single(p => p.Id == ssp.Programme.Value);
        var today = checkedScope.Inputs.Today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var query = ScopeStateQuery.For(checkedScope.Scope, ScopeDesired.From(checkedScope.Scope, checkedScope.Inputs)) with
        {
            ThirdParties = true,
            AllRowsOf = new HashSet<string>(StringComparer.Ordinal) { SspBuilder.AuditOfRecord(checkedScope.Scope, programme, today) },
        };
        var held = await services.GetRequiredService<IScopeStateSource>().ReadAsync(query, cancellationToken);
        // s17-03: the nist-800-171 profile flags what CMMC does not allow on a POA&M, by the bundled SPRS weights.
        var sprs = ssp.Profile == SspProfile.Nist800171 ? (await SprsCommands.MethodologyAsync(null, cancellationToken)).Match<SprsMethodology?>(m => m, _ => null) : null;
        return held.Match<(int, SspDocument?)>(
            objects => (ExitCodes.Ok, SspBuilder.Build(ssp, checkedScope.Scope, objects, Snapshot(checkedScope, programme.Audit.Value), today, sprs)),
            problems => (Log.Problems(logger, problems), null));
    }

    private static DirectoryInfo Scope(ParseResult parseResult, Option<DirectoryInfo> scope) =>
        parseResult.GetValue(scope) ?? new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "scope"));

    private static FrameworkSnapshot? Snapshot(ScopeCommands.CheckedScope checkedScope, string audit)
    {
        var framework = checkedScope.Scope.Setup.Programmes.Single(p => p.Audit.Value == audit).Framework.Value;
        return checkedScope.Inputs.Snapshots.GetValueOrDefault(framework);
    }

    /// <summary>The hand-written sections' text, read from their files relative to the repo (the scope folder's parent); a missing file reads as empty.</summary>
    private static async Task<Dictionary<string, string>> SectionsAsync(SspDocument document, string repo, CancellationToken cancellationToken)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in document.Sections)
        {
            var path = section.Value.Value is { } file ? Path.GetFullPath(file, repo) : null;
            if (path is not null && path.StartsWith(repo + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(path))
            {
                texts[section.Name] = await File.ReadAllTextAsync(path, cancellationToken);
            }
        }

        return texts;
    }
}
