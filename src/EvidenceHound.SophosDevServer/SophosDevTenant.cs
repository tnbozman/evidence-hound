using EvidenceHound.Core.Checks;

namespace EvidenceHound.SophosDevServer;

public enum DevVariant
{
    Compliant,
    Gaps,
}

public sealed record Deviation(string Name, string Description, IReadOnlyDictionary<string, CheckOutcome> Fails, Func<SophosDevTenant, SophosDevTenant> Apply);

public sealed record SophosDevTenant(string TenantId)
{
    public static readonly string AccessToken = "x";

    public static readonly string ClientSecret = "x";

    public static SophosDevTenant Compliant => throw new NotImplementedException();

    public static IReadOnlyList<Deviation> Deviations => throw new NotImplementedException();

    public static SophosDevTenant For(DevVariant variant) => throw new NotImplementedException();

    public static Dictionary<string, string> Settings(Uri url) => throw new NotImplementedException();

    public SophosDevTenant With(Deviation deviation) => throw new NotImplementedException();
}
