using System.CommandLine;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound irp build</c>: the incident response plan, built from the repo (s15-09): contacts, systems and their data, suppliers and
/// customers, and the reports an incident owes. The person writes only the judgement sections. Reads no CISO Assistant and writes nothing.
/// </summary>
internal static class IrpCommands
{
    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.irp");
        var programme = new Option<string>("--programme") { Description = "The programme's id from scope/setup.yaml that scope/irp.yaml names, such as essential-eight.", Required = true };
        var scope = new Option<DirectoryInfo>("--scope") { Description = "The scope folder. Default: scope/ in the current folder." };
        var json = CommonOptions.Json("Answer in JSON, each value with its source.");
        var strict = new Option<bool>("--strict") { Description = "Exit non-zero while any gap remains: a role or judgement section not written." };
        var build = new Command("build", "Build the incident response plan from the repo, gaps first: NIST SP 800-61 Rev 3 with the Australian reporting, covering ISM-0043. Writes nothing.")
        {
            programme, scope, json, strict,
        };
        build.SetAction(async (parseResult, cancellationToken) =>
        {
            var dir = parseResult.GetValue(scope) ?? new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "scope"));
            var checkedScope = await RepoScope.ValidateAsync(dir, null, registry, AuditSeries.Today(configuration), cancellationToken);
            if (checkedScope.Problems.Count > 0)
            {
                return Log.Problems(logger, checkedScope.Problems.Select(p => p.ToProblem()));
            }

            var (irp, _) = await RepoScope.IrpAsync(dir, checkedScope.Scope, cancellationToken);
            var wanted = parseResult.GetRequiredValue(programme);
            if (irp is null || irp.Programme.Value != wanted)
            {
                return Log.Problems(logger, [new Problem("irp.programme_unknown", irp is null
                    ? "the scope has no irp.yaml. Write one naming the programme, the roles and the judgement sections (docs/incidents.md)."
                    : $"scope/irp.yaml is the plan for '{irp.Programme.Value}', not '{wanted}'.")]);
            }

            var repo = dir.Parent?.FullName ?? dir.FullName;
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var section in irp.Sections)
            {
                var path = Path.Combine(repo, section.File);
                texts[section.File] = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : "";
            }

            var (reporting, _) = await RepoScope.ReportingAsync(dir, cancellationToken);
            var plan = IrpBuilder.Build(irp, checkedScope.Scope, reporting, texts);
            await output.WriteAsync(parseResult.GetValue(json) ? IrpJson.Write(plan) : IrpLayout.Markdown(plan, texts));
            return parseResult.GetValue(strict) && plan.Gaps.Count > 0 ? ExitCodes.SspGaps : ExitCodes.Ok;
        });
        return new Command("irp", "The incident response plan: built from the repo, with the parts a person judges written in Markdown.") { build };
    }
}
