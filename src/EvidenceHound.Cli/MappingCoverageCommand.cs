using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Yaml;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>s12-01: <c>hound mapping coverage</c>: how each requirement of a framework is answered, per parent node and selected group.</summary>
internal static class MappingCoverageCommand
{
    public static Command Build(TextWriter output, ILogger logger)
    {
        var mapping = new Argument<FileInfo>("mapping") { Description = "Path to a framework mapping file (YAML)." };
        var framework = new Option<FileInfo>("--framework") { Description = "The framework snapshot the mapping is for.", Required = true };
        var groups = new Option<string?>("--groups") { Description = "The audit's selected implementation groups, comma separated (for example 1,2). Default: every group." };
        var manual = new Option<FileInfo[]>("--manual")
        {
            Description = "A hound config or manual source template, or a folder of them: checks of 'type: manual' collectors count as manual. Repeatable.",
            AllowMultipleArgumentsPerToken = false,
        };
        var json = new Option<bool>("--json") { Description = "Print JSON instead of Markdown." };
        var command = new Command(
            "coverage",
            "Count requirements answered by collectors (automated), by people (manual) and by nothing (uncovered), per parent node and group.")
        {
            mapping, framework, groups, manual, json,
        };
        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetRequiredValue(mapping),
            parseResult.GetRequiredValue(framework),
            parseResult.GetValue(groups),
            parseResult.GetValue(manual) ?? [],
            parseResult.GetValue(json),
            output,
            logger,
            cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(
        FileInfo mappingFile, FileInfo frameworkFile, string? groupList, FileInfo[] manualPaths, bool json, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        var loaded = await MappingCommands.LoadAsync(mappingFile, cancellationToken);
        var snapshot = await FrameworkCommands.LoadSnapshotAsync(frameworkFile, cancellationToken);
        var manualChecks = await ReadManualChecksAsync(manualPaths, cancellationToken);
        var counted = loaded.Bind(l => snapshot.Bind(s => manualChecks.Bind(checks => Count(l, s, groupList, checks))));
        return await counted.Match(
            async report =>
            {
                await output.WriteAsync(json ? Json(report) : Markdown(report));
                return ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    private static Result<CoverageReport> Count(LoadedMapping loaded, FrameworkSnapshot snapshot, string? groupList, IReadOnlySet<CheckId> manualChecks)
    {
        var problems = MappingValidator.Validate(loaded, snapshot);
        if (problems.Count > 0)
        {
            return Result.Fail<CoverageReport>(problems);
        }

        var known = snapshot.Groups.Select(g => g.RefId).ToList();
        var groups = groupList is null ? known : [.. groupList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        var unknown = groups.Where(g => !known.Contains(g)).ToList();
        if (unknown.Count > 0)
        {
            return Result.Fail<CoverageReport>(new Problem(
                "mapping.group_unknown",
                $"Group {string.Join(", ", unknown)} is not in {snapshot.FrameworkUrn}; its groups are {string.Join(", ", known)}."));
        }

        var manualOnly = loaded.ManualOnly.Select(m => m.Key).ToList();
        var top = snapshot.Parents.Any(p => p.ParentUrn is not null)
            ? MappingCoverage.CountByTop(snapshot, loaded.Mapping, manualOnly, manualChecks, groups)
            : [];
        return Result.Ok(new CoverageReport(snapshot, groups, MappingCoverage.Count(snapshot, loaded.Mapping, manualOnly, manualChecks, groups), top));
    }

    private static async Task<Result<IReadOnlySet<CheckId>>> ReadManualChecksAsync(FileInfo[] paths, CancellationToken cancellationToken)
    {
        var files = paths.SelectMany<FileInfo, string>(path => Directory.Exists(path.FullName)
            ? Directory.EnumerateFiles(path.FullName, "*.y*ml", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            : [path.FullName]);
        var results = new List<Result<IReadOnlyList<CheckId>>>();
        foreach (var file in files)
        {
            results.Add(File.Exists(file)
                ? ManualSourceChecks.Read(await File.ReadAllTextAsync(file, cancellationToken), file)
                : Result.Fail<IReadOnlyList<CheckId>>(new Problem("manual.file_not_found", $"Manual source not found: {file}")));
        }

        return Result.Combine(results).Map(lists => (IReadOnlySet<CheckId>)lists.SelectMany(l => l).ToHashSet());
    }

    private static string Markdown(CoverageReport report)
    {
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Coverage of {report.Snapshot.FrameworkUrn} for groups {string.Join(", ", report.Groups)}")
            .AppendLine()
            .AppendLine("| Parent | Group | Automated | Manual | Uncovered |")
            .AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var row in report.Rows)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {Short(row.ParentUrn)} | {row.Group} | {row.Automated} | {row.Manual} | {row.Uncovered} |");
        }

        if (report.Top.Count > 0)
        {
            text.AppendLine()
                .AppendLine("Per top-level node (for the ASD Essentials, per domain):")
                .AppendLine()
                .AppendLine("| Top level | Group | Automated | Manual | Uncovered |")
                .AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var row in report.Top)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| {report.Snapshot.ParentName(row.ParentUrn)} | {row.Group} | {row.Automated} | {row.Manual} | {row.Uncovered} |");
            }
        }

        return text
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Total: {report.Automated} automated, {report.Manual} manual, {report.Uncovered} uncovered (one per requirement row and group).")
            .ToString();
    }

    private static string Json(CoverageReport report) =>
        new JsonObject
        {
            ["framework"] = report.Snapshot.FrameworkUrn,
            ["groups"] = new JsonArray([.. report.Groups.Select(g => (JsonNode)g)]),
            ["automated"] = report.Automated,
            ["manual"] = report.Manual,
            ["uncovered"] = report.Uncovered,
            ["rows"] = new JsonArray([.. report.Rows.Select(r => new JsonObject
            {
                ["parent"] = r.ParentUrn,
                ["group"] = r.Group,
                ["automated"] = r.Automated,
                ["manual"] = r.Manual,
                ["uncovered"] = r.Uncovered,
            })]),
            ["top"] = new JsonArray([.. report.Top.Select(r => new JsonObject
            {
                ["node"] = r.ParentUrn,
                ["name"] = report.Snapshot.ParentName(r.ParentUrn),
                ["group"] = r.Group,
                ["automated"] = r.Automated,
                ["manual"] = r.Manual,
                ["uncovered"] = r.Uncovered,
            })]),
        }.ToJsonString(OnboardAgentCommand.Indented) + Environment.NewLine;

    /// <summary>The last part of a parent node's URN, such as <c>mfa</c> for the Essential Eight's multi-factor authentication.</summary>
    private static string Short(string urn) => urn[(urn.LastIndexOf(':') + 1)..];

    private sealed record CoverageReport(FrameworkSnapshot Snapshot, IReadOnlyList<string> Groups, IReadOnlyList<CoverageCount> Rows, IReadOnlyList<CoverageCount> Top)
    {
        public int Automated => Rows.Sum(r => r.Automated);

        public int Manual => Rows.Sum(r => r.Manual);

        public int Uncovered => Rows.Sum(r => r.Uncovered);
    }
}
