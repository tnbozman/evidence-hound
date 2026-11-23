using System.Text.Json.Nodes;

namespace EvidenceHound.SophosDevServer;

/// <summary>An evidence file an earlier run of the sophos-central profile would have kept, and when that run collected it.</summary>
public sealed record EarlierEvidence(DateTimeOffset CollectedAt, JsonObject Evidence);

/// <summary>Renders a <see cref="SophosDevTenant"/> as the Sophos Central answers the <c>sophos-central</c> profile reads.</summary>
public static class SophosAnswers
{
    /// <summary>Each list's name (also the evidence file the profile writes) and the path that answers it, under the data region.</summary>
    public static IReadOnlyDictionary<string, string> Paths { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["firewalls"] = "/firewall/v1/firewalls",
        ["admins"] = "/common/v1/admins",
        ["roles"] = "/common/v1/roles",
    };

    /// <summary>Who the credential belongs to: the tenant, with the dev server as both its global and its data region host.</summary>
    public static JsonObject Whoami(SophosDevTenant tenant, Uri url)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(url);
        var host = url.ToString().TrimEnd('/');
        return new()
        {
            ["id"] = tenant.TenantId,
            ["idType"] = "tenant",
            ["apiHosts"] = new JsonObject { ["global"] = host, ["dataRegion"] = host },
        };
    }

    /// <summary>The token address's one answer, whatever is sent: the dev server checks no credential.</summary>
    public static JsonObject TokenAnswer() => new()
    {
        ["access_token"] = SophosDevTenant.AccessToken,
        ["errorCode"] = "success",
        ["expires_in"] = 3600,
        ["message"] = "OK",
        ["token_type"] = "bearer",
    };

    /// <summary>The path of the firmware upgrade check, a POST naming the firewalls in its body.</summary>
    public static readonly string UpgradeCheckPath = "/firewall/v1/firewalls/actions/firmware-upgrade-check";

    /// <summary>The firmware upgrade check's answer for one firewall: the upgrades offered and their (fictional) release notes, no date.</summary>
    public static JsonObject UpgradeCheck(Firewall firewall)
    {
        ArgumentNullException.ThrowIfNull(firewall);
        return new()
        {
            ["firewalls"] = new JsonArray(Offer(firewall, firewall.UpgradeTo)),
            ["firmwareVersions"] = firewall.UpgradeTo is { } version
                ? new JsonArray(new JsonObject { ["version"] = version, ["size"] = "1.1 GB", ["bugs"] = new JsonArray("NC-00001"), ["news"] = new JsonArray("Fictional release notes for the dev tenant.") })
                : new JsonArray(),
        };
    }

    /// <summary>
    /// The <c>firmware_upgrades.json</c> evidence of one run a day for the days the longest offer has been seen, oldest first: what
    /// the evidence store would hold had the profile run daily. Empty when no upgrade is offered.
    /// </summary>
    public static IReadOnlyList<EarlierEvidence> EarlierUpgradeChecks(SophosDevTenant tenant, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var days = tenant.Firewalls.Select(f => f.UpgradeTo is null ? 0 : f.OfferedDays).DefaultIfEmpty(0).Max();
        return [.. Enumerable.Range(1, days).Reverse().Select(ago => new EarlierEvidence(now.AddDays(-ago), new JsonObject
        {
            ["query"] = "firmware_upgrades",
            ["item_count"] = tenant.Firewalls.Count,
            ["items"] = new JsonArray([.. tenant.Firewalls.Select(f => (JsonNode)Offer(f, ago <= f.OfferedDays ? f.UpgradeTo : null))]),
        }))];
    }

    public static Dictionary<string, JsonArray> Items(SophosDevTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return new(StringComparer.Ordinal)
        {
            ["firewalls"] = [.. tenant.Firewalls.Select(f => (JsonNode)Firewall(f, tenant.TenantId))],
            ["admins"] = [.. tenant.Admins.Select(a => (JsonNode)Admin(a, tenant.TenantId))],
            ["roles"] = [.. tenant.Roles.Select(r => (JsonNode)Role(r))],
        };
    }

    private static JsonObject Firewall(Firewall firewall, string tenant) => new()
    {
        ["id"] = firewall.Id,
        ["cluster"] = null,
        ["tenant"] = new JsonObject { ["id"] = tenant },
        ["serialNumber"] = firewall.SerialNumber,
        ["group"] = null,
        ["hostname"] = firewall.Hostname,
        ["name"] = firewall.Name,
        ["firmwareVersion"] = firewall.Firmware,
        ["model"] = firewall.Model,
        ["status"] = new JsonObject { ["managing"] = "approved", ["reporting"] = "approved", ["connected"] = true, ["suspended"] = false },
        ["capabilities"] = new JsonArray("firmwareUpgrade", "managing", "reporting"),
    };

    private static JsonObject Offer(Firewall firewall, string? version) => new()
    {
        ["id"] = firewall.Id,
        ["firmwareVersion"] = firewall.Firmware,
        ["upgradeToVersion"] = version is null ? new JsonArray() : new JsonArray(version),
    };

    private static JsonObject Admin(Admin admin, string tenant)
    {
        var name = $"{admin.FirstName} {admin.LastName}";
        return new()
        {
            ["id"] = admin.Id,
            ["tenant"] = new JsonObject { ["id"] = tenant, ["name"] = "Example Pty Ltd" },
            ["profile"] = new JsonObject { ["name"] = name, ["firstName"] = admin.FirstName, ["lastName"] = admin.LastName, ["email"] = admin.Email },
            ["roleAssignments"] = new JsonArray([.. admin.RoleIds.Select((role, i) => (JsonNode)new JsonObject { ["id"] = $"{admin.Id[..^4]}{i:x4}", ["roleId"] = role })]),
        };
    }

    private static JsonObject Role(Role role) => new()
    {
        ["id"] = role.Id,
        ["name"] = role.Name,
        ["type"] = "predefined",
        ["principalType"] = role.PrincipalType,
        ["description"] = role.Description,
    };
}
