using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.Rest.Profiles;

/// <summary>
/// A REST profile, parsed once from YAML: what every customer of one product shares. The connection, the queries read once per run,
/// the evidence datasets built from them and the checks over those datasets. Text that may hold <c>${...}</c> placeholders is kept
/// as written; placeholders are filled in for each collector instance from its parameters, its secrets and bootstrap variables.
/// </summary>
public sealed record Profile(
    string Name,
    string Version,
    string? Source,
    string? Requires,
    string Description,
    IReadOnlyList<ParameterSpec> Parameters,
    IReadOnlyList<SecretSpec> Secrets,
    Connection Connection,
    IReadOnlyList<Query> Bootstrap,
    IReadOnlyList<Query> Queries,
    IReadOnlyList<Dataset> Datasets,
    IReadOnlyList<Check> Checks)
{
    /// <summary>Every query, bootstrap first.</summary>
    public IEnumerable<Query> AllQueries => Bootstrap.Concat(Queries);
}

/// <summary>The kinds of value a parameter holds. Each instance's settings are checked against them before anything is called.</summary>
public enum ParameterType
{
    Text,
    WholeNumber,
    Number,
    Flag,
    List,
    Duration,
    Url,
}

/// <summary>A per-instance setting the profile reads, such as a client ID or a threshold. <paramref name="Default"/> is used when the instance does not set it.</summary>
public sealed record ParameterSpec(string Name, ParameterType Type, bool Required, string? Default, string Description);

/// <summary>A secret the profile needs from each instance, referenced as <c>${secrets.name}</c>.</summary>
public sealed record SecretSpec(string Name, bool Required, string Description);

/// <summary>Where the API is, which hosts may receive credentials, how to sign in, how fast to call it and how to trust its certificate.</summary>
public sealed record Connection(string BaseUrl, IReadOnlyList<string> AllowedHosts, Auth Auth, RateLimit RateLimit, TlsSettings Tls);

/// <summary>Requests per second, and how many times a 429 or 503 is retried (honouring <c>Retry-After</c>).</summary>
public sealed record RateLimit(double PerSecond, int MaxRetries)
{
    public static RateLimit Default { get; } = new(5, 3);
}

/// <summary>
/// How the server's certificate is trusted. With a pinned SHA-256 thumbprint, a self-signed certificate is accepted only when it
/// matches; verification is never simply turned off.
/// </summary>
public sealed record TlsSettings(string? PinnedThumbprint, string? ClientCertificate)
{
    public static TlsSettings Default { get; } = new(null, null);
}

/// <summary>How requests are signed in. Every value may hold placeholders.</summary>
public abstract record Auth
{
    /// <summary>Every text value of this auth, for placeholder checks.</summary>
    public abstract IEnumerable<string> Texts { get; }
}

/// <summary>No sign in.</summary>
public sealed record NoAuth : Auth
{
    public override IEnumerable<string> Texts => [];
}

/// <summary>
/// OAuth 2.0 client credentials. Either <paramref name="ClientSecret"/>, or <paramref name="ClientCertificate"/> (a PEM certificate
/// with its private key) for a signed client assertion.
/// </summary>
public sealed record OAuthClientCredentials(string TokenUrl, string ClientId, string? ClientSecret, string? ClientCertificate, string? Scope) : Auth
{
    public override IEnumerable<string> Texts => new[] { TokenUrl, ClientId, ClientSecret, ClientCertificate, Scope }.OfType<string>();
}

/// <summary>A static API key sent in a header or a query parameter called <paramref name="Name"/>.</summary>
public sealed record ApiKeyAuth(ApiKeyLocation In, string Name, string Value) : Auth
{
    public override IEnumerable<string> Texts => [Name, Value];
}

public enum ApiKeyLocation
{
    Header,
    Query,
}

/// <summary>A static bearer token.</summary>
public sealed record BearerAuth(string Token) : Auth
{
    public override IEnumerable<string> Texts => [Token];
}

/// <summary>HTTP basic authentication.</summary>
public sealed record BasicAuth(string Username, string Password) : Auth
{
    public override IEnumerable<string> Texts => [Username, Password];
}

/// <summary>Mutual TLS: the client certificate (PEM with its private key) is the only credential.</summary>
public sealed record MutualTlsAuth(string Certificate) : Auth
{
    public override IEnumerable<string> Texts => [Certificate];
}

