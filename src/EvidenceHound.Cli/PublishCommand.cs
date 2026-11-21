using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound publish</c>: plan a run's updates and send them to CISO Assistant, then print the report as JSON.</summary>
internal static class PublishCommand
{
    public const string UrlSetting = "HOUND_CA_URL";
    public const string TokenSetting = "HOUND_CA_TOKEN";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration)
    {
        var logger = loggers.CreateLogger("hound.publish");
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the report of what would be sent, without calling CISO Assistant." };
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var checks = new Option<FileInfo>("--checks") { Description = "Path to a check results file (JSON).", Required = true };
        var evidenceDir = new Option<DirectoryInfo>("--evidence-dir") { Description = "Folder holding the evidence files the check results cite.", Required = true };

        var publish = new Command("publish", $"Publish a run's check results to CISO Assistant (needs {UrlSetting} and {TokenSetting}).") { dryRun, config, checks, evidenceDir };
        publish.SetAction(async (parseResult, cancellationToken) =>
        {
            var plan = await PlanCommand.PlanAsync(parseResult.GetRequiredValue(config), parseResult.GetRequiredValue(checks), cancellationToken);
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
        CancellationToken cancellationToken) =>
        Connection(configuration).Match(
            options => SendAsync(updates, evidenceDir, options, loggers, output, logger, cancellationToken),
            problems => Task.FromResult(Log.Problems(logger, problems)));

    private static async Task<int> SendAsync(
        IReadOnlyList<AuditUpdate> updates,
        DirectoryInfo evidenceDir,
        CisoAssistantOptions options,
        ILoggerFactory loggers,
        TextWriter output,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var services = new ServiceCollection()
            .AddSingleton(loggers)
            .AddCisoAssistantClient(options)
            .BuildServiceProvider();
        var publisher = new Publisher(services.GetRequiredService<ICisoAssistantClient>(), new EvidenceDirectory(evidenceDir));

        var report = await publisher.PublishAsync(updates, cancellationToken);
        await WriteAsync(report, output);
        Log.Published(logger, report.Sent, report.Skipped, report.Failed);
        Log.Problems(logger, report.Items
            .Where(item => item.Outcome == PublishOutcome.Failed)
            .SelectMany(item => item.Problems.Select(p => new Problem(p.Code, $"{item.Description}: {p.Message}"))));

        return report.HasFailures ? ExitCodes.PublishFailed : ExitCodes.Ok;
    }

    private static Result<CisoAssistantOptions> Connection(IConfiguration configuration)
    {
        var url = configuration[UrlSetting];
        var token = configuration[TokenSetting];
        return Validation.Validate(
            () => new CisoAssistantOptions(new Uri(url!.EndsWith('/') ? url : url + "/"), token!),
            new Invariant(Uri.TryCreate(url, UriKind.Absolute, out _), new Problem("config.ca_url_missing", $"Set {UrlSetting} to the CISO Assistant API address, for example http://localhost:8000/api/.")),
            new Invariant(!string.IsNullOrWhiteSpace(token), new Problem("config.ca_token_missing", $"Set {TokenSetting} to a CISO Assistant personal access token.")));
    }

    private static async Task<int> WriteAsync(PublishReport report, TextWriter output)
    {
        var json = new JsonObject
        {
            ["planned"] = report.Planned,
            ["sent"] = report.Sent,
            ["skipped"] = report.Skipped,
            ["failed"] = report.Failed,
            ["items"] = new JsonArray([.. report.Items.Select(item => new JsonObject
            {
                ["update"] = item.Description,
                ["outcome"] = item.Outcome.ToString().ToLowerInvariant(),
                ["problems"] = new JsonArray([.. item.Problems.Select(p => new JsonObject { ["code"] = p.Code, ["message"] = p.Message })]),
            })]),
        };
        await output.WriteLineAsync(json.ToJsonString(Indented));
        return ExitCodes.Ok;
    }
}
