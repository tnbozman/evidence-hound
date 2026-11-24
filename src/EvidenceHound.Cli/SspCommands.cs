using System.CommandLine;
using System.Globalization;
using System.Text;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
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
    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.ssp");
        var programme = new Option<string>("--programme") { Description = "The programme's id from scope/setup.yaml, such as essential-eight.", Required = true };
        var scope = new Option<DirectoryInfo>("--scope") { Description = "The scope folder. Default: scope/ in the current folder." };
        var config = new Option<FileInfo?>("--config") { Description = "The deployment config, for the CISO Assistant connection when HOUND_CA_URL and HOUND_CA_TOKEN are not set." };
        var json = new Option<bool>("--json") { Description = "Answer in JSON, each value with its source." };
        var strict = new Option<bool>("--strict") { Description = "Exit non-zero while any gap remains: a requirement with no statement, no owner, or not applicable without a justification." };
        var build = new Command("build", "Assemble a programme's system security plan from CISO Assistant and the repo, gaps first. Reads only; writes nothing.")
        {
            programme, scope, config, json, strict,
        };
        build.SetAction(async (parseResult, cancellationToken) =>
        {
            var (exit, document) = await DocumentAsync(parseResult, programme, scope, config, configuration, registry, loggers, logger, cancellationToken);
            if (document is null)
            {
                return exit;
            }

            await output.WriteAsync(parseResult.GetValue(json) ? SspJson.Write(document) : Markdown(document));
            return parseResult.GetValue(strict) && document.Gaps.Count > 0 ? ExitCodes.SspGaps : ExitCodes.Ok;
        });
        var poam = new Command("poam", "The plan of action and milestones for a programme: every requirement not yet met, with its control, owner, ETA and status. Reads only.")
        {
            programme, scope, config, json,
        };
        poam.SetAction(async (parseResult, cancellationToken) =>
        {
            var (exit, document) = await DocumentAsync(parseResult, programme, scope, config, configuration, registry, loggers, logger, cancellationToken);
            if (document is null)
            {
                return exit;
            }

            var items = Poam.Of(document);
            await output.WriteAsync(parseResult.GetValue(json) ? SspJson.Write(items) : Poam.Markdown(items));
            return ExitCodes.Ok;
        });
        return new Command("ssp", "System security plans: one per programme, assembled from CISO Assistant and the repo.") { build, poam };
    }

    /// <summary>Checks the scope, then reads CISO Assistant once and builds the plan; on a problem, the exit code and no plan.</summary>
    private static async Task<(int Exit, SspDocument? Document)> DocumentAsync(
        ParseResult parseResult, Option<string> programme, Option<DirectoryInfo> scope, Option<FileInfo?> config, IConfiguration configuration, CollectorRegistry registry,
        ILoggerFactory loggers, ILogger logger, CancellationToken cancellationToken)
    {
        var dir = parseResult.GetValue(scope) ?? new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "scope"));
        var configFile = parseResult.GetValue(config);
        var checkedScope = await ScopeCommands.ValidateAsync(dir, configFile, registry, AuditSeries.Today(configuration), cancellationToken);
        if (checkedScope.Problems.Count > 0)
        {
            return (Log.Problems(logger, checkedScope.Problems.Select(p => p.ToProblem())), null);
        }

        var id = parseResult.GetRequiredValue(programme);
        if (checkedScope.Scope.Ssps.FirstOrDefault(s => s.Programme.Value == id) is not { } ssp)
        {
            return (Log.Problems(logger, [new Problem("ssp.not_declared", $"no plan is declared for programme '{id}'; declare it in scope/ssp.yaml.")]), null);
        }

        var connection = await PublishCommand.ConnectionAsync(configuration, configFile, registry, cancellationToken);
        return await connection.Match(
            c => ReadAsync(ssp, checkedScope, c, loggers, logger, cancellationToken),
            problems => Task.FromResult<(int, SspDocument?)>((Log.Problems(logger, problems), null)));
    }

    private static async Task<(int Exit, SspDocument? Document)> ReadAsync(
        ScopeSsp ssp, ScopeCommands.CheckedScope checkedScope, CisoAssistantOptions connection, ILoggerFactory loggers, ILogger logger, CancellationToken cancellationToken)
    {
        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(connection).BuildServiceProvider();
        var audit = checkedScope.Scope.Setup.Programmes.Single(p => p.Id == ssp.Programme.Value).Audit.Value;
        var query = ScopeStateQuery.For(checkedScope.Scope, ScopeDesired.From(checkedScope.Scope, checkedScope.Inputs)) with
        {
            ThirdParties = true,
            AllRowsOf = new HashSet<string>(StringComparer.Ordinal) { audit },
        };
        var held = await services.GetRequiredService<IScopeStateSource>().ReadAsync(query, cancellationToken);
        return held.Match<(int, SspDocument?)>(
            objects => (ExitCodes.Ok, SspBuilder.Build(ssp, checkedScope.Scope, objects)),
            problems => (Log.Problems(logger, problems), null));
    }

    /// <summary>The gaps first, then a short summary for a person; the profile's full layout comes with s11-04.</summary>
    private static string Markdown(SspDocument document)
    {
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"## System security plan: {document.SystemName.Value} (version {document.Version.Value})")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Programme {document.Programme}, profile {document.Profile}, boundary {document.Perimeter.Value}, audit of record {document.Audit.Value}.")
            .AppendLine()
            .AppendLine("### Gaps")
            .AppendLine();
        if (document.Gaps.Count == 0)
        {
            text.AppendLine("No gaps.");
        }

        foreach (var gap in document.Gaps)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {gap.Requirement}: {gap.Reason}");
        }

        text.AppendLine()
            .AppendLine("### Inventory")
            .AppendLine()
            .AppendLine("| System | Type | Owner |")
            .AppendLine("| --- | --- | --- |");
        foreach (var system in document.Inventory)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {system.Name.Value} | {system.Type.Value ?? "-"} | {system.Owner.Value ?? "-"} |");
        }

        return text
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"{document.ExternalServices.Count} external service(s), {document.Controls.Count} control(s), {document.Requirements.Count} requirement row(s). Use --json for every value with its source.")
            .ToString();
    }
}