/// <summary>
/// One request (or a series of pages, or one request per item of another query), read once per run and saved as one evidence file.
/// <see cref="Vars"/> names values later queries can use as <c>${name}</c>.
/// </summary>
public sealed record Query(string Name, string Method, string Path)
{
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    public JsonNode? Body { get; init; }

    public Paging? Paging { get; init; }

    /// <summary>Where the list of items is in each response; <c>$</c> when the response is the list.</summary>
    public string Items { get; init; } = "$";

    /// <summary>Property names, at any depth, that identify a person, a device or a network. Hashed in evidence unless identities are included.</summary>
    public IReadOnlyList<string> Identities { get; init; } = [];

    public IReadOnlyDictionary<string, string> Vars { get; init; } = new Dictionary<string, string>();

    /// <summary>Another query whose items this query is called once for, each available as <c>${item.path}</c>.</summary>
    public string? ForEach { get; init; }

    public int MaxCalls { get; init; } = 100;

    /// <summary>Status codes that mean "not available here" (for example, no licence) rather than a failure.</summary>
    public IReadOnlyList<int> OptionalOn { get; init; } = [];

    public AsyncJob? Async { get; init; }

    public ResponseFormat Format { get; init; } = ResponseFormat.Auto;

    /// <summary>Hosts other than the API's that a redirect may lead to, such as signed download storage. They receive a plain GET without credentials.</summary>
    public IReadOnlyList<string> RedirectHosts { get; init; } = [];

    /// <summary>With <see cref="ResponseFormat.Zip"/>: the archive member read, by its extension as JSON, XML or CSV (JSON otherwise).</summary>
    public string? Member { get; init; }

    /// <summary>Paths that are always arrays when an XML response is read.</summary>
    public IReadOnlyList<string> Arrays { get; init; } = [];

    /// <summary>When not empty, only these item properties are kept, in memory and in evidence.</summary>
    public IReadOnlyList<string> Keep { get; init; } = [];

    public int MaxItems { get; init; } = 50_000;

    public long MaxBytes { get; init; } = 50_000_000;

    public int MaxPages { get; init; } = 1000;

    /// <summary>A bootstrap query runs before every other query, is not saved as evidence and cannot be checked.</summary>
    public bool IsBootstrap { get; init; }

    /// <summary>Every text that may hold placeholders.</summary>
    public IEnumerable<string> Texts =>
        new[] { Path }.Concat(Headers.Values).Concat(Parameters.Values).Concat(BodyTexts(Body));

    private static IEnumerable<string> BodyTexts(JsonNode? node) => node switch
    {
        JsonObject obj => obj.SelectMany(p => BodyTexts(p.Value)),
        JsonArray array => array.SelectMany(BodyTexts),
        JsonValue value when value.TryGetValue<string>(out var text) => [text],
        _ => [],
    };
}

/// <summary>How the response body is read. Auto uses the response's content type.</summary>
public enum ResponseFormat
{
    Auto,
    Json,
    Xml,
    Csv,

    /// <summary>A zip archive, such as a pipeline artefact, of which one <see cref="Query.Member"/> is read.</summary>
    Zip,
}

/// <summary>How a query's pages are followed.</summary>
public abstract record Paging;

/// <summary>The response holds the next page's address (absolute or relative) at <paramref name="Next"/>, absent on the last page.</summary>
public sealed record NextLinkPaging(string Next) : Paging;

/// <summary>Pages by number in query parameter <paramref name="Page"/>, starting at 1, until <paramref name="TotalPages"/> is reached.</summary>
public sealed record PageNumberPaging(string Page, string? SizeParam, int Size, string TotalPages) : Paging;

/// <summary>The response holds a cursor at <paramref name="Cursor"/>, sent back as query parameter <paramref name="Param"/>; no cursor ends paging.</summary>
public sealed record CursorPaging(string Cursor, string Param) : Paging;

/// <summary>Offset and limit parameters; paging ends at <paramref name="Total"/> items, or at a page shorter than the limit when there is no total.</summary>
public sealed record OffsetPaging(string OffsetParam, string LimitParam, int Limit, string? Total) : Paging;

