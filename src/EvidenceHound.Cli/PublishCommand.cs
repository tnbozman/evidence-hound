using System.CommandLine;
using System.Text.Json.Nodes;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound publish</c>: plan a run's updates and send them to CISO Assistant, then print the report as JSON.</summary>
internal static class PublishCommand
{
    public const string TransportSetting = "HOUND_TRANSPORT";


    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.publish");
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the report of what would be sent, without calling CISO Assistant." };
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var checks = new Option<FileInfo>("--checks") { Description = "Path to a check results file (JSON).", Required = true };
        var evidenceDir = new Option<DirectoryInfo>("--evidence-dir") { Description = "Folder holding the evidence files the check results cite.", Required = true };

        var publish = new Command("publish", $"Publish a run's check results to CISO Assistant (needs {CisoAssistantConnection.UrlSetting} and {CisoAssistantConnection.TokenSetting}).") { dryRun, config, checks, evidenceDir };
        publish.SetAction(async (parseResult, cancellationToken) =>
        {
            // Each result carries the run marker hound run writes, so scope apply keeps it and scope export leaves it out.
            var plan = await PlanCommand.PlanAsync(
                parseResult.GetRequiredValue(config), parseResult.GetRequiredValue(checks), registry, AuditSeries.Today(configuration), cancellationToken, RunIds.From(DateTimeOffset.UtcNow));
            return await plan.Match(
                updates => parseResult.GetValue(dryRun)
                    ? WriteAsync(Publisher.DryRun(updates), output)
                    : SendAsync(updates, parseResult.GetRequiredValue(evidenceDir), configuration, loggers, output, logger, cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return publish;
    }

    private static Task<int> SendAsync(
        IReadOnlyList<AuditUpdate> updates,
        DirectoryInfo evidenceDir,
        IConfiguration configuration,
        ILoggerFactory loggers,
        TextWriter output,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var files = new EvidenceDirectory(evidenceDir);
        return (configuration[TransportSetting] ?? "rest") switch
        {
            "rest" => CisoAssistantConnection.FromSettings(configuration).Match(
                options => SendRestAsync(options, files, updates, loggers, output, logger, cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems))),
            "kafka" => Task.FromResult(Log.Problems(logger, [new Problem("config.transport_removed", $"{TransportSetting}=kafka: the Kafka transport was removed in release 3; hound publishes over the REST API. Unset {TransportSetting}.")])),
            var other => Task.FromResult(Log.Problems(logger, [new Problem("config.transport_unknown", $"Unknown {TransportSetting} '{other}'; hound publishes over the REST API, so leave it unset or set it to rest.")])),
        };
    }

    private static async Task<int> SendRestAsync(
        CisoAssistantOptions options, IEvidenceFiles files, IReadOnlyList<AuditUpdate> updates, ILoggerFactory loggers, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        using var services = new ServiceCollection()
            .AddSingleton(loggers)
            .AddCisoAssistantClient(options)
            .BuildServiceProvider();
        return await SendAsync(new RestSink(services.GetRequiredService<ICisoAssistantClient>(), files), updates, output, logger, cancellationToken);
    }

    private static async Task<int> SendAsync(RestSink sink, IReadOnlyList<AuditUpdate> updates, TextWriter output, ILogger logger, CancellationToken cancellationToken)
    {
        var report = await sink.SendAsync(updates, cancellationToken);
        await WriteAsync(report, output);
        Log.Published(logger, report.Sent, report.Skipped, report.Failed);
        Log.Problems(logger, report.Items
            .Where(item => item.Outcome == PublishOutcome.Failed)
            .SelectMany(item => item.Problems.Select(p => new Problem(p.Code, $"{item.Description}: {p.Message}"))));

        return report.HasFailures ? ExitCodes.PublishFailed : ExitCodes.Ok;
    }

    /// <summary>Each audit's requirement items counted, for the publish and run reports.</summary>
    internal static JsonArray AuditsJson(PublishReport report) =>
        new([.. report.Audits.Select(a => new JsonObject
        {
            ["compliance_assessment"] = a.ComplianceAssessmentRefId,
            ["planned"] = a.Planned,
            ["sent"] = a.Sent,
            ["skipped"] = a.Skipped,
            ["failed"] = a.Failed,
        })]);

    private static async Task<int> WriteAsync(PublishReport report, TextWriter output)
    {
        var json = new JsonObject
        {
            ["planned"] = report.Planned,
            ["sent"] = report.Sent,
            ["skipped"] = report.Skipped,
            ["failed"] = report.Failed,
            ["audits"] = AuditsJson(report),
            ["items"] = new JsonArray([.. report.Items.Select(item => new JsonObject
            {
                ["update"] = item.Description,
                ["outcome"] = item.Outcome.ToString().ToLowerInvariant(),
                ["note"] = item.Note,
                ["problems"] = new JsonArray([.. item.Problems.Select(p => new JsonObject { ["code"] = p.Code, ["message"] = p.Message })]),
            })]),
        };
        await output.WriteLineAsync(json.ToJsonString(CommandJson.Escaped));
        return ExitCodes.Ok;
    }
}
