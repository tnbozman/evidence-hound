using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Cli;

/// <summary>Reads evidence files from one folder, by file name. The publisher checks each file's SHA-256.</summary>
internal sealed class EvidenceDirectory(DirectoryInfo root) : IEvidenceFiles
{
    public async Task<Result<ReadOnlyMemory<byte>>> ReadAsync(Evidence evidence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (Path.GetFileName(evidence.FileName) != evidence.FileName)
        {
            return Result.Fail<ReadOnlyMemory<byte>>(new Problem(
                "evidence.file_name_invalid", $"Evidence {evidence.Id} file name '{evidence.FileName}' must be a plain file name, not a path."));
        }

        var path = Path.Combine(root.FullName, evidence.FileName);
        return File.Exists(path)
            ? Result.Ok<ReadOnlyMemory<byte>>(await File.ReadAllBytesAsync(path, cancellationToken))
            : Result.Fail<ReadOnlyMemory<byte>>(new Problem("evidence.file_not_found", $"Evidence {evidence.Id} file not found: {path}"));
    }
}
