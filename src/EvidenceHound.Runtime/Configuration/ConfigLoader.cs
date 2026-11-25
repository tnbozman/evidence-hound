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
    private static readonly string[] TopKeys = ["ciso_assistant", "evidence", "scheduler", "collectors", "target", "targets", "scope"];
    private static readonly string[] SchedulerKeys = ["time_zone", "jitter_seconds"];
    private static readonly string[] CisoAssistantKeys = ["url", "token", "transport", "kafka_bootstrap_servers"];
    private static readonly string[] EvidenceKeys = ["root", "retention_days", "period_retention_years"];
    private static readonly string[] CollectorKeys = ["name", "type", "schedule", "settings", "secrets"];
    private static readonly string[] TargetKeys = ["mapping", "compliance_assessment", "controls", "health_control", "no_evidence"];
    private static readonly string[] Transports = ["rest", "kafka"];
    private static readonly int NoRetention = -1;
    private static readonly int DefaultPeriodRetentionYears = 7;

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
        var llm = LlmInRunner(root, at).ToArray();
        if (llm.Length > 0)
        {
            return Result.Fail<DeploymentConfig>(llm);
        }

        var unknown = UnknownKeys(root, TopKeys, at);
        var ca = Section(root, "ciso_assistant", at).Bind(map => ParseCisoAssistant(map, at));
        var evidence = Section(root, "evidence", at).Bind(map => ParseEvidence(map, at));
        var collectors = Required(root, "collectors", at).Bind(node => ParseCollectors(node, at, knownTypes));
        var targets = ParseTargets(root, at);
        var scope = OptionalScalar(root, "scope", at).Map(node => node.Value is "" ? "" : at.FullPath(node.Value!));
        var scheduler = Child(root, "scheduler") is null
            ? Result.Ok(SchedulerConfig.Default)
            : Section(root, "scheduler", at).Bind(map => ParseScheduler(map, at));

        Problem[] problems = [.. unknown, .. ProblemsOf(ca), .. ProblemsOf(evidence), .. ProblemsOf(scheduler), .. ProblemsOf(collectors), .. ProblemsOf(targets), .. ProblemsOf(scope)];
        return problems.Length > 0
            ? Result.Fail<DeploymentConfig>(problems)
            : ca.Bind(c => evidence.Bind(e => collectors.Bind(l => targets.Bind(t => scheduler.Bind(s => scope.Map(folder =>
                new DeploymentConfig(c, e, l, t) { Scope = folder.Length > 0 ? folder : null, Scheduler = s }))))));
    }

    /// <summary><c>scheduler</c>: the IANA time zone schedules are read in (default UTC) and the most a run is delayed by (default 300 seconds).</summary>
    private static Result<SchedulerConfig> ParseScheduler(YamlMappingNode map, Locator at)
    {
        var zone = OptionalScalar(map, "time_zone", at).Bind(node => node.Value switch
        {
            "" => Result.Ok(SchedulerConfig.Default.TimeZone),
            _ when TimeZoneInfo.TryFindSystemTimeZoneById(node.Value!, out var found) => Result.Ok(found),
            _ => Result.Fail<TimeZoneInfo>(at.Problem(node.Start, "config.time_zone_unknown", $"unknown time zone '{node.Value}'; use an IANA name such as Australia/Sydney or UTC.")),
        });
        var jitter = OptionalScalar(map, "jitter_seconds", at).Bind(node => node.Value switch
        {
            "" => Result.Ok(SchedulerConfig.Default.MaxJitter),
            _ when int.TryParse(node.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds <= 3600 => Result.Ok(TimeSpan.FromSeconds(seconds)),
            _ => Result.Fail<TimeSpan>(at.Problem(node.Start, "config.jitter_invalid", "'jitter_seconds' must be a whole number of seconds from 0 to 3600.")),
        });

        Problem[] problems = [.. UnknownKeys(map, SchedulerKeys, at), .. ProblemsOf(zone), .. ProblemsOf(jitter)];
        return problems.Length > 0
            ? Result.Fail<SchedulerConfig>(problems)
            : zone.Bind(z => jitter.Map(j => new SchedulerConfig(z, j)));
    }

    /// <summary><c>targets</c>, a list, or a single <c>target</c> read as a list of one; never both, never empty, one target per audit.</summary>
    private static Result<IReadOnlyList<TargetConfig>> ParseTargets(YamlMappingNode root, Locator at)
    {
        var parsed = (Child(root, "target"), Child(root, "targets")) switch
        {
            (not null, { } both) => Result.Fail<IReadOnlyList<TargetConfig>>(at.Problem(both.Start, "config.targets_conflict", "use 'target' for one audit or 'targets' for several, not both.")),
            (null, null) => Result.Fail<IReadOnlyList<TargetConfig>>(at.Problem(root.Start, "config.key_missing", "'targets' (or a single 'target') is missing.")),
            (not null, null) => Section(root, "target", at).Bind(map => ParseTarget(map, at)).Map(t => (IReadOnlyList<TargetConfig>)[t]),
            (null, YamlSequenceNode { Children.Count: 0 } empty) => Result.Fail<IReadOnlyList<TargetConfig>>(at.Problem(empty.Start, "config.targets_empty", "'targets' needs at least one audit.")),
            (null, YamlSequenceNode list) => Result.Combine(list.Children.Select(item => item is YamlMappingNode map
                ? ParseTarget(map, at)
                : Result.Fail<TargetConfig>(at.Problem(item.Start, "config.section_not_a_map", "each target must be a map of keys.")))),
            (null, { } other) => Result.Fail<IReadOnlyList<TargetConfig>>(at.Problem(other.Start, "config.targets_not_a_list", "'targets' must be a list.")),
        };
        return parsed.Bind(targets => targets.GroupBy(t => t.ComplianceAssessmentRefId, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToArray() switch
        {
            [] => Result.Ok(targets),
            var duplicates => Result.Fail<IReadOnlyList<TargetConfig>>(new Problem(
                "config.target_duplicate", $"{at.Source}: audit {string.Join(", ", duplicates)} is targeted more than once; give each audit one target.")),
        });
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

        var years = OptionalScalar(map, "period_retention_years", at).Bind(node => node.Value switch
        {
            "" => Result.Ok(DefaultPeriodRetentionYears),
            _ when int.TryParse(node.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 => Result.Ok(value),
            _ => Result.Fail<int>(at.Problem(node.Start, "config.retention_invalid", "'period_retention_years' must be a whole number of years, 1 or more.")),
        });

        Problem[] problems = [.. UnknownKeys(map, EvidenceKeys, at), .. ProblemsOf(root), .. ProblemsOf(retention), .. ProblemsOf(years)];
        return problems.Length > 0
            ? Result.Fail<EvidenceConfig>(problems)
            : root.Bind(r => retention.Bind(days => years.Map(y => new EvidenceConfig(r, days == NoRetention ? null : days) { PeriodRetentionYears = y })));
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

    /// <summary>Collector types hound no longer has, with what to write instead.</summary>
    private static readonly Dictionary<string, string> RetiredTypes = new(StringComparer.Ordinal)
    {
        ["sophos"] = "use `type: rest` with the setting `profile: sophos-central` (and `profiles_path` if the profiles folder is not the default). "
            + "client_id, supported_firewall_firmware, super_admin_max and the client_secret secret keep their names; tenant_id is not used, "
            + "as the profile needs a tenant credential. See docs/collectors/sophos.md.",
    };

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
            : RetiredTypes.TryGetValue(node.Value!, out var replacement)
                ? Result.Fail<string>(at.Problem(node.Start, "config.collector_type_retired", $"collector type '{node.Value}' was replaced: {replacement}"))
                : Result.Fail<string>(at.Problem(node.Start, "config.collector_type_unknown", $"unknown collector type '{node.Value}'; known types: {string.Join(", ", knownTypes.Keys.Order(StringComparer.Ordinal))}.")));
        var schedule = Scalar(map, "schedule", at).Bind(node => IsCron(node.Value!)
            ? Result.Ok(node.Value!)
            : Result.Fail<string>(at.Problem(node.Start, "config.schedule_invalid", "'schedule' must be a five-field cron expression, for example \"0 2 * * *\".")));
        var settingsMap = OptionalMap(map, "settings", at);
        var secretsMap = OptionalMap(map, "secrets", at);
        var settings = settingsMap.Bind(m => Settings(m, at));
        var secrets = secretsMap.Bind(m => Secrets(m, at));
        var missing = type.Match(
            t => [.. Missing("setting", t, knownTypes[t].Settings, settingsMap, map, at), .. Missing("secret", t, knownTypes[t].Secrets, secretsMap, map, at)],
            _ => Array.Empty<Problem>());

        Problem[] problems =
        [
            .. UnknownKeys(map, CollectorKeys, at), .. ProblemsOf(name), .. ProblemsOf(type), .. ProblemsOf(schedule), .. ProblemsOf(settings), .. ProblemsOf(secrets), .. missing,
        ];
        return problems.Length > 0
            ? Result.Fail<CollectorInstance>(problems)
            : name.Bind(n => type.Bind(t => schedule.Bind(s => settings.Bind(v => secrets.Map(r => new CollectorInstance(n, t, s, ResolvePaths(v, knownTypes[t].Settings, at), r))))));
    }

    /// <summary>Settings the collector describes as paths, resolved against the config file's folder like every other path in the config.</summary>
    private static Dictionary<string, string> ResolvePaths(IReadOnlyDictionary<string, string> values, IReadOnlyList<SettingDescription> described, Locator at)
    {
        var paths = described.Where(d => d.IsPath).Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        return values.ToDictionary(pair => pair.Key, pair => paths.Contains(pair.Key) ? at.FullPath(pair.Value) : pair.Value, StringComparer.Ordinal);
    }

    /// <summary>A problem for each required setting or secret that the collector's section does not give.</summary>
    private static IEnumerable<Problem> Missing(
        string kind, string type, IReadOnlyList<SettingDescription> described, Result<YamlMappingNode> section, YamlMappingNode collector, Locator at) =>
        section.Match(
            given => described
                .Where(d => d.Required && Child(given, d.Name) is null)
                .Select(d => at.Problem(collector.Start, $"config.{kind}_missing", $"collector type '{type}' needs {kind} '{d.Name}'.")),
            _ => []);

    private static Result<TargetConfig> ParseTarget(YamlMappingNode map, Locator at)
    {
        var mapping = Scalar(map, "mapping", at).Map(node => at.FullPath(node.Value!));
        var audit = Scalar(map, "compliance_assessment", at).Map(node => node.Value!);
        var controls = OptionalMap(map, "controls", at).Bind(m => Strings(m, at));
        var health = OptionalScalar(map, "health_control", at).Map(node => node.Value!);

        var noEvidence = OptionalScalar(map, "no_evidence", at).Bind(node => node.Value switch
        {
            "" or "non_compliant" => Result.Ok(EvidenceHound.Core.Mapping.ComplianceResult.NonCompliant),
            "not_assessed" => Result.Ok(EvidenceHound.Core.Mapping.ComplianceResult.NotAssessed),
            var other => Result.Fail<EvidenceHound.Core.Mapping.ComplianceResult>(at.Problem(
                node.Start, "config.no_evidence_invalid", $"no_evidence '{other}' is not non_compliant or not_assessed. A row with no evidence is not compliant; not_assessed leaves it out of CISO Assistant's percentage.")),
        });

        Problem[] problems = [.. UnknownKeys(map, TargetKeys, at), .. ProblemsOf(mapping), .. ProblemsOf(audit), .. ProblemsOf(controls), .. ProblemsOf(health), .. ProblemsOf(noEvidence)];
        return problems.Length > 0
            ? Result.Fail<TargetConfig>(problems)
            : mapping.Bind(m => audit.Bind(a => controls.Bind(c => health.Bind(h => noEvidence.Map(n => new TargetConfig(m, a, c, h.Length > 0 ? h : null) { NoEvidence = n })))));
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

    /// <summary>
    /// A setting is a secret when its name says so, when it holds a private key, or when it holds a long unbroken run of letters and
    /// digits. A setting named as an address or path (<c>token_url</c>, <c>secrets_path</c>) is judged by its name only when its value
    /// is not an address or an absolute path. A path setting (<c>drop_folder</c>, <c>profiles_path</c>) holding a path skips the
    /// long-run rule, as a folder named by a GUID is not a token.
    /// </summary>
    private static bool LooksLikeSecret(string key, string value) =>
        (SecretName().IsMatch(key) && !IsAddress(key, value))
        || value.Contains("PRIVATE KEY", StringComparison.Ordinal)
        || (!IsPath(key, value) && TokenLike().Matches(value).Any(m => m.Value.Any(char.IsLetter) && m.Value.Any(char.IsDigit)));

    private static bool IsPath(string key, string value) =>
        PathName().IsMatch(key) && (value.StartsWith('/') || value.StartsWith("./", StringComparison.Ordinal) || value.StartsWith("../", StringComparison.Ordinal));

    private static bool IsAddress(string key, string value) =>
        (key.EndsWith("_url", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "https" or "http")
        || (key.EndsWith("_path", StringComparison.OrdinalIgnoreCase) && value.StartsWith('/'));

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

    /// <summary>
    /// No model in evidence collection: an <c>llm</c> section, or any value naming the onboarding key, is refused, so the runner never holds a
    /// model key. The model belongs in onboarding.yaml, which only <c>hound onboard</c> reads.
    /// </summary>
    private static IEnumerable<Problem> LlmInRunner(YamlMappingNode root, Locator at)
    {
        const string Hint = "hound never uses a language model, in a run or anywhere else. For model help, set up your coding assistant with 'hound agent setup'.";
        if (root.Children.Keys.OfType<YamlScalarNode>().FirstOrDefault(k => k.Value == "llm") is { } section)
        {
            yield return at.Problem(section.Start, "config.llm_in_runner", $"'llm' is not allowed here: {Hint}");
        }

        foreach (var value in root.AllNodes.OfType<YamlScalarNode>().Where(n => n.Value?.Contains("llm_api_key", StringComparison.OrdinalIgnoreCase) == true))
        {
            yield return at.Problem(value.Start, "config.llm_in_runner", $"this refers to a model key: {Hint}");
        }
    }

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

    [GeneratedRegex("(_path|_folder|_dir|_directory|_root)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PathName();

    private sealed record Locator(string Source, string Folder)
    {
        public string FullPath(string path) => Path.GetFullPath(Path.Combine(Folder, path));

        public Problem Problem(Mark mark, string code, string message) =>
            new(code, $"{Source} line {mark.Line.ToString(CultureInfo.InvariantCulture)}: {message}");
    }
}
