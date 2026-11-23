namespace EvidenceHound.M365DevServer;

public sealed record DevServerOptions(int Port, string Variant, IReadOnlyList<string> Deviations, DevTenant Tenant)
{
    public static object Parse(IReadOnlyList<string> args) => throw new NotImplementedException();
}
