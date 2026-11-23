using EvidenceHound.Core.Checks;

namespace EvidenceHound.M365DevServer;

public enum DevVariant
{
    Compliant,
    Gaps,
}

/// <summary>A named way the <c>gaps</c> tenant differs from the compliant one, and the checks it is expected to fail.</summary>
public sealed record Deviation(string Name, string Description, IReadOnlyDictionary<string, CheckOutcome> Fails, Func<DevTenant, DevTenant> Apply);

public sealed record DevTenant
{
    public static DevTenant Compliant => throw new NotImplementedException();

    public static IReadOnlyList<Deviation> Deviations => throw new NotImplementedException();

    public static DevTenant For(DevVariant variant) => throw new NotImplementedException();

    public static Dictionary<string, string> Settings() => throw new NotImplementedException();

    public DevTenant With(Deviation deviation) => throw new NotImplementedException();
}
