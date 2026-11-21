using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Everything CISO Assistant holds under a root folder, for <c>hound scope export</c>: the objects as the plan reads them, with every row of every
/// audit, and a snapshot of each framework those audits use.
/// </summary>
public sealed record ScopeExportState(string RootFolder, IReadOnlyList<ActualObject> Objects, IReadOnlyList<FrameworkSnapshot> Frameworks);

/// <summary>Reads what <c>hound scope export</c> writes out. Read-only.</summary>
public interface IScopeExportSource
{
    Task<Result<ScopeExportState>> ReadAllAsync(string rootFolder, CancellationToken cancellationToken);

    /// <summary>The latest file of the document with this id (its evidence's <c>ref_id</c>); none when it has none.</summary>
    Task<Result<IReadOnlyList<ExportedFile>>> DownloadAsync(string documentId, CancellationToken cancellationToken);
}

/// <summary>A document's file as CISO Assistant serves it.</summary>
public sealed record ExportedFile(string FileName, byte[] Content);

/// <summary>The <c>ref_id</c> given to an audit, control or document that has none, made from its name. Pure.</summary>
public static class ScopeRefIds
{
    /// <summary>
    /// The name's letters and digits, each run of anything else as one dash: upper case for an audit or a control (<c>EXAMPLE-AUDIT</c>), lower
    /// case for a document, whose <c>ref_id</c> is its id in the scope files.
    /// </summary>
    public static string For(ScopeKind kind, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var slug = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(kind == ScopeKind.Document ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().TrimEnd('-');
    }
}
