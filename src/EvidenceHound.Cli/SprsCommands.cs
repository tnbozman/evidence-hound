using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Reporting;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Sprs;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>s17-05: <c>hound sprs score</c>, the NIST SP 800-171 DoD Assessment Methodology score of the CMMC Level 2 audit.</summary>
internal static class SprsCommands
{
    /// <summary>The methodology hound ships, from <c>frameworks/sprs/</c>.</summary>
    internal const string BundledMethodology = "sprs/dod-assessment-methodology-1.2.1.yaml";

    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.sprs");
        var audit = new Option<string>("--audit") { Description = "The ref_id of the CMMC Level 2 audit in CISO Assistant, such as EX-CMMC-2026.", Required = true };
        var config = new Option<FileInfo>("--config") { Description = "The deployment config: the CISO Assistant connection.", Required = true };
        var methodology = new Option<FileInfo?>("--methodology") { Description = "A methodology file to use instead of the bundled DoD Assessment Methodology 1.2.1 (frameworks/sprs/ in the Evidence Hound repo)." };
        var control = new Option<string?>("--control") { Description = "The ref_id of the applied control the score is uploaded to as evidence, such as your 'SPRS score kept current' control. Without it nothing is written to CISO Assistant." };
        var format = new Option<string>("--format") { Description = "text (the default) or json.", DefaultValueFactory = _ => "text" };
        var score = new Command(
            "score",
            "Work out the score from the audit's rows: 110, less each requirement's points unless its row is compliant or not applicable. Rows not assessed or missing count as not implemented. A person reviews each deduction before entering the score in SPRS.")
        {
            audit, config, methodology, control, format,
        };
        score.SetAction((parseResult, cancellationToken) => ScoreAsync(
            parseResult.GetRequiredValue(audit),
            parseResult.GetRequiredValue(config),
            parseResult.GetValue(methodology),
            parseResult.GetValue(control),
            parseResult.GetValue(format) == "json",
            output,
            loggers,
            logger,
            configuration,
            registry,
            cancellationToken));
        return new Command("sprs", "The NIST SP 800-171 DoD Assessment Methodology score that suppliers holding CUI enter in SPRS.")
        {
            score, AffirmationPack(output, loggers, logger, configuration, registry, config, methodology),
        };
    }

    /// <summary>s17-06: <c>hound sprs affirmation-pack</c>, what the affirming official reads before the yearly affirmation.</summary>
    private static Command AffirmationPack(
        TextWriter output, ILoggerFactory loggers, ILogger logger, IConfiguration configuration, CollectorRegistry registry, Option<FileInfo> config, Option<FileInfo?> methodology)
    {
        var programme = new Option<string>("--programme") { Description = "The CMMC Level 2 programme's id from scope/setup.yaml, with its plan in scope/ssp.yaml, such as cmmc-level-2.", Required = true };
        var period = new Option<string>("--period") { Description = "The month (YYYY-MM) of the posture snapshot to read freshness and lapsed sources from: take it first with hound report snapshot.", Required = true };
        var scope = new Option<DirectoryInfo>("--scope") { Description = "The scope folder. Default: scope/ in the current folder." };
        var outFile = new Option<FileInfo?>("--out") { Description = "Write the pack to this file instead of printing it. Keep it out of the repo." };
        var command = new Command(
            "affirmation-pack",
            "Gather what the affirming official reads before the yearly CMMC affirmation: the SPRS score, open POA&M items with their dates, evidence freshness and lapsed manual sources. Reads only; the official affirms.")
        {
            programme, period, scope, config, methodology, outFile,
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var configFile = parseResult.GetRequiredValue(config);
            var weights = await MethodologyAsync(parseResult.GetValue(methodology), cancellationToken);
            var deployment = await DeploymentFile.LoadAsync(configFile, registry, cancellationToken);
            var posture = await ReportingPeriod.Parse(parseResult.GetRequiredValue(period))
                .Bind(p => deployment.Map(d => (Period: p, d.Evidence.Root)))
                .BindAsync(x => ReportCommands.ReadSnapshotAsync(x.Root, x.Period, cancellationToken));
            var inputs = weights.Bind(w => posture.Map(p => (Weights: w, Posture: p)));
            if (inputs is not Result<(SprsMethodology Weights, PostureSnapshot Posture)>.Ok { Value: var read })
            {
                return Log.Problems(logger, inputs.Match(_ => [], ps => ps));
            }

            var scopeDir = parseResult.GetValue(scope) ?? new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "scope"));
            var (exit, plan) = await SspCommands.PlanAsync(parseResult.GetRequiredValue(programme), scopeDir, configFile, configuration, registry, loggers, logger, cancellationToken);
            if (plan is null)
            {
                return exit;
            }

            var pack = Core.Sprs.AffirmationPack.Markdown(plan, read.Weights, read.Posture, AuditSeries.Today(configuration));
            if (parseResult.GetValue(outFile) is { } file)
            {
                await File.WriteAllTextAsync(file.FullName, pack, cancellationToken);
                return ExitCodes.Ok;
            }

            await output.WriteAsync(pack);
            return ExitCodes.Ok;
        });
        return command;
    }

    private static async Task<int> ScoreAsync(
        string auditRef, FileInfo configFile, FileInfo? methodologyFile, string? control, bool json, TextWriter output, ILoggerFactory loggers, ILogger logger,
        IConfiguration configuration, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var methodology = await MethodologyAsync(methodologyFile, cancellationToken);
        if (methodology is not Result<SprsMethodology>.Ok { Value: var weights })
        {
            return Log.Problems(logger, methodology.Match(_ => [], ps => ps));
        }

        var connection = await CisoAssistantConnection.ResolveAsync(configuration, configFile, registry, cancellationToken);
        if (connection is not Result<CisoAssistantOptions>.Ok { Value: var options })
        {
            return Log.Problems(logger, connection.Match(_ => [], ps => ps));
        }

        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(options).BuildServiceProvider();
        var client = services.GetRequiredService<ICisoAssistantClient>();
        var rows = await client.ListRequirementAssessmentsAsync(auditRef, cancellationToken);
        if (rows is not Result<IReadOnlyList<CurrentRow>>.Ok { Value: var current })
        {
            return Log.Problems(logger, rows.Match(_ => [], ps => ps));
        }

        var date = AuditSeries.Today(configuration);
        var scored = SprsScoring.Score(weights, [.. current.Where(r => r.RefId is not null).Select(r => new SprsRow(r.RefId!, r.Result))]);
        var jsonText = Json(auditRef, date, weights, scored);
        await output.WriteAsync(json ? jsonText : Text(auditRef, date, weights, scored));
        if (control is null)
        {
            return ExitCodes.Ok;
        }

        var name = $"{auditRef}-{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        var uploaded = await UploadAsync(client, name, $"SPRS score {auditRef} {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", control, jsonText, cancellationToken);
        return await uploaded.Match(
            async _ =>
            {
                if (!json)
                {
                    await output.WriteLineAsync($"uploaded hound-sprs/{name} to applied control {control}");
                }

                return ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    internal static async Task<Result<SprsMethodology>> MethodologyAsync(FileInfo? file, CancellationToken cancellationToken)
    {
        if (file is not null)
        {
            return file.Exists
                ? SprsMethodologyReader.Parse(await File.ReadAllTextAsync(file.FullName, cancellationToken), file.ToString())
                : Result.Fail<SprsMethodology>(new Problem("sprs.methodology_missing", $"{file}: no such methodology file."));
        }

        await using var stream = typeof(SprsCommands).Assembly.GetManifestResourceStream(BundledMethodology)!;
        using var reader = new StreamReader(stream);
        return SprsMethodologyReader.Parse(await reader.ReadToEndAsync(cancellationToken), "frameworks/" + BundledMethodology);
    }

    private static string Text(string audit, DateOnly date, SprsMethodology methodology, SprsScore score)
    {
        var lines = new List<string>
        {
            $"SPRS score for {audit} on {Day(date)}: {score.Score} (from {methodology.Start}, {methodology.Name} {methodology.Version})",
        };
        if (score.SspMissing)
        {
            lines.Add("No system security plan (3.12.4) is recorded as compliant, so an assessment could not be completed (DFARS 252.204-7012).");
        }

        lines.Add($"Deductions ({score.Deductions.Count}, {score.Deductions.Sum(d => d.Points)} points):");
        lines.AddRange(score.Deductions.Select(Line));
        lines.Add("Review each deduction with the audit before you enter the score in SPRS. A practice recorded as not applicable takes no points off.");
        return string.Join('\n', lines) + "\n";
    }

    private static string Line(SprsDeduction d) =>
        d.Practice is null
            ? $"  -{d.Points}  {d.Requirement}  not in the audit"
            : $"  -{d.Points}  {d.Requirement}  {d.Practice}  {Names.Of(d.Result!.Value)}" + (d.Note is { } note ? $"  {note}" : "");

    private static string Json(string audit, DateOnly date, SprsMethodology methodology, SprsScore score) =>
        new JsonObject
        {
            ["audit"] = audit,
            ["date"] = Day(date),
            ["methodology"] = $"{methodology.Name} {methodology.Version}",
            ["start"] = methodology.Start,
            ["score"] = score.Score,
            ["ssp_missing"] = score.SspMissing,
            ["deductions"] = new JsonArray([.. score.Deductions.Select(d => (JsonNode)new JsonObject
            {
                ["requirement"] = d.Requirement,
                ["practice"] = d.Practice,
                ["result"] = d.Result is { } result ? Names.Of(result) : null,
                ["points"] = d.Points,
                ["note"] = d.Note,
            })]),
        }.ToJsonString(CommandJson.Escaped) + "\n";

    private static async Task<Result<Unit>> UploadAsync(ICisoAssistantClient client, string name, string title, string control, string json, CancellationToken cancellationToken)
    {
        var evidence = await client.FindOrCreateEvidenceAsync($"hound-sprs/{name}", title, cancellationToken);
        return await evidence.BindAsync(async e =>
            (await client.UploadEvidenceRevisionAsync(e.Id, new EvidenceFile(name + ".json", Encoding.UTF8.GetBytes(json)), cancellationToken))
                .Map(_ => e.Id))
            .BindAsync(id => client.LinkEvidenceToAppliedControlAsync(control, id, cancellationToken));
    }

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

}
