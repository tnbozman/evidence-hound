namespace EvidenceHound.Cli;

/// <summary>
/// Explains a denied file or folder in words a person can act on. The usual cause is the default evidence root
/// (<c>/var/lib/evidence-hound</c>), which only the container's user or root can write.
/// </summary>
internal static class AccessDenied
{
    public static string? Explain(Exception exception) =>
        exception is UnauthorizedAccessException
            ? $"{exception.Message} If this is the evidence folder, set 'evidence.root' in hound.yaml to a folder you can write "
              + "(for a trial, a folder in your home), or create it for your user: sudo mkdir -p <folder> && sudo chown $USER <folder>."
            : null;
}
