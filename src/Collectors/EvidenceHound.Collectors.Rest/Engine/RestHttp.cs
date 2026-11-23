using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>One request to send: method, address (without auth), headers and an optional JSON body.</summary>
public sealed record RestRequest(HttpMethod Method, Uri Url, IReadOnlyDictionary<string, string> Headers, JsonNode? Body = null)
{
    /// <summary>Other hosts a redirect may lead to, such as signed download storage. They get a plain GET: no sign-in and no headers.</summary>
    public IReadOnlyList<string> RedirectHosts { get; init; } = [];

    /// <summary>True once a redirect has left for one of <see cref="RedirectHosts"/>: send it unsigned.</summary>
    internal bool Unsigned { get; init; }
}

/// <summary>An answer: status, body text, content type, the headers the engine reads (Link, Location, Retry-After) and the address it came from.</summary>
public sealed record RestResponse(HttpStatusCode Status, string Body, string? ContentType, IReadOnlyDictionary<string, string> Headers, Uri Url)
{
    /// <summary>The body as received, for a binary answer such as a zip; empty when only the text was kept.</summary>
    public ReadOnlyMemory<byte> Bytes { get; init; }

    public bool IsSuccess => (int)Status is >= 200 and < 300;

    public string? Header(string name) => Headers.GetValueOrDefault(name);
}

/// <summary>
/// Sends requests for one profile instance: only to allowed hosts and over https (or to this machine), spaced by the rate limit,
/// signed in, retrying 429 and 503 within bounds, refreshing an OAuth token once when it is refused, and following redirects only on
/// the same host. Every expected failure is a problem; nothing is thrown for a refused or unreachable API.
/// </summary>
public sealed class RestHttp(HttpClient http, IClock clock, IDelay delay, Connection connection, Renderer renderer, IReadOnlyList<string> allowedHosts)
{
    private static readonly int MaxRedirects = 5;

    private static readonly TimeSpan TokenMargin = TimeSpan.FromSeconds(30);

    private DateTimeOffset? _last;

    private (string Value, DateTimeOffset Expires)? _token;

    public async Task<Result<RestResponse>> SendAsync(RestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var refreshed = false;
        var url = request.Url;
        var unsigned = false;
        for (var redirects = 0; ; redirects++)
        {
            var sent = await SendWithRetriesAsync(unsigned ? new RestRequest(HttpMethod.Get, url, new Dictionary<string, string>()) { Unsigned = true } : request with { Url = url }, cancellationToken);
            if (sent is not Result<RestResponse>.Ok { Value: var response })
            {
                return sent;
            }

            if (response.Status == HttpStatusCode.Unauthorized && connection.Auth is OAuthClientCredentials && !refreshed && !unsigned)
            {
                _token = null;
                refreshed = true;
                continue;
            }

            if ((int)response.Status is >= 300 and < 400 && Location(response) is { } location)
            {
                if (redirects < MaxRedirects && !SameOrigin(location, url) && request.Method == HttpMethod.Get && HostRules.IsAllowed(location, request.RedirectHosts))
                {
                    url = location;
                    unsigned = true;
                    continue;
                }

                if (!SameOrigin(location, url) || redirects == MaxRedirects)
                {
                    return Result.Fail<RestResponse>(new Problem("rest.redirect_refused", $"{url.Host} answered with a redirect to {location.Host}; redirects are only followed on the same host, so nothing was sent there."));
                }

                url = location;
                continue;
            }

            return sent;
        }
    }

    private static Uri? Location(RestResponse response) => response.Header("Location") is { } location && Uri.TryCreate(response.Url, location, out var url) ? url : null;

    private static bool SameOrigin(Uri a, Uri b) => a.Scheme == b.Scheme && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;

    private async Task<Result<RestResponse>> SendWithRetriesAsync(RestRequest request, CancellationToken cancellationToken)
    {
        if (!request.Unsigned && !HostRules.IsAllowed(request.Url, allowedHosts))
        {
            return Result.Fail<RestResponse>(new Problem("rest.host_not_allowed", $"{request.Url.Host} is not in allowed_hosts, so nothing was sent there."));
        }

        for (var attempt = 0; ; attempt++)
        {
            var response = await SendOnceAsync(request, cancellationToken);
            if (response is not Result<RestResponse>.Ok { Value: { Status: HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable } busy })
            {
                return response;
            }

            if (attempt >= connection.RateLimit.MaxRetries)
            {
                return Result.Fail<RestResponse>(new Problem(
                    "rest.rate_limited", string.Create(CultureInfo.InvariantCulture, $"{request.Url.Host} answered {(int)busy.Status} and still did after {attempt} retries.")));
            }

            await delay.WaitAsync(RetryAfter(busy, attempt), cancellationToken);
        }
    }

