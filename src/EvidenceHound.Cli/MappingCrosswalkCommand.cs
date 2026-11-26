using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// s12-08: <c>hound mapping crosswalk</c>: which requirements of another framework (NIST SP 800-171 first) a programme's evidence answers
/// today. Reads the crosswalk, the programme's mapping and, unless offline, the audit of record. Writes nothing anywhere.
/// </summary>
internal static class MappingCrosswalkCommand
{
    /// <summary>Crosswalks hound carries, by embedded resource name under <c>crosswalks/</c>.</summary>
    private static readonly string BundledPrefix = "crosswalks/";

    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.mapping");
        var from = new Option<string>("--from") { Description = "The programme whose evidence answers the other framework, by id (see hound programme list).", Required = true };
        var to = new Option<string?>("--to") { Description = "The other framework's URN. Default: the only crosswalk from the programme's framework." };
        var file = new Option<FileInfo?>("--file") { Description = "A crosswalk file. Default: mappings/crosswalks/ next to the config, then the crosswalks hound carries." };
        var scope = new Option<DirectoryInfo?>("--scope") { Description = "The scope folder. Default: scope/ in the current folder." };
        var config = new Option<FileInfo>("--config") { Description = "The deployment config: the programme's mapping and the CISO Assistant connection.", Required = true };
        var offline = new Option<bool>("--offline") { Description = "Do not read today's results from CISO Assistant; show only what would answer each requirement." };
        var json = new Option<bool>("--json") { Description = "Print JSON instead of Markdown." };
        var command = new Command(
            "crosswalk",
            "Show which requirements of another framework (such as NIST SP 800-171) the programme's evidence answers today, partly answers or leaves open. Advisory: writes nothing.")
        {
            from, to, file, scope, config, offline, json,
        };
        command.SetAction((parseResult, cancellationToken) => RunAsync(
            new Request(
                parseResult.GetRequiredValue(from),
                parseResult.GetValue(to),
                parseResult.GetValue(file),
                parseResult.GetValue(scope) ?? new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "scope")),
                parseResult.GetRequiredValue(config),
                parseResult.GetValue(offline),
                parseResult.GetValue(json)),
            output,
            loggers,
            logger,
            configuration,
            registry,
            cancellationToken));
        return command;
    }

    /// <summary>The checks people attest: those of the config's <c>type: manual</c> collectors.</summary>
    internal static IReadOnlySet<CheckId> ManualChecks(DeploymentConfig deployment) =>
        deployment.Collectors
            .Where(c => c.Type == "manual" && c.Settings.ContainsKey("checks"))
            .SelectMany(c => c.Settings["checks"].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(CheckId.Create)
            .SelectMany(r => r.Match(id => new[] { id }, _ => []))
            .ToHashSet();

    private static async Task<int> RunAsync(
        Request request, TextWriter output, ILoggerFactory loggers, ILogger logger, IConfiguration configuration, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var today = AuditSeries.Today(configuration);
        var checkedScope = await ScopeCommands.ValidateAsync(request.Scope, null, registry, today, cancellationToken);
        if (checkedScope.Problems.Count > 0)
        {
            return Log.Problems(logger, checkedScope.Problems.Select(p => p.ToProblem()));
        }

        var programmes = checkedScope.Scope.Setup.Programmes;
        if (programmes.FirstOrDefault(p => p.Id == request.From) is not { } programme)
        {
            return Log.Problems(logger, [new Problem(
                "crosswalk.programme_unknown",
                $"No programme '{request.From}' in {request.Scope}; the programmes are {string.Join(", ", programmes.Select(p => p.Id))}.")]);
        }

        var deployment = await ConfigCommands.LoadAsync(request.Config, registry, cancellationToken);
        var crosswalk = await FindAsync(request, programme, cancellationToken);
        var mapping = await deployment.BindAsync(d => MappingAsync(d, programme, cancellationToken));
        var setup = deployment.Bind(d => crosswalk.Bind(c => mapping.Map(m => (Deployment: d, Crosswalk: c, Mapping: m))));
        if (setup is not Result<(DeploymentConfig Deployment, CrosswalkDefinition Crosswalk, LoadedMapping Mapping)>.Ok { Value: var ready })
        {
            return Log.Problems(logger, setup.Match(_ => [], ps => ps));
        }

        var cycles = await AuditSeries.CyclesAsync(request.Scope.FullName, cancellationToken);
        var auditRef = request.Offline ? null : AuditPeriods.Target(programme.Audit.Value, cycles, today);
        var rows = auditRef is null
            ? Result.Ok<IReadOnlyList<CurrentRow>>([])
            : await ReadRowsAsync(auditRef, request.Config, configuration, loggers, registry, cancellationToken);
        if (rows is not Result<IReadOnlyList<CurrentRow>>.Ok { Value: var current })
        {
            return Log.Problems(logger, [.. rows.Match(_ => [], ps => ps), TryOffline]);
        }

        var framework = checkedScope.Inputs.Snapshots[programme.Framework.Value];
        var answers = Crosswalks.Answers(ready.Mapping.Mapping, framework, ManualChecks(ready.Deployment), current);
        var report = new Report(ready.Crosswalk, programme, auditRef, Crosswalks.Build(ready.Crosswalk, answers));
        await output.WriteAsync(request.Json ? Json(report) : Markdown(report));
        return ExitCodes.Ok;
    }

    private static readonly Problem TryOffline = new(
        "crosswalk.results_not_read",
        "Today's results could not be read from CISO Assistant. To see what would answer each requirement without them, run again with --offline.");

    private static async Task<Result<LoadedMapping>> MappingAsync(DeploymentConfig deployment, ScopeProgramme programme, CancellationToken cancellationToken) =>
        deployment.Targets.FirstOrDefault(t => t.ComplianceAssessmentRefId == programme.Audit.Value) is { } target
            ? await MappingCommands.LoadAsync(new FileInfo(target.MappingPath), cancellationToken)
            : Result.Fail<LoadedMapping>(new Problem(
                "crosswalk.mapping_missing",
                $"The config has no target for {programme.Audit.Value}, the audit of programme '{programme.Id}', so hound does not know which checks answer it. Add one under 'targets'."));

    private static async Task<Result<IReadOnlyList<CurrentRow>>> ReadRowsAsync(
        string auditRef, FileInfo configFile, IConfiguration configuration, ILoggerFactory loggers, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var connection = await CisoAssistantConnection.ResolveAsync(configuration, configFile, registry, cancellationToken);
        return await connection.BindAsync(async options =>
        {
            using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(options).BuildServiceProvider();
            return await services.GetRequiredService<ICisoAssistantClient>().ListRequirementAssessmentsAsync(auditRef, cancellationToken);
        });
    }

    /// <summary>The crosswalk: the file named, else the repo's own for the programme's framework, else the one hound carries.</summary>
    private static async Task<Result<CrosswalkDefinition>> FindAsync(Request request, ScopeProgramme programme, CancellationToken cancellationToken)
    {
        if (request.File is { } file)
        {
            return (await ReadFileAsync(file.FullName, cancellationToken)).Bind(c => Fits(c, programme, request.To)
                ? Result.Ok(c)
                : Result.Fail<CrosswalkDefinition>(new Problem(
                    "crosswalk.framework_mismatch",
                    $"{file.FullName} is a crosswalk from {c.FromUrn} to {c.ToUrn}, but programme '{programme.Id}' is {programme.Framework.Value}{(request.To is null ? "" : $" and you asked for {request.To}")}.")));
        }

        var folder = Path.Combine(request.Config.DirectoryName ?? ".", "mappings", "crosswalks");
        var own = new List<Result<CrosswalkDefinition>>();
        foreach (var path in Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.y*ml").Order(StringComparer.Ordinal) : Enumerable.Empty<string>())
        {
            own.Add(await ReadFileAsync(path, cancellationToken));
        }

        var bundled = new List<Result<CrosswalkDefinition>>();
        foreach (var name in typeof(MappingCrosswalkCommand).Assembly.GetManifestResourceNames().Where(n => n.StartsWith(BundledPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            await using var stream = typeof(MappingCrosswalkCommand).Assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            bundled.Add(CrosswalkLoader.Parse(await reader.ReadToEndAsync(cancellationToken), $"hound's {name[BundledPrefix.Length..]}"));
        }

        return Result.Combine(own).Bind(ours => Result.Combine(bundled).Bind(carried =>
        {
            var fitting = ours.Where(c => Fits(c, programme, request.To)).ToList() is { Count: > 0 } list ? list : [.. carried.Where(c => Fits(c, programme, request.To))];
            return fitting switch
            {
                [var one] => Result.Ok(one),
                [] => Result.Fail<CrosswalkDefinition>(new Problem(
                    "crosswalk.not_found",
                    $"No crosswalk from {programme.Framework.Value}{(request.To is null ? "" : $" to {request.To}")} in {folder} or among hound's. Write one there, or name it with --file.")),
                _ => Result.Fail<CrosswalkDefinition>(new Problem(
                    "crosswalk.several",
                    $"Several crosswalks from {programme.Framework.Value}: to {string.Join(", ", fitting.Select(c => c.ToUrn))}. Choose one with --to.")),
            };
        }));
    }

    private static bool Fits(CrosswalkDefinition crosswalk, ScopeProgramme programme, string? to) =>
        crosswalk.FromUrn == programme.Framework.Value && (to is null || crosswalk.ToUrn == to);

    private static async Task<Result<CrosswalkDefinition>> ReadFileAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path)
            ? CrosswalkLoader.Parse(await File.ReadAllTextAsync(path, cancellationToken), path)
            : Result.Fail<CrosswalkDefinition>(new Problem("crosswalk.file_not_found", $"Crosswalk not found: {path}"));

    private static string Markdown(Report report)
    {
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# Crosswalk: {report.Programme.Name} to {report.Crosswalk.ToUrn}")
            .AppendLine()
            .AppendLine(report.Crosswalk.Review is { } review
                ? string.Create(CultureInfo.InvariantCulture, $"Reviewed by {review.By} on {review.On:yyyy-MM-dd}. Still advisory: nothing is written to CISO Assistant.")
                : "Advisory: nobody has reviewed this crosswalk yet. Check each link before you rely on it, and nothing is written to CISO Assistant.")
            .AppendLine(CultureInfo.InvariantCulture, $"Crosswalk: {report.Crosswalk.Origin}. Results: {(report.Audit is null ? "not read (offline)" : $"today's, from audit {report.Audit}")}.")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Total: {report.Count(CrosswalkStatus.Answered)} answered, {report.Count(CrosswalkStatus.PartlyAnswered)} partly answered, {report.Count(CrosswalkStatus.Open)} open, of {report.Rows.Count} requirements.")
            .AppendLine()
            .AppendLine("| Requirement | Status | Answered by: today |")
            .AppendLine("| --- | --- | --- |");
        foreach (var row in report.Rows)
        {
            var title = row.Target.Title.Length > 0 ? $"{row.Target.RefId} {row.Target.Title}" : row.Target.RefId;
            var sources = row.Sources.Count == 0 ? "nothing" : string.Join("; ", row.Sources.Select(s => Source(s.Link, s.Answer)));
            text.AppendLine(CultureInfo.InvariantCulture, $"| {Cell(title)} | {Status(row.Status)} | {Cell(sources)} |");
        }

        return text.ToString();
    }

    private static string Source(CrosswalkLink link, SourceAnswer answer)
    {
        var checks = answer.Checks.Count switch
        {
            0 => "no checks",
            1 => "1 check",
            var n => $"{n.ToString(CultureInfo.InvariantCulture)} checks",
        };
        var manual = answer.ManualChecks.Count > 0 ? $", {answer.ManualChecks.Count.ToString(CultureInfo.InvariantCulture)} by a person" : "";
        var result = answer.Result is { } r ? $": {ResultText(r)}" : "";
        return $"{link.SourceRef} ({Relationship(link.Relationship)}, {checks}{manual}){result}";
    }

    private static string Json(Report report) =>
        new JsonObject
        {
            ["from"] = report.Crosswalk.FromUrn,
            ["to"] = report.Crosswalk.ToUrn,
            ["programme"] = report.Programme.Id,
            ["crosswalk"] = report.Crosswalk.Origin,
            ["advisory"] = true,
            ["reviewed"] = report.Crosswalk.Review is { } review
                ? new JsonObject { ["by"] = review.By, ["on"] = review.On.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
                : null,
            ["audit"] = report.Audit,
            ["answered"] = report.Count(CrosswalkStatus.Answered),
            ["partly_answered"] = report.Count(CrosswalkStatus.PartlyAnswered),
            ["open"] = report.Count(CrosswalkStatus.Open),
            ["rows"] = new JsonArray([.. report.Rows.Select(row => new JsonObject
            {
                ["ref"] = row.Target.RefId,
                ["title"] = row.Target.Title,
                ["status"] = Status(row.Status).Replace(' ', '_'),
                ["sources"] = new JsonArray([.. row.Sources.Select(s => new JsonObject
                {
                    ["ref"] = s.Link.SourceRef,
                    ["relationship"] = Relationship(s.Link.Relationship),
                    ["checks"] = new JsonArray([.. s.Answer.Checks.Select(c => (JsonNode)c)]),
                    ["manual_checks"] = new JsonArray([.. s.Answer.ManualChecks.Select(c => (JsonNode)c)]),
                    ["result"] = s.Answer.Result is { } r ? ResultText(r).Replace(' ', '_') : null,
                })]),
            })]),
        }.ToJsonString(CommandJson.Readable) + Environment.NewLine;

    private static string Status(CrosswalkStatus status) => status switch
    {
        CrosswalkStatus.Answered => "answered",
        CrosswalkStatus.PartlyAnswered => "partly answered",
        _ => "open",
    };

    private static string Relationship(CrosswalkRelationship relationship) => relationship.ToString().ToLowerInvariant();

    private static string ResultText(ComplianceResult result) => result switch
    {
        ComplianceResult.Compliant => "compliant",
        ComplianceResult.PartiallyCompliant => "partially compliant",
        ComplianceResult.NonCompliant => "non compliant",
        ComplianceResult.NotApplicable => "not applicable",
        _ => "not assessed",
    };

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);

    private sealed record Request(string From, string? To, FileInfo? File, DirectoryInfo Scope, FileInfo Config, bool Offline, bool Json);

    private sealed record Report(CrosswalkDefinition Crosswalk, ScopeProgramme Programme, string? Audit, IReadOnlyList<CrosswalkRow> Rows)
    {
        public int Count(CrosswalkStatus status) => Rows.Count(r => r.Status == status);
    }
}
