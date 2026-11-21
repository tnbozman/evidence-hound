namespace EvidenceHound.Collectors.M365;

/// <summary>Office hardening checks over Intune policies: macros from the internet, and the Office attack surface reduction rules.</summary>
internal static class OfficeChecks
{
    public static Finding MacrosBlockedFromInternet(TenantData tenant, M365Options options) => throw new NotImplementedException();

    public static Finding AsrOfficeRulesEnabled(TenantData tenant, M365Options options) => throw new NotImplementedException();
}
