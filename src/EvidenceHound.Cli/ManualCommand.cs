using System.CommandLine;
using System.Security.Cryptography;
using EvidenceHound.Collectors;
using EvidenceHound.Collectors.Manual;
using EvidenceHound.Collectors.VulnScan;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound manual upload</c>: copy an export file into a manual source's drop folder, and optionally attest it.</summary>
internal static class ManualCommand
{
    public static Command Build(Terminal terminal, ILoggerFactory loggers, CollectorRegistry registry, IClock clock)
    {
        var logger = loggers.CreateLogger("hound.manual");
        var config = new Option<FileInfo>("--config") { Description = "Path to the deployment config file (YAML).", Required = true };
        var source = new Option<string>("--source") { Description = "The manual source: its collector name in the config.", Required = true };
        var file = new Argument<FileInfo>("file") { Description = "The export file to upload." };

        var upload = new Command("upload", "Copy an export file into a manual source's drop folder, then, for a person at a terminal, ask whether to attest it. Through a pipe or an agent it is copied unattested.") { config, source, file };
        upload.SetAction(async (parseResult, cancellationToken) =>
        {
            var found = (await ConfigCommands.LoadAsync(parseResult.GetRequiredValue(config), registry, cancellationToken))
                .Bind(deployment => Find(deployment, parseResult.GetRequiredValue(source)));
            return await found.Match(
                manual => UploadAsync(terminal, logger, clock, manual, parseResult.GetRequiredValue(file), cancellationToken),
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });

        return new Command("manual", "Work with manual-upload sources: upload a file, propose a result as an agent, attest proposals as a person.")
        {
            upload,
            ManualProposalCommands.BuildPropose(terminal, logger, registry, clock),
            ManualProposalCommands.BuildApprove(terminal, logger, registry, clock),
        };
    }

    internal static Result<ManualSource> Find(DeploymentConfig deployment, string name) =>
        deployment.Collectors.FirstOrDefault(c => c.Name == name) switch
        {
            null => Result.Fail<ManualSource>(new Problem(
                "manual.source_unknown",
                $"No collector named '{name}' in the config. Manual sources: {Names(deployment.Collectors.Where(c => c.Type is ManualCollector.TypeName or VulnScanCollector.TypeName))}.")),
            { Type: VulnScanCollector.TypeName } scans => VulnScanSource.FromSettings(new CollectorSettings(scans.Settings))
                .Map(scan => new ManualSource(scan.DropFolder, [scan.Pattern], [], 1, scan.Owner)),
            { Type: not ManualCollector.TypeName } other => Result.Fail<ManualSource>(new Problem(
                "manual.source_not_manual", $"Collector '{name}' has type '{other.Type}'; it is not a manual source.")),
            var manual => ManualSource.FromSettings(new CollectorSettings(manual.Settings)),
        };

    private static async Task<int> UploadAsync(Terminal terminal, ILogger logger, IClock clock, ManualSource source, FileInfo file, CancellationToken cancellationToken)
    {
        if (!file.Exists)
        {
            return Log.Problems(logger, [new Problem("manual.file_missing", $"File '{file.FullName}' does not exist.")]);
        }

        if (!source.Accepts(file.Name))
        {
            return Log.Problems(logger, [new Problem("manual.file_not_accepted", $"'{file.Name}' does not match this source's patterns: {string.Join(", ", source.Patterns)}.")]);
        }

        var content = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
        if (AlreadyUploaded(source, content) is { } existing)
        {
            Log.AlreadyUploaded(logger, file.Name, existing);
            return ExitCodes.Ok;
        }

        var destination = Path.Combine(source.DropFolder, file.Name);
        if (File.Exists(destination))
        {
            return Log.Problems(logger, [new Problem("manual.file_exists", $"A different '{file.Name}' is already in {source.DropFolder}. Rename the new export and upload it again.")]);
        }

        // A scan source's export is the evidence itself, with no checks to attest. Only a person at a terminal attests: through a pipe
        // or from an agent the file is copied unattested, for a person to attest later.
        var answer = source.Checks.Count == 0 || !terminal.Interactive ? Result.Ok(new Answer(null)) : await AskAsync(terminal, source, clock, cancellationToken);
        if (source.Checks.Count > 0 && !terminal.Interactive)
        {
            Log.Usage(logger, $"{file.Name} is copied but not attested: only a person at a terminal attests evidence. Propose a result with 'hound manual propose', and a person attests it with 'hound manual approve' or in the weekly review.");
        }
        if (answer is Result<Answer>.Fail failed)
        {
            foreach (var problem in failed.Errors)
            {
                Log.Usage(logger, problem.Message);
            }

            return ExitCodes.Usage;
        }

        var attestation = answer.Match(a => a.Attestation, _ => null);
        Directory.CreateDirectory(source.DropFolder);
        await File.WriteAllBytesAsync(destination, content, cancellationToken);
        if (attestation is not null)
        {
            await File.WriteAllTextAsync(destination + Attestation.Suffix, attestation.ToYaml(), cancellationToken);
        }

        await terminal.Output.WriteLineAsync(destination);
        Log.Uploaded(logger, file.Name, source.DropFolder, attestation is not null);
        return ExitCodes.Ok;
    }

    /// <summary>The name of an accepted file in the drop folder with the same content, if there is one.</summary>
    private static string? AlreadyUploaded(ManualSource source, byte[] content)
    {
        if (!Directory.Exists(source.DropFolder))
        {
            return null;
        }

        var hash = SHA256.HashData(content);
        return Directory.EnumerateFiles(source.DropFolder)
            .Where(path => source.Accepts(Path.GetFileName(path)))
            .Order(StringComparer.Ordinal)
            .Where(path => SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(hash))
            .Select(Path.GetFileName)
            .FirstOrDefault();
    }

    /// <summary>Asks whether to attest the file and, if so, who attests, the outcome of each check and a note. Prompts go to stderr.</summary>
    private static async Task<Result<Answer>> AskAsync(Terminal terminal, ManualSource source, IClock clock, CancellationToken cancellationToken)
    {
        var attest = await PromptAsync(terminal, "Attest this file now? [y/N]: ", cancellationToken);
        if (attest is not ("y" or "Y" or "yes" or "Yes"))
        {
            return Result.Ok(new Answer(null));
        }

        var who = await PromptAsync(terminal, $"Attested by [{source.Owner}]: ", cancellationToken) is { Length: > 0 } name ? name : source.Owner;
        var outcomes = new Dictionary<string, CheckOutcome>(StringComparer.Ordinal);
        foreach (var check in source.Checks)
        {
            var text = await PromptAsync(terminal, $"Outcome for {check} ({string.Join(", ", Attestation.OutcomeNames)}) [pass]: ", cancellationToken) is { Length: > 0 } given ? given : "pass";
            if (!Attestation.TryParseOutcome(text, out var outcome))
            {
                return Result.Fail<Answer>(new Problem("manual.outcome_invalid", $"'{text}' is not an outcome for {check}; answer one of {string.Join(", ", Attestation.OutcomeNames)}."));
            }

            outcomes[check.Value] = outcome;
        }

        var note = await PromptAsync(terminal, "Note (optional): ", cancellationToken);
        return Result.Ok(new Answer(new Attestation(who, clock.UtcNow, outcomes, note is { Length: > 0 } ? note : null)));
    }

    private static async Task<string> PromptAsync(Terminal terminal, string question, CancellationToken cancellationToken)
    {
        await terminal.Error.WriteAsync(question.AsMemory(), cancellationToken);
        await terminal.Error.FlushAsync(cancellationToken);
        return (await terminal.Input.ReadLineAsync(cancellationToken))?.Trim() ?? "";
    }

    private static string Names(IEnumerable<CollectorInstance> collectors) =>
        collectors.Select(c => c.Name).ToArray() is [_, ..] names ? string.Join(", ", names) : "none";

    /// <summary>The answers to the attestation prompt: an attestation, or none when the uploader chose not to attest.</summary>
    private sealed record Answer(Attestation? Attestation);
}
