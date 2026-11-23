namespace EvidenceHound.SophosDevServer;

public sealed record DevServerOptions(int Port, Uri PublicUrl, string Variant, IReadOnlyList<string> Deviations, SophosDevTenant Tenant)
{
    public static object Parse(IReadOnlyList<string> args) => throw new NotImplementedException();
}