    /// <summary>The server's <c>Retry-After</c> (seconds or a date, at most five minutes), else 1, 2, 4... seconds.</summary>
    private TimeSpan RetryAfter(RestResponse response, int attempt) => response.Header("Retry-After") switch
    {
        { } text when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) => TimeSpan.FromSeconds(Math.Min(seconds, 300)),
        { } text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) => Clamp(at - clock.UtcNow),
        _ => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
    };

    private static TimeSpan Clamp(TimeSpan wait) => wait < TimeSpan.Zero ? TimeSpan.Zero : wait > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : wait;

    private async Task<Result<RestResponse>> SendOnceAsync(RestRequest request, CancellationToken cancellationToken)
    {
        var auth = request.Unsigned ? Result.Ok(Signer.None) : await AuthorisationAsync(cancellationToken);
        if (auth is Result<Signer>.Fail failed)
        {
            return Result.Fail<RestResponse>(failed.Errors);
        }

        await PaceAsync(cancellationToken);
        var signer = ((Result<Signer>.Ok)auth).Value;
        using var message = new HttpRequestMessage(request.Method, signer.Url(request.Url));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        foreach (var (name, value) in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        signer.Sign(message);
        if (request.Body is { } body)
        {
            message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var text = Encoding.UTF8.GetString(bytes);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (response.Headers.Location is { } location)
            {
                headers["Location"] = location.ToString();
            }

            if (RetryAfterHeader(response) is { } retryAfter)
            {
                headers["Retry-After"] = retryAfter;
            }

            if (response.Headers.TryGetValues("Link", out var links))
            {
                headers["Link"] = string.Join(", ", links);
            }

            return Result.Ok(new RestResponse(response.StatusCode, text, response.Content.Headers.ContentType?.MediaType, headers, request.Url) { Bytes = bytes });
        }
        catch (HttpRequestException ex)
        {
            return Result.Fail<RestResponse>(new Problem("rest.unreachable", $"Could not reach {request.Url.Host}: {ex.Message}"));
        }
    }

    private static string? RetryAfterHeader(HttpResponseMessage response) =>
        response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => ((int)delta.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            { Date: { } date } => date.ToString("O", CultureInfo.InvariantCulture),
            _ => null,
        };

    /// <summary>Waits until the rate limit allows the next request.</summary>
    private async Task PaceAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(1 / Math.Max(connection.RateLimit.PerSecond, 0.001));
        if (_last is { } last && clock.UtcNow < last + interval)
        {
            await delay.WaitAsync(last + interval - clock.UtcNow, cancellationToken);
        }

        _last = clock.UtcNow;
    }

    private async Task<Result<Signer>> AuthorisationAsync(CancellationToken cancellationToken) => connection.Auth switch
    {
        OAuthClientCredentials oauth => (await TokenAsync(oauth, cancellationToken)).Map(token => Signer.Header(new AuthenticationHeaderValue("Bearer", token))),
        BearerAuth bearer => renderer.Render(bearer.Token).Map(token => Signer.Header(new AuthenticationHeaderValue("Bearer", token))),
        BasicAuth basic => renderer.Render(basic.Username).Bind(user => renderer.Render(basic.Password)
            .Map(password => Signer.Header(new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")))))),
        ApiKeyAuth { In: ApiKeyLocation.Header } key => renderer.Render(key.Name).Bind(name => renderer.Render(key.Value).Map(value => Signer.CustomHeader(name, value))),
        ApiKeyAuth key => renderer.Render(key.Name).Bind(name => renderer.Render(key.Value).Map(value => Signer.Query(name, value))),
        _ => Result.Ok(Signer.None),
    };

    private async Task<Result<string>> TokenAsync(OAuthClientCredentials oauth, CancellationToken cancellationToken)
    {
        if (_token is { } cached && clock.UtcNow < cached.Expires - TokenMargin)
        {
            return Result.Ok(cached.Value);
        }

        var form = renderer.Render(oauth.TokenUrl).Bind(tokenUrl => Uri.TryCreate(tokenUrl, UriKind.Absolute, out var url)
                ? Result.Ok(url)
                : Result.Fail<Uri>(new Problem("rest.token_url_invalid", $"The token URL '{tokenUrl}' is not an address.")))
            .Bind(url => renderer.Render(oauth.ClientId).Bind(clientId => Credential(oauth, clientId, url).Bind(credential =>
                (oauth.Scope is null ? Result.Ok("") : renderer.Render(oauth.Scope)).Map(scope => (Url: url, Fields: Fields(clientId, credential, scope))))));
        if (form is not Result<(Uri Url, Dictionary<string, string> Fields)>.Ok { Value: var request })
        {
            return Result.Fail<string>(((Result<(Uri, Dictionary<string, string>)>.Fail)form).Errors);
        }

        if (!HostRules.IsAllowed(request.Url, allowedHosts))
        {
            return Result.Fail<string>(new Problem("rest.host_not_allowed", $"The token URL's host {request.Url.Host} is not in allowed_hosts, so the client credential was not sent."));
        }

        await PaceAsync(cancellationToken);
        try
        {
            using var content = new FormUrlEncodedContent(request.Fields);
            using var response = await http.PostAsync(request.Url, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = TryParse(body);
            if (!response.IsSuccessStatusCode || json?["access_token"] is not JsonValue token || !token.TryGetValue<string>(out var value))
            {
                var error = json?["error"] is JsonValue e && e.TryGetValue<string>(out var code) ? code : "no token";
                return Result.Fail<string>(new Problem(
                    "rest.auth_failed", string.Create(CultureInfo.InvariantCulture, $"The token endpoint refused the client credential ({(int)response.StatusCode} {error}). Check the client ID and secret.")));
            }

            var lifetime = json["expires_in"] is JsonValue seconds && seconds.TryGetValue<int>(out var s) ? s : 3600;
            _token = (value, clock.UtcNow.AddSeconds(lifetime));
            return Result.Ok(value);
        }
        catch (HttpRequestException ex)
        {
            return Result.Fail<string>(new Problem("rest.unreachable", $"Could not reach the token endpoint {request.Url.Host}: {ex.Message}"));
        }
    }

    private static Dictionary<string, string> Fields(string clientId, KeyValuePair<string, string>[] credential, string scope)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["grant_type"] = "client_credentials", ["client_id"] = clientId };
        foreach (var (key, value) in credential)
        {
            fields[key] = value;
        }

        if (scope.Length > 0)
        {
            fields["scope"] = scope;
        }

        return fields;
    }

    private Result<KeyValuePair<string, string>[]> Credential(OAuthClientCredentials oauth, string clientId, Uri tokenUrl) =>
        oauth.ClientSecret is { } secret
            ? renderer.Render(secret).Map(value => new[] { KeyValuePair.Create("client_secret", value) })
            : renderer.Render(oauth.ClientCertificate!).Bind(pem => ClientAssertion.Create(pem, clientId, tokenUrl, clock.UtcNow))
                .Map(assertion => new[]
                {
                    KeyValuePair.Create("client_assertion_type", "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"),
                    KeyValuePair.Create("client_assertion", assertion),
                });

    private static JsonObject? TryParse(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>How a request is signed: a header, or a query parameter.</summary>
    private sealed record Signer(Action<HttpRequestMessage> Sign, Func<Uri, Uri> Url)
    {
        public static Signer None { get; } = new(_ => { }, url => url);

        public static Signer Header(AuthenticationHeaderValue value) => new(m => m.Headers.Authorization = value, url => url);

        public static Signer CustomHeader(string name, string value) => new(m => m.Headers.TryAddWithoutValidation(name, value), url => url);

        public static Signer Query(string name, string value) => new(_ => { }, url => QueryString.Set(url, name, value));
    }
}

/// <summary>Sets query parameters on an address.</summary>
public static class QueryString
{
    public static Uri Set(Uri url, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(url);
        var builder = new UriBuilder(url);
        var pairs = builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !string.Equals(Uri.UnescapeDataString(p.Split('=')[0]), name, StringComparison.Ordinal))
            .Append($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        builder.Query = string.Join('&', pairs);
        return builder.Uri;
    }
}

/// <summary>A signed client assertion (RFC 7523) from a PEM certificate and private key, as Microsoft Entra ID and others accept instead of a secret.</summary>
public static class ClientAssertion
{
    public static Result<string> Create(string pem, string clientId, Uri audience, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(audience);
        X509Certificate2 certificate;
        try
        {
            certificate = X509Certificate2.CreateFromPem(pem, pem);
        }
        catch (CryptographicException)
        {
            return Result.Fail<string>(new Problem("rest.certificate_invalid", "The client certificate secret must be a PEM certificate with its private key."));
        }

        using (certificate)
        using (var key = certificate.GetRSAPrivateKey())
        {
            if (key is null)
            {
                return Result.Fail<string>(new Problem("rest.certificate_invalid", "The client certificate has no RSA private key."));
            }

            var header = new JsonObject
            {
                ["alg"] = "RS256",
                ["typ"] = "JWT",
                ["x5t"] = Base64Url(certificate.GetCertHash()),
                ["x5t#S256"] = Base64Url(certificate.GetCertHash(HashAlgorithmName.SHA256)),
            };
            var claims = new JsonObject
            {
                ["aud"] = audience.ToString(),
                ["iss"] = clientId,
                ["sub"] = clientId,
                ["jti"] = Guid.NewGuid().ToString(),
                ["nbf"] = now.ToUnixTimeSeconds(),
                ["exp"] = now.AddMinutes(10).ToUnixTimeSeconds(),
            };
            var unsigned = $"{Base64Url(Encoding.UTF8.GetBytes(header.ToJsonString()))}.{Base64Url(Encoding.UTF8.GetBytes(claims.ToJsonString()))}";
            var signature = key.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Result.Ok($"{unsigned}.{Base64Url(signature)}");
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
