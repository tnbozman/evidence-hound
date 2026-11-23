using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Collectors.Rest.Profiles;

/// <summary>
/// Reads a profile's YAML into a <see cref="Profile"/>, reporting every problem at once with its line. Parsing checks shape (keys,
/// kinds of value, operators); <see cref="ProfileValidator"/> checks meaning (references, paths, hosts, cycles).
/// </summary>
public static class ProfileParser
{
    private static readonly string[] TopKeys = ["profile", "version", "source", "description", "requires", "parameters", "secrets", "connection", "bootstrap", "queries", "datasets", "checks"];
    private static readonly string[] ParameterKeys = ["type", "required", "default", "description"];
    private static readonly string[] SecretKeys = ["required", "description"];
    private static readonly string[] ConnectionKeys = ["base_url", "allowed_hosts", "auth", "rate_limit", "tls"];
    private static readonly string[] RateLimitKeys = ["per_second", "max_retries"];
    private static readonly string[] TlsKeys = ["pinned_thumbprint", "client_certificate"];
    private static readonly string[] QueryKeys =
    [
        "get", "post", "headers", "query", "body", "paging", "items", "identities", "vars", "for_each", "max_calls", "optional_on", "async", "format", "member", "redirect_hosts", "write", "arrays", "keep",
        "max_items", "max_bytes", "max_pages",
    ];
    private static readonly string[] AsyncKeys = ["status_url", "done", "result_url", "poll_seconds", "timeout_seconds"];
    private static readonly string[] DatasetKeys = ["from", "join", "flatten", "where", "fields"];
    private static readonly string[] JoinKeys = ["with", "on", "as", "optional"];
    private static readonly string[] OnKeys = ["left", "right"];
    private static readonly string[] CheckKeys = ["id", "title", "over", "where", "each", "require", "when_empty", "describe", "code", "with", "history"];
    private static readonly string[] TestModifiers = ["field", "missing", "negate"];

    /// <summary>The YAML name of each value.</summary>
    public static IReadOnlyDictionary<string, ParameterType> ParameterTypes { get; } = new Dictionary<string, ParameterType>(StringComparer.Ordinal)
    {
        ["string"] = ParameterType.Text,
        ["int"] = ParameterType.WholeNumber,
        ["number"] = ParameterType.Number,
        ["bool"] = ParameterType.Flag,
        ["list"] = ParameterType.List,
        ["duration"] = ParameterType.Duration,
        ["url"] = ParameterType.Url,
    };

    /// <summary>The auth types a connection may use.</summary>
    public static IReadOnlyList<string> AuthTypes { get; } = ["none", "oauth2_client_credentials", "api_key", "bearer", "basic", "mutual_tls"];

    /// <summary>The paging styles a query may use.</summary>
    public static IReadOnlyList<string> PagingStyles { get; } = ["next_link", "page_number", "cursor", "offset", "link_header"];

    /// <summary>The YAML name of each operator.</summary>
    public static IReadOnlyDictionary<string, TestOperator> Operators { get; } = new Dictionary<string, TestOperator>(StringComparer.Ordinal)
    {
        ["equals"] = TestOperator.EqualTo,
        ["not_equals"] = TestOperator.NotEqualTo,
        ["in"] = TestOperator.In,
        ["matches"] = TestOperator.Matches,
        ["any_matches"] = TestOperator.AnyMatches,
        ["exists"] = TestOperator.Exists,
        ["version_in"] = TestOperator.VersionIn,
        ["version_at_least"] = TestOperator.VersionAtLeast,
        ["within"] = TestOperator.Within,
        ["older_than"] = TestOperator.OlderThan,
        ["at_least"] = TestOperator.AtLeast,
        ["at_most"] = TestOperator.AtMost,
    };

