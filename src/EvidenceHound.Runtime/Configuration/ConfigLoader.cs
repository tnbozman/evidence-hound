using System.Globalization;
using System.Text.RegularExpressions;
using EvidenceHound.Collectors;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Runtime.Configuration;

/// <summary>
/// Parses and validates a deployment config file (see <c>schemas/config.schema.json</c>), reporting every problem at once.
/// Every problem names the file and line, and none repeats a value that might be a secret.
/// </summary>
public static partial class ConfigLoader
{
    private static readonly string[] TopKeys = ["ciso_assistant", "evidence", "collectors", "target"];
    private static readonly string[] CisoAssistantKeys = ["url", "token", "transport", "kafka_bootstrap_servers"];
    private static readonly string[] EvidenceKeys = ["root", "retention_days"];
    private static readonly string[] CollectorKeys = ["name", "type", "schedule", "settings", "secrets"];
    private static readonly string[] TargetKeys = ["mapping", "compliance_assessment", "controls", "health_control"];
    private static readonly string[] Transports = ["rest", "kafka"];
    private static readonly int NoRetention = -1;

    public static async Task<Result<DeploymentConfig>> LoadAsync(string path, IReadOnlyDictionary<string, CollectorDescription> knownCollectorTypes, CancellationToken cancellationToken) =>
        File.Exists(path)
            ? Parse(await File.ReadAllTextAsync(path, cancellationToken), path, knownCollectorTypes)
            : Result.Fail<DeploymentConfig>(new Problem("config.file_not_found", $"Config file not found: {path}"));

    public static Result<DeploymentConfig> Parse(string yaml, string source, IReadOnlyDictionary<string, CollectorDescription> knownCollectorTypes)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(knownCollectorTypes);

