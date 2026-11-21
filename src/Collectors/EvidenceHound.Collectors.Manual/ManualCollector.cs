using System.Text;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Manual;

/// <summary>
/// The built-in <c>manual</c> collector, for systems without an API. Each run reads the drop folder, emits the files within the
/// due interval as evidence, and reports each configured check from the newest file's attestation.
/// </summary>
public sealed class ManualCollector : ICollector
{
    public const string TypeName = "manual";

    public string Type => TypeName;

    public string Version => "1.0.0";

    public CollectorDescription Describe() => new(
        [],
        [
            new(ManualSettings.DropFolder, "Folder the owner drops export files in. A relative path is resolved against the config file's folder.", Required: true, IsPath: true),
            new(ManualSettings.Patterns, "Comma-separated file patterns to accept, for example *.txt,*.csv. Default: every file.", Required: false),
            new(ManualSettings.Checks, "Comma-separated check IDs this source supports, for example network.firewall.rules_reviewed.", Required: true),
            new(ManualSettings.DueDays, "Days after which fresh evidence is due, for example 30.", Required: true),
            new(ManualSettings.Owner, "Who owns the evidence and attests it.", Required: true),
        ],
        [],
        ChecksSetting: ManualSettings.Checks);

    public async Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var now = context.Clock.UtcNow;
        return await ManualSource.FromSettings(context.Settings)
            .BindAsync(async source => (await ReadAsync(source, cancellationToken))
                .Bind(files => ManualAssessment.Assess(source, files, now, context.Name)));
    }

    private static async Task<Result<IReadOnlyList<DroppedFile>>> ReadAsync(ManualSource source, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source.DropFolder))
        {
            return Result.Fail<IReadOnlyList<DroppedFile>>(new Problem(
                "manual.drop_folder_missing", $"Drop folder '{source.DropFolder}' does not exist. Create it, or check the '{ManualSettings.DropFolder}' setting."));
        }

        var files = new List<Result<DroppedFile>>();
        foreach (var path in Directory.EnumerateFiles(source.DropFolder).Where(p => source.Accepts(Path.GetFileName(p))).Order(StringComparer.Ordinal))
        {
            files.Add(await ReadFileAsync(path, cancellationToken));
        }

        return Result.Combine(files);
    }

    private static async Task<Result<DroppedFile>> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(path);
        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        var sidecar = path + Attestation.Suffix;
        if (!File.Exists(sidecar))
        {
            return Result.Ok(new DroppedFile(name, content, modified, null, ReadOnlyMemory<byte>.Empty));
        }

        var attestationContent = await File.ReadAllBytesAsync(sidecar, cancellationToken);
        // Editors such as Notepad start UTF-8 files with a byte-order mark; it is not part of the first key.
        var yaml = Encoding.UTF8.GetString(attestationContent).TrimStart('﻿');
        return Attestation.Parse(yaml, Attestation.SidecarFor(name))
            .Map(attestation => new DroppedFile(name, content, modified, attestation, attestationContent));
    }
}