    /// <summary>The YAML name of each value.</summary>
    public static IReadOnlyDictionary<string, EmptyResult> EmptyResults { get; } = new Dictionary<string, EmptyResult>(StringComparer.Ordinal)
    {
        ["not_applicable"] = EmptyResult.NotApplicable,
        ["fail"] = EmptyResult.Fail,
        ["unable_to_assess"] = EmptyResult.UnableToAssess,
    };

    /// <summary>The YAML name of each value.</summary>
    public static IReadOnlyDictionary<string, MissingRule> MissingRules { get; } = new Dictionary<string, MissingRule>(StringComparer.Ordinal)
    {
        ["unable_to_assess"] = MissingRule.UnableToAssess,
        ["pass"] = MissingRule.Pass,
        ["fail"] = MissingRule.Fail,
    };

    private static readonly Dictionary<string, ResponseFormat> Formats = new(StringComparer.Ordinal)
    {
        ["auto"] = ResponseFormat.Auto,
        ["json"] = ResponseFormat.Json,
        ["xml"] = ResponseFormat.Xml,
        ["csv"] = ResponseFormat.Csv,
        ["zip"] = ResponseFormat.Zip,
    };

    public static Result<Profile> Parse(string yaml, string source)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(source);
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            return Result.Fail<Profile>(new Problem("profile.yaml_invalid", $"{source} line {ex.Start.Line.ToString(CultureInfo.InvariantCulture)}: not valid YAML: {ex.Message}"));
        }

        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root)
        {
            return Result.Fail<Profile>(new Problem("profile.document_not_a_map", $"{source}: a profile must be a map of keys."));
        }

        var reader = new Reader(source);
        var profile = reader.Profile(root);
        return reader.Problems.Count > 0 ? Result.Fail<Profile>(reader.Problems) : Result.Ok(profile);
    }

    /// <summary>Reads nodes, keeping every problem; a bad value comes back as a harmless fallback so reading can go on.</summary>
    private sealed class Reader(string source)
    {
        private readonly List<Problem> _problems = [];

        public List<Problem> Problems => _problems;

        public Profile Profile(YamlMappingNode root)
        {
            Keys(root, TopKeys, "profile");
            var requires = Child(root, "requires") switch
            {
                null => null,
                YamlMappingNode map => Requires(map),
                var other => Problem<string?>(other, "profile.value_invalid", "'requires' must be a map such as { hound: \">=1.0\" }.", null),
            };
            return new Profile(
                RequiredText(root, "profile"),
                RequiredText(root, "version"),
                OptionalText(root, "source"),
                requires,
                OptionalText(root, "description") ?? "",
                Named(root, "parameters", Parameter),
                Named(root, "secrets", Secret),
                Child(root, "connection") is YamlMappingNode connection ? Connection(connection) : Missing(root, "connection", DefaultConnection),
                Named(root, "bootstrap", (name, node) => Query(name, node) with { IsBootstrap = true }),
                Named(root, "queries", Query),
                Named(root, "datasets", Dataset),
                List(root, "checks", Check));
        }

        private string? Requires(YamlMappingNode map)
        {
            Keys(map, ["hound"], "requires");
            return OptionalText(map, "hound");
        }

        private static Connection DefaultConnection => new("", [], new NoAuth(), Profiles.RateLimit.Default, TlsSettings.Default);

        private ParameterSpec Parameter(string name, YamlNode node)
        {
            var map = Map(node, $"parameter '{name}'");
            Keys(map, ParameterKeys, $"parameter '{name}'");
            var typeName = OptionalText(map, "type") ?? "string";
            var type = ParameterTypes.TryGetValue(typeName, out var found)
                ? found
                : Problem(map, "profile.value_invalid", $"parameter '{name}' has unknown type '{typeName}'; use {string.Join(", ", ParameterTypes.Keys)}.", ParameterType.Text);
            return new ParameterSpec(name, type, Flag(map, "required"), DefaultText(map), OptionalText(map, "description") ?? "");
        }

        private string? DefaultText(YamlMappingNode map) => Child(map, "default") switch
        {
            null => null,
            YamlScalarNode scalar => scalar.Value,
            YamlSequenceNode list => string.Join(", ", list.Children.OfType<YamlScalarNode>().Select(s => s.Value)),
            var other => Problem<string?>(other, "profile.value_invalid", "'default' must be a value or a list.", null),
        };

        private SecretSpec Secret(string name, YamlNode node)
        {
            var map = Map(node, $"secret '{name}'");
            Keys(map, SecretKeys, $"secret '{name}'");
            return new SecretSpec(name, Flag(map, "required"), OptionalText(map, "description") ?? "");
        }

        private Connection Connection(YamlMappingNode map)
        {
            Keys(map, ConnectionKeys, "connection");
            return new Connection(
                RequiredText(map, "base_url"),
                Texts(map, "allowed_hosts"),
                Child(map, "auth") is YamlMappingNode auth ? Auth(auth) : new NoAuth(),
                Child(map, "rate_limit") is YamlMappingNode rate ? RateLimit(rate) : Profiles.RateLimit.Default,
                Child(map, "tls") is YamlMappingNode tls ? Tls(tls) : TlsSettings.Default);
        }

        private RateLimit RateLimit(YamlMappingNode map)
        {
            Keys(map, RateLimitKeys, "rate_limit");
            return new RateLimit(Number(map, "per_second", Profiles.RateLimit.Default.PerSecond), Whole(map, "max_retries", Profiles.RateLimit.Default.MaxRetries));
        }

        private TlsSettings Tls(YamlMappingNode map)
        {
            Keys(map, TlsKeys, "tls");
            return new TlsSettings(OptionalText(map, "pinned_thumbprint"), OptionalText(map, "client_certificate"));
        }

        private Auth Auth(YamlMappingNode map)
        {
            var type = RequiredText(map, "type");
            (string[] Keys, Func<Auth> Build) spec = type switch
            {
                "none" => ([], () => new NoAuth()),
                "oauth2_client_credentials" => (["token_url", "client_id", "client_secret", "client_certificate", "scope"], () => OAuth(map)),
                "api_key" => (["in", "name", "value"], () => new ApiKeyAuth(OptionalText(map, "in") == "query" ? ApiKeyLocation.Query : ApiKeyLocation.Header, RequiredText(map, "name"), RequiredText(map, "value"))),
                "bearer" => (["token"], () => new BearerAuth(RequiredText(map, "token"))),
                "basic" => (["username", "password"], () => new BasicAuth(RequiredText(map, "username"), RequiredText(map, "password"))),
                "mutual_tls" => (["certificate"], () => new MutualTlsAuth(RequiredText(map, "certificate"))),
                _ => (Array.Empty<string>(), () => Problem<Auth>(map, "profile.auth_unknown", $"unknown auth type '{type}'; use {string.Join(", ", AuthTypes)}.", new NoAuth())),
            };
            Keys(map, ["type", .. spec.Keys], "auth");
            if (OptionalText(map, "in") is { } location && location is not ("header" or "query"))
            {
                Problem<int>(map, "profile.value_invalid", "api_key 'in' must be header or query.", 0);
            }

            return spec.Build();
        }

        private OAuthClientCredentials OAuth(YamlMappingNode map)
        {
            var secret = OptionalText(map, "client_secret");
            var certificate = OptionalText(map, "client_certificate");
            if ((secret is null) == (certificate is null))
            {
                Problem<int>(map, "profile.value_invalid", "oauth2_client_credentials needs one of client_secret or client_certificate.", 0);
            }

            return new OAuthClientCredentials(RequiredText(map, "token_url"), RequiredText(map, "client_id"), secret, certificate, OptionalText(map, "scope"));
        }

        private Query Query(string name, YamlNode node)
        {
            var map = Map(node, $"query '{name}'");
            Keys(map, QueryKeys, $"query '{name}'");
            var (method, path) = (OptionalText(map, "get"), OptionalText(map, "post")) switch
            {
                ({ } get, null) => ("GET", get),
                (null, { } post) => ("POST", post),
                _ => Problem(map, "profile.value_invalid", $"query '{name}' needs exactly one of 'get' or 'post'.", ("GET", "")),
            };
            var member = OptionalText(map, "member");
            var zip = OptionalText(map, "format") == "zip";
            if (zip != member is not null)
            {
                Problem(map, "profile.value_invalid", zip ? $"query '{name}' reads a zip, so it needs 'member': the file in the archive to read." : $"query '{name}' has a 'member', which only a query with 'format: zip' reads.", 0);
            }

            return new Query(name, method, path)
            {
                Member = member,
                RedirectHosts = Texts(map, "redirect_hosts"),
                Write = OptionalText(map, "write") is { } write && (write == "true" || (write != "false" && Problem(map, "profile.value_invalid", $"query '{name}': 'write' is true or false.", false))),
                Headers = Strings(map, "headers"),
                Parameters = Strings(map, "query"),
                Body = Child(map, "body") is { } body ? ToJson(body) : null,
                Paging = Child(map, "paging") is YamlMappingNode paging ? Paging(paging) : null,
                Items = OptionalText(map, "items") ?? "$",
                Identities = Texts(map, "identities"),
                Vars = Strings(map, "vars"),
                ForEach = OptionalText(map, "for_each"),
                MaxCalls = Whole(map, "max_calls", 100),
                OptionalOn = [.. Texts(map, "optional_on").Select(code => int.TryParse(code, CultureInfo.InvariantCulture, out var status) ? status : Problem(map, "profile.value_invalid", $"'optional_on' must list HTTP status codes, not '{code}'.", 0))],
                Async = Child(map, "async") is YamlMappingNode job ? Async(job) : null,
                Format = OptionalText(map, "format") is { } format
                    ? Formats.TryGetValue(format, out var f) ? f : Problem(map, "profile.value_invalid", $"unknown format '{format}'; use json, xml, csv or zip.", ResponseFormat.Auto)
                    : ResponseFormat.Auto,
                Arrays = Texts(map, "arrays"),
                Keep = Texts(map, "keep"),
                MaxItems = Whole(map, "max_items", 50_000),
                MaxBytes = Whole(map, "max_bytes", 50_000_000),
                MaxPages = Whole(map, "max_pages", 1000),
            };
        }

        private Paging? Paging(YamlMappingNode map)
        {
            var style = RequiredText(map, "style");
            (string[] Keys, Func<Paging?> Build) spec = style switch
            {
                "next_link" => (["next"], () => new NextLinkPaging(RequiredText(map, "next"))),
                "page_number" => (["page", "size_param", "size", "total_pages"], () => new PageNumberPaging(OptionalText(map, "page") ?? "page", OptionalText(map, "size_param"), Whole(map, "size", 100), RequiredText(map, "total_pages"))),
                "cursor" => (["cursor", "param"], () => new CursorPaging(RequiredText(map, "cursor"), RequiredText(map, "param"))),
                "offset" => (["offset_param", "limit_param", "limit", "total"], () => new OffsetPaging(OptionalText(map, "offset_param") ?? "offset", OptionalText(map, "limit_param") ?? "limit", Whole(map, "limit", 100), OptionalText(map, "total"))),
                "link_header" => ([], () => new LinkHeaderPaging()),
                _ => (Array.Empty<string>(), () => Problem<Paging?>(map, "profile.paging_unknown", $"unknown paging style '{style}'; use {string.Join(", ", PagingStyles)}.", null)),
            };
            Keys(map, ["style", .. spec.Keys], "paging");
            return spec.Build();
        }

        private AsyncJob Async(YamlMappingNode map)
        {
            Keys(map, AsyncKeys, "async");
            var done = Child(map, "done") is { } node ? Test(node) : Missing(map, "done", new FieldTest("$.status", TestOperator.EqualTo, ["done"]));
            return new AsyncJob(
                RequiredText(map, "status_url"),
                done,
                RequiredText(map, "result_url"),
                TimeSpan.FromSeconds(Whole(map, "poll_seconds", 5)),
                TimeSpan.FromSeconds(Whole(map, "timeout_seconds", 300)));
        }

        private Dataset Dataset(string name, YamlNode node)
        {
            var map = Map(node, $"dataset '{name}'");
            Keys(map, DatasetKeys, $"dataset '{name}'");
            return new Dataset(name, RequiredText(map, "from"))
            {
                Joins = Child(map, "join") switch
                {
                    null => [],
                    YamlSequenceNode list => [.. list.Children.Select(Join)],
                    var other => [Join(other)],
                },
                Flatten = OptionalText(map, "flatten"),
                Where = Tests(map, "where"),
                Fields = Strings(map, "fields"),
            };
        }

        private Join Join(YamlNode node)
        {
            var map = Map(node, "join");
            Keys(map, JoinKeys, "join");
            var on = Child(map, "on") as YamlMappingNode ?? Missing(map, "on", new YamlMappingNode());
            Keys(on, OnKeys, "join 'on'");
            var with = RequiredText(map, "with");
            return new Join(with, RequiredText(on, "left"), RequiredText(on, "right"), OptionalText(map, "as") ?? with, Flag(map, "optional"));
        }

        private Check Check(YamlNode node)
        {
            var map = Map(node, "check");
            Keys(map, CheckKeys, "check");
            var id = RequiredText(map, "id");
            return new Check(id, Child(map, "over") is YamlScalarNode { Value: { } over } ? [over] : Texts(map, "over"))
            {
                Title = OptionalText(map, "title") ?? "",
                Where = Tests(map, "where"),
                Each = Child(map, "each") is { } each ? Test(each) : null,
                Require = Child(map, "require") is { } require ? Require(require) : new RequireAll(),
                WhenEmpty = OptionalText(map, "when_empty") is { } empty
                    ? EmptyResults.TryGetValue(empty, out var e) ? e : Problem<EmptyResult?>(map, "profile.value_invalid", $"check '{id}': when_empty must be not_applicable, fail or unable_to_assess.", null)
                    : null,
                Describe = OptionalText(map, "describe"),
                Code = OptionalText(map, "code"),
                With = Strings(map, "with"),
                History = OptionalText(map, "history"),
            };
        }

        private Requirement Require(YamlNode node) => node switch
        {
            YamlScalarNode { Value: "all" } => new RequireAll(),
            YamlScalarNode { Value: "any" } => new RequireAny(),
            YamlScalarNode { Value: "none" } => new RequireNone(),
            YamlMappingNode map when Child(map, "count_at_most") is not null => Single(map, () => new RequireCountAtMost(RequiredText(map, "count_at_most"))),
            YamlMappingNode map when Child(map, "count_at_least") is not null => Single(map, () => new RequireCountAtLeast(RequiredText(map, "count_at_least"))),
            YamlMappingNode map when Child(map, "ratio_at_least") is not null => Ratio(map),
            _ => Problem<Requirement>(node, "profile.require_unknown", "'require' must be all, any, none, or a map with count_at_most, count_at_least or ratio_at_least.", new RequireAll()),
        };

        private Requirement Single(YamlMappingNode map, Func<Requirement> build)
        {
            Keys(map, ["count_at_most", "count_at_least"], "require");
            return build();
        }

        private RequireRatio Ratio(YamlMappingNode map)
        {
            Keys(map, ["ratio_at_least", "partial_at"], "require");
            var pass = Number(map, "ratio_at_least", 1);
            double? partial = Child(map, "partial_at") is null ? null : Number(map, "partial_at", 0);
            if (pass is < 0 or > 1 || partial is < 0 or > 1 || partial > pass)
            {
                Problem<int>(map, "profile.value_invalid", "ratio_at_least and partial_at are fractions from 0 to 1, with partial_at below ratio_at_least.", 0);
            }

            return new RequireRatio(pass, partial);
        }

        private IReadOnlyList<FieldTest> Tests(YamlMappingNode map, string key) => Child(map, key) switch
        {
            null => [],
            YamlSequenceNode list => [.. list.Children.Select(Test)],
            var node => [Test(node)],
        };

        private FieldTest Test(YamlNode node)
        {
            var map = Map(node, "test");
            var operators = map.Children.Keys.OfType<YamlScalarNode>().Where(k => !TestModifiers.Contains(k.Value, StringComparer.Ordinal)).ToArray();
            var unknown = operators.Where(k => !Operators.ContainsKey(k.Value!)).ToArray();
            foreach (var key in unknown)
            {
                Problem<int>(key, "profile.operator_unknown", $"unknown operator '{key.Value}'; use {string.Join(", ", Operators.Keys)}.", 0);
            }

            var known = operators.Except(unknown).ToArray();
            if (known.Length != 1)
            {
                return unknown.Length > 0
                    ? new FieldTest("$", TestOperator.Exists, ["true"])
                    : Problem(map, "profile.operator_missing", $"a test needs exactly one operator ({string.Join(", ", Operators.Keys)}).", new FieldTest("$", TestOperator.Exists, ["true"]));
            }

            var operands = map.Children[known[0]] switch
            {
                YamlScalarNode { Value: { } value } => new[] { value },
                YamlSequenceNode list => [.. list.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "")],
                var other => Problem(other, "profile.value_invalid", $"'{known[0]}' needs a value or a list of values.", Array.Empty<string>()),
            };
            var missing = OptionalText(map, "missing") is { } rule
                ? MissingRules.TryGetValue(rule, out var r) ? r : Problem(map, "profile.value_invalid", "'missing' must be unable_to_assess, pass or fail.", MissingRule.UnableToAssess)
                : MissingRule.UnableToAssess;
            return new FieldTest(RequiredText(map, "field"), Operators[known[0].Value!], operands) { Missing = missing, Negate = Flag(map, "negate") };
        }

        private List<T> Named<T>(YamlMappingNode root, string key, Func<string, YamlNode, T> read) => Child(root, key) switch
        {
            null => [],
            YamlMappingNode map => [.. map.Children.Select(pair => read(((YamlScalarNode)pair.Key).Value ?? "", pair.Value))],
            var other => Problem(other, "profile.section_not_a_map", $"'{key}' must be a map of names.", new List<T>()),
        };

        private List<T> List<T>(YamlMappingNode root, string key, Func<YamlNode, T> read) => Child(root, key) switch
        {
            null => [],
            YamlSequenceNode list => [.. list.Children.Select(read)],
            var other => Problem(other, "profile.section_not_a_list", $"'{key}' must be a list.", new List<T>()),
        };

        private YamlMappingNode Map(YamlNode node, string what) =>
            node as YamlMappingNode ?? Problem(node, "profile.section_not_a_map", $"{what} must be a map of keys.", new YamlMappingNode());

        private string RequiredText(YamlMappingNode map, string key) => Child(map, key) switch
        {
            YamlScalarNode { Value: { Length: > 0 } value } => value,
            null => Problem(map, "profile.key_missing", $"'{key}' is missing.", ""),
            var other => Problem(other, "profile.value_invalid", $"'{key}' must be a non-empty text value.", ""),
        };

        private string? OptionalText(YamlMappingNode map, string key) => Child(map, key) switch
        {
            null => null,
            YamlScalarNode { Value: { Length: > 0 } value } => value,
            YamlScalarNode => null,
            var other => Problem<string?>(other, "profile.value_invalid", $"'{key}' must be a text value.", null),
        };

        private List<string> Texts(YamlMappingNode map, string key) => Child(map, key) switch
        {
            null => [],
            YamlSequenceNode list => [.. list.Children.Select(item => item is YamlScalarNode { Value: { } v } ? v : Problem(item, "profile.value_invalid", $"'{key}' must be a list of values.", ""))],
            var other => Problem(other, "profile.value_invalid", $"'{key}' must be a list.", new List<string>()),
        };

        private Dictionary<string, string> Strings(YamlMappingNode map, string key) => Child(map, key) switch
        {
            null => new(StringComparer.Ordinal),
            YamlMappingNode values => values.Children.ToDictionary(
                pair => ((YamlScalarNode)pair.Key).Value ?? "",
                pair => pair.Value is YamlScalarNode { Value: { } v } ? v : Problem(pair.Value, "profile.value_invalid", $"'{key}.{pair.Key}' must be a text value.", ""),
                StringComparer.Ordinal),
            var other => Problem(other, "profile.value_invalid", $"'{key}' must be a map.", new Dictionary<string, string>(StringComparer.Ordinal)),
        };

        private bool Flag(YamlMappingNode map, string key) => OptionalText(map, key) switch
        {
            null => false,
            var text when bool.TryParse(text, out var value) => value,
            var text => Problem(map, "profile.value_invalid", $"'{key}' must be true or false, not '{text}'.", false),
        };

        private int Whole(YamlMappingNode map, string key, int fallback) => OptionalText(map, key) switch
        {
            null => fallback,
            var text when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) => value,
            var text => Problem(map, "profile.value_invalid", $"'{key}' must be a whole number of 0 or more, not '{text}'.", fallback),
        };

        private double Number(YamlMappingNode map, string key, double fallback) => OptionalText(map, key) switch
        {
            null => fallback,
            var text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0 => value,
            var text => Problem(map, "profile.value_invalid", $"'{key}' must be a number of 0 or more, not '{text}'.", fallback),
        };

        private void Keys(YamlMappingNode map, string[] allowed, string what)
        {
            foreach (var key in map.Children.Keys.Where(k => k is not YamlScalarNode { Value: { } name } || !allowed.Contains(name, StringComparer.Ordinal)))
            {
                Problem<int>(key, "profile.key_unknown", $"unknown key '{key}' in {what}; expected one of {string.Join(", ", allowed)}.", 0);
            }
        }

        private T Missing<T>(YamlMappingNode map, string key, T fallback) => Problem(map, "profile.key_missing", $"'{key}' is missing.", fallback);

        private T Problem<T>(YamlNode at, string code, string message, T fallback)
        {
            _problems.Add(new Problem(code, $"{source} line {at.Start.Line.ToString(CultureInfo.InvariantCulture)}: {message}"));
            return fallback;
        }

        private static YamlNode? Child(YamlMappingNode map, string key) =>
            map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

        /// <summary>A request body: plain numbers, true, false and null keep their JSON type; everything else is text.</summary>
        private static JsonNode? ToJson(YamlNode node) => node switch
        {
            YamlMappingNode map => new JsonObject(map.Children.Select(p => KeyValuePair.Create(((YamlScalarNode)p.Key).Value ?? "", ToJson(p.Value)))),
            YamlSequenceNode list => new JsonArray([.. list.Children.Select(ToJson)]),
            YamlScalarNode { Style: ScalarStyle.Plain, Value: "null" or "~" } => null,
            YamlScalarNode { Style: ScalarStyle.Plain, Value: "true" } => JsonValue.Create(true),
            YamlScalarNode { Style: ScalarStyle.Plain, Value: "false" } => JsonValue.Create(false),
            YamlScalarNode { Style: ScalarStyle.Plain, Value: { } text } when long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole) => JsonValue.Create(whole),
            YamlScalarNode { Style: ScalarStyle.Plain, Value: { } text } when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => JsonValue.Create(number),
            YamlScalarNode scalar => JsonValue.Create(scalar.Value ?? ""),
            _ => null,
        };
    }
}
