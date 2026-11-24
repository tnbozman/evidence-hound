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
/// <c>hound ssp build</c>: the system security plan for a programme, assembled from one read of CISO Assistant and the repo (s11-02). It
/// writes nothing to CISO Assistant.
/// </summary>
internal static class SspCommands
{
    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.ssp");
        var programme = new Option<string>("--programme") { Description = "The programme's id from scope/setup.yaml, such as essential-eight.", Required = true };
        var scope = new Option<DirectoryInfo>("--scope") { Description = "The scope folder. Default: scope/ in the current folder." };
        var config = new Option<FileInfo?>("--config") { Description = "The deployment config, for the CISO Assistant connection when HOUND_CA_URL and HOUND_CA_TOKEN are not set." };
        var json = new Option<bool>("--json") { Description = "Print the plan's model as JSON, each value with its source." };
        var build = new Command("build", "Assemble a programme's system security plan from CISO Assistant and the repo. Reads only; writes nothing.")
        {
            programme, scope, config, json,
        };
        build.SetAction(async (parseResult, cancellationToken) =>
        {
            var dir = parseResult.GetValue(scope) ?? new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "scope"));
            var configFile = parseResult.GetValue(config);
            var checkedScope = await ScopeCommands.ValidateAsync(dir, configFile, registry, AuditSeries.Today(configuration), cancellationToken);
            if (checkedScope.Problems.Count > 0)
            {
                return Log.Problems(logger, checkedScope.Problems.Select(p => p.ToProblem()));
            }

            var id = parseResult.GetRequiredValue(programme);
            if (checkedScope.Scope.Ssps.FirstOrDefault(s => s.Programme.Value == id) is not { } ssp)
            {
                return Log.Problems(logger, [new Problem("ssp.not_declared", $"no plan is declared for programme '{id}'; declare it in scope/ssp.yaml.")]);
            }

            var connection = await PublishCommand.ConnectionAsync(configuration, configFile, registry, cancellationToken);
            return await connection.Match(
                c => BuildAsync(ssp, checkedScope, c, parseResult.GetValue(json), loggers, output, logger, cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return new Command("ssp", "System security plans: one per programme, assembled from CISO Assistant and the repo.") { build };
    }

    private static async Task<int> BuildAsync(
        ScopeSsp ssp, ScopeCommands.CheckedScope checkedScope, CisoAssistantOptions connection, bool json, ILoggerFactory loggers, TextWriter output, ILogger logger,
        CancellationToken cancellationToken)
    {
        using var services = new ServiceCollection().AddSingleton(loggers).AddCisoAssistantClient(connection).BuildServiceProvider();
        var audit = checkedScope.Scope.Setup.Programmes.Single(p => p.Id == ssp.Programme.Value).Audit.Value;
        var query = ScopeStateQuery.For(checkedScope.Scope, ScopeDesired.From(checkedScope.Scope, checkedScope.Inputs)) with
        {
            ThirdParties = true,
            AllRowsOf = new HashSet<string>(StringComparer.Ordinal) { audit },
        };
        var held = await services.GetRequiredService<IScopeStateSource>().ReadAsync(query, cancellationToken);
        return await held.Match(
            async objects =>
            {
                var document = SspBuilder.Build(ssp, checkedScope.Scope, objects);
                await output.WriteAsync(json ? SspJson.Write(document) : Markdown(document));
                return ExitCodes.Ok;
            },
            problems => Task.FromResult(Log.Problems(logger, problems)));
    }

    /// <summary>A short summary for a person; the profile's full layout comes with s11-04.</summary>
    private static string Markdown(SspDocument document)
    {
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"## System security plan: {document.SystemName.Value} (version {document.Version.Value})")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Programme {document.Programme}, profile {document.Profile}, boundary {document.Perimeter.Value}, audit of record {document.Audit.Value}.")
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
