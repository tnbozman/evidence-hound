using System.Text.Json.Nodes;

namespace EvidenceHound.SophosDevServer;

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
