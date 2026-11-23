namespace EvidenceHound.IacDevServer;

/// <summary>The dev server's command line.</summary>
public sealed record DevServerOptions(int Port, Uri PublicUrl, string Variant, IReadOnlyList<string> Deviations, string? ControlsReport)
{
    public static object Parse(IReadOnlyList<string> args) => throw new NotImplementedException();
}