        var at = new Locator(source, Path.GetDirectoryName(Path.GetFullPath(source)) ?? ".");
        return LoadRoot(yaml, at).Bind(root => ParseRoot(root, at, knownCollectorTypes));
    }

    private static Result<YamlMappingNode> LoadRoot(string yaml, Locator at)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            return Result.Fail<YamlMappingNode>(at.Problem(ex.Start, "config.yaml_invalid", $"not valid YAML: {ex.Message}"));
        }

        return stream.Documents.FirstOrDefault()?.RootNode switch
        {
            null or YamlScalarNode { Value: null or "" } => Result.Fail<YamlMappingNode>(new Problem("config.document_empty", $"{at.Source}: the config file is empty.")),
            YamlMappingNode root => Result.Ok(root),
            var other => Result.Fail<YamlMappingNode>(at.Problem(other.Start, "config.document_not_a_map", "the config file must be a map of keys.")),
        };
    }

    private static Result<DeploymentConfig> ParseRoot(YamlMappingNode root, Locator at, IReadOnlyDictionary<string, CollectorDescription> knownTypes)
    {
        var unknown = UnknownKeys(root, TopKeys, at);
        var ca = Section(root, "ciso_assistant", at).Bind(map => ParseCisoAssistant(map, at));
        var evidence = Section(root, "evidence", at).Bind(map => ParseEvidence(map, at));
        var collectors = Required(root, "collectors", at).Bind(node => ParseCollectors(node, at, knownTypes));
        var target = Section(root, "target", at).Bind(map => ParseTarget(map, at));

        Problem[] problems = [.. unknown, .. ProblemsOf(ca), .. ProblemsOf(evidence), .. ProblemsOf(collectors), .. ProblemsOf(target)];
        return problems.Length > 0
            ? Result.Fail<DeploymentConfig>(problems)
            : ca.Bind(c => evidence.Bind(e => collectors.Bind(l => target.Map(t => new DeploymentConfig(c, e, l, t)))));
    }

    private static Result<CisoAssistantConfig> ParseCisoAssistant(YamlMappingNode map, Locator at)
    {
        var url = Scalar(map, "url", at).Bind(node =>
            Uri.TryCreate(node.Value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? Result.Ok(uri)
                : Result.Fail<Uri>(at.Problem(node.Start, "config.url_invalid", "'url' must be an absolute http or https address.")));
        var token = Scalar(map, "token", at).Bind(node => Reference(node, "token", at));
        var transport = OptionalScalar(map, "transport", at).Bind(node => node.Value is "" || Transports.Contains(node.Value, StringComparer.Ordinal)
            ? Result.Ok(node.Value is "" ? "rest" : node.Value!)
            : Result.Fail<string>(at.Problem(node.Start, "config.transport_unknown", $"unknown transport '{node.Value}'; use {string.Join(" or ", Transports)}.")));
        var servers = OptionalScalar(map, "kafka_bootstrap_servers", at).Map(node => node.Value!);
        var kafkaWithoutServers = transport.Match(t => t == "kafka", _ => false) && servers.Match(s => s.Length == 0, _ => false);
        var kafkaReady = kafkaWithoutServers
            ? Result.Fail<Unit>(at.Problem(map.Start, "config.kafka_servers_missing", "transport 'kafka' needs 'kafka_bootstrap_servers'."))
            : Result.Ok(Unit.Value);

        Problem[] problems = [.. UnknownKeys(map, CisoAssistantKeys, at), .. ProblemsOf(url), .. ProblemsOf(token), .. ProblemsOf(transport), .. ProblemsOf(servers), .. ProblemsOf(kafkaReady)];
        return problems.Length > 0
            ? Result.Fail<CisoAssistantConfig>(problems)
            : url.Bind(u => token.Bind(k => transport.Bind(t => servers.Map(s => new CisoAssistantConfig(u, k, t, s.Length > 0 ? s : null)))));
    }

    private static Result<EvidenceConfig> ParseEvidence(YamlMappingNode map, Locator at)
    {
        var root = Scalar(map, "root", at).Map(node => at.FullPath(node.Value!));
        var retention = OptionalScalar(map, "retention_days", at).Bind(node => node.Value switch
        {
            "" => Result.Ok(NoRetention),
            _ when int.TryParse(node.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days > 0 => Result.Ok(days),
            _ => Result.Fail<int>(at.Problem(node.Start, "config.retention_invalid", "'retention_days' must be a whole number of days, 1 or more.")),
        });

        Problem[] problems = [.. UnknownKeys(map, EvidenceKeys, at), .. ProblemsOf(root), .. ProblemsOf(retention)];
        return problems.Length > 0
            ? Result.Fail<EvidenceConfig>(problems)
            : root.Bind(r => retention.Map(days => new EvidenceConfig(r, days == NoRetention ? null : days)));
    }

    private static Result<IReadOnlyList<CollectorInstance>> ParseCollectors(YamlNode node, Locator at, IReadOnlyDictionary<string, CollectorDescription> knownTypes)
    {
        if (node is not YamlSequenceNode list)
        {
            return Result.Fail<IReadOnlyList<CollectorInstance>>(at.Problem(node.Start, "config.collectors_not_a_list", "'collectors' must be a list."));
        }

        var parsed = Result.Combine(list.Children.Select(item => ParseCollector(item, at, knownTypes)));
        var names = list.Children.OfType<YamlMappingNode>().Select(item => Child(item, "name")).OfType<YamlScalarNode>().ToArray();
        var duplicates = names
            .Where((name, index) => names.Take(index).Any(earlier => earlier.Value == name.Value))
            .Select(name => at.Problem(name.Start, "config.collector_name_duplicate", $"collector name '{name.Value}' is already used."));

        Problem[] problems = [.. ProblemsOf(parsed), .. duplicates];
        return problems.Length > 0 ? Result.Fail<IReadOnlyList<CollectorInstance>>(problems) : parsed;
    }

    private static Result<CollectorInstance> ParseCollector(YamlNode item, Locator at, IReadOnlyDictionary<string, CollectorDescription> knownTypes)
    {
        if (item is not YamlMappingNode map)
        {
            return Result.Fail<CollectorInstance>(at.Problem(item.Start, "config.collector_not_a_map", "each collector must be a map with name, type and schedule."));
        }

        var name = Scalar(map, "name", at).Bind(node => CollectorName().IsMatch(node.Value!)
            ? Result.Ok(node.Value!)
            : Result.Fail<string>(at.Problem(node.Start, "config.collector_name_invalid", "'name' must be lowercase letters, digits and hyphens, starting with a letter or digit.")));
        var type = Scalar(map, "type", at).Bind(node => knownTypes.ContainsKey(node.Value!)
            ? Result.Ok(node.Value!)
            : Result.Fail<string>(at.Problem(node.Start, "config.collector_type_unknown", $"unknown collector type '{node.Value}'; known types: {string.Join(", ", knownTypes.Keys.Order(StringComparer.Ordinal))}.")));
        var schedule = Scalar(map, "schedule", at).Bind(node => IsCron(node.Value!)
            ? Result.Ok(node.Value!)
            : Result.Fail<string>(at.Problem(node.Start, "config.schedule_invalid", "'schedule' must be a five-field cron expression, for example \"0 2 * * *\".")));
        var settings = OptionalMap(map, "settings", at).Bind(m => Settings(m, at));
        var secrets = OptionalMap(map, "secrets", at).Bind(m => Secrets(m, at));

        Problem[] problems = [.. UnknownKeys(map, CollectorKeys, at), .. ProblemsOf(name), .. ProblemsOf(type), .. ProblemsOf(schedule), .. ProblemsOf(settings), .. ProblemsOf(secrets)];
        return problems.Length > 0
            ? Result.Fail<CollectorInstance>(problems)
            : name.Bind(n => type.Bind(t => schedule.Bind(s => settings.Bind(v => secrets.Map(r => new CollectorInstance(n, t, s, v, r))))));
    }

    private static Result<TargetConfig> ParseTarget(YamlMappingNode map, Locator at)
    {
        var mapping = Scalar(map, "mapping", at).Map(node => at.FullPath(node.Value!));
        var audit = Scalar(map, "compliance_assessment", at).Map(node => node.Value!);
        var controls = OptionalMap(map, "controls", at).Bind(m => Strings(m, at));
        var health = OptionalScalar(map, "health_control", at).Map(node => node.Value!);

        Problem[] problems = [.. UnknownKeys(map, TargetKeys, at), .. ProblemsOf(mapping), .. ProblemsOf(audit), .. ProblemsOf(controls), .. ProblemsOf(health)];
        return problems.Length > 0
            ? Result.Fail<TargetConfig>(problems)
            : mapping.Bind(m => audit.Bind(a => controls.Bind(c => health.Map(h => new TargetConfig(m, a, c, h.Length > 0 ? h : null)))));
    }

    private static Result<IReadOnlyDictionary<string, string>> Settings(YamlMappingNode map, Locator at) =>
        Strings(map, at).Bind(values =>
        {
            var inline = map.Children
                .Where(pair => pair.Key is YamlScalarNode { Value: { } key } && pair.Value is YamlScalarNode { Value: { } value } && LooksLikeSecret(key, value))
                .Select(pair => at.Problem(
                    pair.Value.Start,
                    "config.secret_inline",
                    $"setting '{pair.Key}' looks like a secret; put a reference (env:NAME or file:/path) under 'secrets' instead."))
                .ToArray();
            return inline.Length > 0 ? Result.Fail<IReadOnlyDictionary<string, string>>(inline) : Result.Ok(values);
        });

    private static Result<IReadOnlyDictionary<string, SecretReference>> Secrets(YamlMappingNode map, Locator at) =>
        Result.Combine(map.Children.Select(pair => pair is { Key: YamlScalarNode { Value: { } key }, Value: YamlScalarNode value }
                ? Reference(value, key, at).Map(reference => KeyValuePair.Create(key, reference))
                : Result.Fail<KeyValuePair<string, SecretReference>>(at.Problem(pair.Key.Start, "config.value_invalid", $"secret '{pair.Key}' must be a text reference."))))
            .Map(pairs => (IReadOnlyDictionary<string, SecretReference>)pairs.ToDictionary(StringComparer.Ordinal));

    private static Result<IReadOnlyDictionary<string, string>> Strings(YamlMappingNode map, Locator at) =>
        Result.Combine(map.Children.Select(pair => pair is { Key: YamlScalarNode { Value: { } key }, Value: YamlScalarNode { Value: { } value } }
                ? Result.Ok(KeyValuePair.Create(key, value))
                : Result.Fail<KeyValuePair<string, string>>(at.Problem(pair.Key.Start, "config.value_invalid", $"'{pair.Key}' must be a text value."))))
            .Map(pairs => (IReadOnlyDictionary<string, string>)pairs.ToDictionary(StringComparer.Ordinal));

    private static Result<SecretReference> Reference(YamlScalarNode node, string key, Locator at) =>
        SecretReference.Parse(node.Value ?? "").Match(
            Result.Ok,
            _ => Result.Fail<SecretReference>(at.Problem(
                node.Start, "config.secret_inline", $"'{key}' must be a secret reference (env:NAME or file:/path), never the secret itself.")));

    /// <summary>A setting is a secret when its name says so, when it holds a private key, or when it holds a long unbroken run of letters and digits.</summary>
    private static bool LooksLikeSecret(string key, string value) =>
        SecretName().IsMatch(key) || value.Contains("PRIVATE KEY", StringComparison.Ordinal) || TokenLike().Matches(value).Any(m => m.Value.Any(char.IsLetter) && m.Value.Any(char.IsDigit));

    private static bool IsCron(string value)
    {
        var fields = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length == 5 && fields.All(field => CronField().IsMatch(field));
    }

    private static Result<YamlMappingNode> Section(YamlMappingNode map, string key, Locator at) =>
        Required(map, key, at).Bind(node => node is YamlMappingNode section
            ? Result.Ok(section)
            : Result.Fail<YamlMappingNode>(at.Problem(node.Start, "config.section_not_a_map", $"'{key}' must be a map of keys.")));

    /// <summary>The map under <paramref name="key"/>, or an empty map when the key is absent or empty.</summary>
    private static Result<YamlMappingNode> OptionalMap(YamlMappingNode map, string key, Locator at) => Child(map, key) switch
    {
        null or YamlScalarNode { Value: null or "" } => Result.Ok(new YamlMappingNode()),
        YamlMappingNode found => Result.Ok(found),
        var other => Result.Fail<YamlMappingNode>(at.Problem(other.Start, "config.section_not_a_map", $"'{key}' must be a map of keys.")),
    };

    private static Result<YamlNode> Required(YamlMappingNode map, string key, Locator at) =>
        Child(map, key) is { } node ? Result.Ok(node) : Result.Fail<YamlNode>(at.Problem(map.Start, "config.key_missing", $"'{key}' is missing."));

    private static Result<YamlScalarNode> Scalar(YamlMappingNode map, string key, Locator at) =>
        Required(map, key, at).Bind(node => node is YamlScalarNode { Value.Length: > 0 } scalar
            ? Result.Ok(scalar)
            : Result.Fail<YamlScalarNode>(at.Problem(node.Start, "config.value_invalid", $"'{key}' must be a non-empty text value.")));

    /// <summary>The text under <paramref name="key"/>, or an empty scalar when the key is absent.</summary>
    private static Result<YamlScalarNode> OptionalScalar(YamlMappingNode map, string key, Locator at) =>
        Child(map, key) is null ? Result.Ok(new YamlScalarNode("")) : Scalar(map, key, at);

    private static IEnumerable<Problem> UnknownKeys(YamlMappingNode map, string[] allowed, Locator at) =>
        map.Children.Keys
            .Where(key => key is not YamlScalarNode { Value: { } name } || !allowed.Contains(name, StringComparer.Ordinal))
            .Select(key => at.Problem(key.Start, "config.key_unknown", $"unknown key '{key}'; expected one of {string.Join(", ", allowed)}."));

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static IReadOnlyList<Problem> ProblemsOf<T>(Result<T> result)
        where T : notnull => result.Match(_ => [], problems => problems);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CollectorName();

    [GeneratedRegex(@"^[0-9A-Za-z*/,\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CronField();

    [GeneratedRegex("(password|passwd|secret|token|api_?key|private_?key|credential)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SecretName();

    [GeneratedRegex("[A-Za-z0-9+/=_]{32,}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenLike();

    private sealed record Locator(string Source, string Folder)
    {
        public string FullPath(string path) => Path.GetFullPath(Path.Combine(Folder, path));

        public Problem Problem(Mark mark, string code, string message) =>
            new(code, $"{Source} line {mark.Line.ToString(CultureInfo.InvariantCulture)}: {message}");
    }
}
