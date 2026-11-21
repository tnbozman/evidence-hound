namespace EvidenceHound.Collectors.M365;

/// <summary>Privileged access checks: how many Global Administrators, whether admins are cloud-only, and whether roles are eligible rather than permanent.</summary>
internal static class PrivilegedChecks
{
    public static Finding GlobalAdminCount(TenantData tenant, M365Options options) => throw new NotImplementedException();

    public static Finding AdminsCloudOnly(TenantData tenant, M365Options options) => throw new NotImplementedException();

    public static Finding PimEligibleNotPermanent(TenantData tenant, M365Options options) => throw new NotImplementedException();
}