/// <summary>The HTTP <c>Link</c> header's <c>rel="next"</c> address.</summary>
public sealed record LinkHeaderPaging : Paging;

/// <summary>
/// A report run as a job: the first request starts it, <paramref name="StatusUrl"/> in its answer is polled until
/// <paramref name="Done"/> holds (or <paramref name="Timeout"/> passes), then <paramref name="ResultUrl"/> in the last status is read.
/// </summary>
public sealed record AsyncJob(string StatusUrl, FieldTest Done, string ResultUrl, TimeSpan Poll, TimeSpan Timeout);

/// <summary>A named evidence dataset: rows of one query, joined with others, flattened, filtered and reduced to named fields.</summary>
public sealed record Dataset(string Name, string From)
{
    public IReadOnlyList<Join> Joins { get; init; } = [];

    /// <summary>A path to a list in each row: one row per element, each element becoming the row with its parent under <c>parent</c>.</summary>
    public string? Flatten { get; init; }

    public IReadOnlyList<FieldTest> Where { get; init; } = [];

    /// <summary>Output field name to path. Empty keeps whole rows.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Attaches the rows of <paramref name="With"/> whose <paramref name="Right"/> value equals a <paramref name="Left"/> value of the row,
/// as a list under <paramref name="As"/>. A required join reports rows that match nothing.
/// </summary>
public sealed record Join(string With, string Left, string Right, string As, bool Optional);

/// <summary>What an empty list in scope means. There is no default: every declarative check says.</summary>
public enum EmptyResult
{
    NotApplicable,
    Fail,
    UnableToAssess,
}

/// <summary>
/// A check: its framework-neutral ID, what it reads, and either a declarative test (<see cref="Each"/> and
/// <see cref="Require"/>) or a built-in code check named by <see cref="Code"/>.
/// </summary>
public sealed record Check(string Id, IReadOnlyList<string> Over)
{
    public string Title { get; init; } = "";

    public IReadOnlyList<FieldTest> Where { get; init; } = [];

    public FieldTest? Each { get; init; }

    public Requirement Require { get; init; } = new RequireAll();

    public EmptyResult? WhenEmpty { get; init; }

    public string? Describe { get; init; }

    public string? Code { get; init; }

    /// <summary>The code check's own settings, as written.</summary>
    public IReadOnlyDictionary<string, string> With { get; init; } = new Dictionary<string, string>();
}

/// <summary>The test operators. Each compares one field of a row with the test's operands.</summary>
public enum TestOperator
{
    EqualTo,
    NotEqualTo,
    In,
    Matches,
    AnyMatches,
    Exists,
    VersionIn,
    VersionAtLeast,
    Within,
    OlderThan,
    AtLeast,
    AtMost,
}

/// <summary>What a missing field means for a test. Unless the profile says otherwise, the check cannot be assessed.</summary>
public enum MissingRule
{
    UnableToAssess,
    Pass,
    Fail,
}

/// <summary>One test of one field: the operator and its operands (a list operator may take several, or one <c>${list}</c> parameter).</summary>
public sealed record FieldTest(string Field, TestOperator Operator, IReadOnlyList<string> Operands)
{
    public MissingRule Missing { get; init; } = MissingRule.UnableToAssess;

    public bool Negate { get; init; }

    public bool Equals(FieldTest? other) =>
        other is not null && Field == other.Field && Operator == other.Operator && Operands.SequenceEqual(other.Operands) && Missing == other.Missing && Negate == other.Negate;

    public override int GetHashCode() => HashCode.Combine(Field, Operator, Operands.Count, Missing, Negate);
}

/// <summary>How item results become the check's result.</summary>
public abstract record Requirement;

public sealed record RequireAll : Requirement;

public sealed record RequireAny : Requirement;

public sealed record RequireNone : Requirement;

/// <summary>At most <paramref name="Count"/> rows in scope (a number or a placeholder).</summary>
public sealed record RequireCountAtMost(string Count) : Requirement;

/// <summary>At least <paramref name="Count"/> rows in scope.</summary>
public sealed record RequireCountAtLeast(string Count) : Requirement;

/// <summary>At least <paramref name="Pass"/> of rows pass; from <paramref name="PartialAt"/> the result is partial.</summary>
public sealed record RequireRatio(double Pass, double? PartialAt) : Requirement;
