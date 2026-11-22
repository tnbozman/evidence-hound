using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>
/// Signs in to Sophos Central: a client credentials token from Sophos ID, then <c>whoami</c> to learn whether the credential
/// belongs to a tenant, a partner or an organisation, and the data region API of the tenant to read.
/// </summary>
internal static class SophosAuth
{
    private static readonly string CentralDomain = ".central.sophos.com";

    public static async Task<Result<SophosSession>> SignInAsync(HttpClient http, SophosConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return await (await TokenAsync(http, connection, cancellationToken))
            .BindAsync(token => WhoamiAsync(http, connection, token, cancellationToken)
                .BindAsync(identity => TenantAsync(http, connection, token, identity, cancellationToken)));
    }

    /// <summary>
    /// The token goes with every request, so data is only read from a Sophos Central host, or from the configured global API's
    /// own host (as in tests and proxies).
    /// </summary>
    public static bool IsTrustedRegion(Uri region, Uri global)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(global);
        var sameHost = string.Equals(region.Host, global.Host, StringComparison.OrdinalIgnoreCase) && region.Scheme == global.Scheme;
        var central = region.Scheme == Uri.UriSchemeHttps && region.Host.EndsWith(CentralDomain, StringComparison.OrdinalIgnoreCase);
        return sameHost || central;
    }

    private static async Task<Result<string>> TokenAsync(HttpClient http, SophosConnection connection, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(connection.IdUrl, "api/v2/oauth2/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = connection.ClientId,
                ["client_secret"] = connection.ClientSecret.Reveal(),
                ["scope"] = "token",
            }),
        };
        return (await SophosHttp.SendAsync(http, request, "a token", cancellationToken)).Bind(response =>
            response is { IsSuccess: true, Body: { } body } && body["access_token"]?.GetValue<string>() is { Length: > 0 } token
                ? Result.Ok(token)
                : Result.Fail<string>(new Problem(
                    "sophos.auth_failed", $"Sophos ID refused the API credential ({(int)response.Status} {response.Error}). Check client_id and the client_secret secret.")));
    }

    private static async Task<Result<Identity>> WhoamiAsync(HttpClient http, SophosConnection connection, string token, CancellationToken cancellationToken)
    {
        using var request = SophosHttp.Get(new Uri(connection.ApiUrl, "whoami/v1"), token);
        return (await SophosHttp.SendAsync(http, request, "whoami", cancellationToken))
            .Bind(response => SophosHttp.Body(response, "whoami"))
            .Bind(Identity.Parse);
    }

    private static async Task<Result<SophosSession>> TenantAsync(HttpClient http, SophosConnection connection, string token, Identity identity, CancellationToken cancellationToken)
    {
        if (identity.Type == "tenant")
        {
            return connection.TenantId is { Length: > 0 } wanted && !string.Equals(wanted, identity.Id, StringComparison.OrdinalIgnoreCase)
                ? Result.Fail<SophosSession>(new Problem("sophos.tenant_mismatch", $"The API credential belongs to tenant {identity.Id}, not the tenant_id setting {wanted}."))
                : Session(token, identity.Id, identity.DataRegion, connection);
        }

        if (connection.TenantId is not { Length: > 0 } tenantId)
        {
            return Result.Fail<SophosSession>(new Problem(
                "sophos.tenant_id_required", $"The API credential is a {identity.Type} credential. Set tenant_id to the ID of the tenant to read."));
        }

        using var request = SophosHttp.Get(new Uri(connection.ApiUrl, $"{identity.Type}/v1/tenants/{Uri.EscapeDataString(tenantId)}"), token);
        request.Headers.Add(identity.Type == "partner" ? "X-Partner-ID" : "X-Organization-ID", identity.Id);
        return (await SophosHttp.SendAsync(http, request, $"tenant {tenantId}", cancellationToken))
            .Bind(response => SophosHttp.Body(response, $"tenant {tenantId}"))
            .Bind(tenant => Session(token, tenantId, Address(tenant["apiHost"]), connection));
    }

    private static Result<SophosSession> Session(string token, string tenantId, Uri? region, SophosConnection connection) => region switch
    {
        null => Result.Fail<SophosSession>(new Problem("sophos.invalid_response", $"Sophos Central gave no data region API for tenant {tenantId}.")),
        _ when !IsTrustedRegion(region, connection.ApiUrl) => Result.Fail<SophosSession>(new Problem(
            "sophos.data_region_untrusted", $"Sophos Central named {region.Host} as the data region, which is not a Sophos Central host; the token was not sent there.")),
        _ => Result.Ok(new SophosSession(token, tenantId, region)),
    };

    /// <summary>An address from Sophos, always ending in a slash so relative paths resolve beneath it.</summary>
    private static Uri? Address(JsonNode? node) =>
        node?.GetValue<string>() is { Length: > 0 } text && Uri.TryCreate(text.EndsWith('/') ? text : text + "/", UriKind.Absolute, out var url) ? url : null;

    /// <summary>Who the credential is, from <c>whoami</c>.</summary>
    private sealed record Identity(string Id, string Type, Uri? DataRegion)
    {
        private static readonly string[] Types = ["tenant", "partner", "organization"];

        public static Result<Identity> Parse(JsonObject body) =>
            body["id"]?.GetValue<string>() is { Length: > 0 } id && body["idType"]?.GetValue<string>() is { } type && Types.Contains(type, StringComparer.Ordinal)
                ? Result.Ok(new Identity(id, type, Address(body["apiHosts"]?["dataRegion"])))
                : Result.Fail<Identity>(new Problem("sophos.invalid_response", "Sophos Central's whoami answer has no id or a type other than tenant, partner or organization."));
    }
}
