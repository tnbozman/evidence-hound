using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound programme list</c>: each programme's framework, groups, audit of record and requirement counts, from the snapshots only.</summary>
internal static class ProgrammeCommands
{
    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.programme");
        var scope = new Option<DirectoryInfo>("--scope") { Description = "The scope folder. Default: scope/ in the current folder." };
        var json = new Option<bool>("--json") { Description = "Answer in JSON, for an agent." };
        var list = new Command("list", "List the compliance programmes with their framework, groups, audit of record and requirement counts per group and parent node. Reads no CISO Assistant.")
        {
            scope, json,
        };
        list.SetAction(async (parseResult, cancellationToken) =>
        {
            var dir = parseResult.GetValue(scope) ?? new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "scope"));
            var checkedScope = await ScopeCommands.ValidateAsync(dir, null, registry, AuditSeries.Today(configuration), cancellationToken);
            if (checkedScope.Problems.Count > 0)
            {
                return Log.Problems(logger, checkedScope.Problems.Select(p => p.ToProblem()));
            }

            var outlines = checkedScope.Scope.Setup.Programmes
                .Select(p => ProgrammeOutline.Of(p, checkedScope.Inputs.Snapshots[p.Framework.Value]))
                .ToArray();
            await output.WriteLineAsync(parseResult.GetValue(json) ? Json(outlines) : Markdown(outlines));
            return ExitCodes.Ok;
        });
        return new Command("programme", "Compliance programmes: a framework, its groups and the audit of record, which reports are organised by.") { list };
    }

    private static string Json(IEnumerable<ProgrammeOutline> outlines) =>
        new JsonObject
        {
            ["programmes"] = new JsonArray([.. outlines.Select(o => new JsonObject
            {
                ["id"] = o.Programme.Id,
                ["name"] = o.Programme.Name,
                ["framework"] = o.Programme.Framework.Value,
                ["version"] = o.Version,
                ["audit"] = o.Programme.Audit.Value,
                ["group_by"] = o.Programme.GroupBy,
                ["groups"] = new JsonArray([.. o.Groups.Select(g => new JsonObject
                {
                    ["ref_id"] = g.RefId,
                    ["name"] = g.Name,
                    ["requirements"] = g.Requirements,
                    ["parents"] = new JsonArray([.. g.Parents.Select(p => new JsonObject { ["urn"] = p.Urn, ["name"] = p.Name, ["requirements"] = p.Requirements })]),
                })]),
            })]),
        }.ToJsonString(OnboardAgentCommand.Indented);

    private static string Markdown(ProgrammeOutline[] outlines)
    {
        if (outlines.Length == 0)
        {
            return "No programmes. Declare them under 'programmes:' in scope/setup.yaml.";
        }

        var text = new StringBuilder();
        foreach (var o in outlines)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"## {o.Programme.Id}: {o.Programme.Name}")
                .AppendLine()
                .AppendLine(CultureInfo.InvariantCulture, $"Framework {o.Programme.Framework.Value} (version {o.Version}), audit of record {o.Programme.Audit.Value}.")
                .AppendLine()
                .AppendLine(CultureInfo.InvariantCulture, $"| Parent | {string.Join(" | ", o.Groups.Select(g => $"{g.RefId} {g.Name}"))} |")
                .AppendLine(CultureInfo.InvariantCulture, $"| --- |{string.Concat(o.Groups.Select(_ => " ---: |"))}");
            foreach (var parent in o.Groups.SelectMany(g => g.Parents).Select(p => p.Name).Distinct(StringComparer.Ordinal))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| {parent} | {string.Join(" | ", o.Groups.Select(g => Count(g, parent)))} |");
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"| Total | {string.Join(" | ", o.Groups.Select(g => g.Requirements))} |").AppendLine();
        }

        return text.ToString().TrimEnd();
    }

    private static int Count(GroupOutline group, string parent) => group.Parents.FirstOrDefault(p => p.Name == parent)?.Requirements ?? 0;
}
