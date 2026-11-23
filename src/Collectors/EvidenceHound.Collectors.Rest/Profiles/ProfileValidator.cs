using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Profiles;

/// <summary>What a profile is validated against: this hound's version and the names of its built-in code checks.</summary>
public sealed record ValidationContext(string HoundVersion, IReadOnlyCollection<string> CodeChecks);

/// <summary>
/// Checks a parsed profile before anything is called, and each collector instance's settings against it. Pure, and reports every
/// problem at once, each with a code.
/// </summary>
public static class ProfileValidator
{
    /// <summary>Instance settings the rest collector reads itself, not the profile.</summary>
    public static IReadOnlyList<string> ReservedSettings { get; } = ["profile", "profiles_path", "include_identities", "require_profile_tests"];

    public static IReadOnlyList<Problem> Validate(Profile profile, ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            .. Duplicates(profile),
            .. Requires(profile.Requires, context.HoundVersion),
            .. Templates(profile),
            .. Paths(profile),
            .. References(profile),
            .. Cycles(profile),
            .. Checks(profile, context),
            .. Hosts(profile),
        ];
    }

    public static IReadOnlyList<Problem> ValidateInstance(Profile profile, IReadOnlyDictionary<string, string> settings, IReadOnlyCollection<string> secrets)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secrets);
        var parameters = profile.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var declaredSecrets = profile.Secrets.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. settings.Keys.Where(k => !parameters.Contains(k) && !ReservedSettings.Contains(k, StringComparer.Ordinal)).Order(StringComparer.Ordinal)
                .Select(k => new Problem("profile.setting_unknown", $"Setting '{k}' is not a parameter of profile '{profile.Name}'; it has {Names(parameters)}.")),
            .. profile.Parameters.Where(p => p.Required && p.Default is null && !settings.ContainsKey(p.Name))
                .Select(p => new Problem("profile.parameter_missing", $"Profile '{profile.Name}' needs setting '{p.Name}': {p.Description}")),
            .. profile.Parameters.Where(p => settings.TryGetValue(p.Name, out var value) && !IsValid(p.Type, value))
                .Select(p => new Problem("profile.parameter_invalid", $"Setting '{p.Name}' must be {Describe(p.Type)}.")),
            .. profile.Secrets.Where(s => s.Required && !secrets.Contains(s.Name))
                .Select(s => new Problem("profile.secret_missing", $"Profile '{profile.Name}' needs secret '{s.Name}': {s.Description}")),
            .. secrets.Where(s => !declaredSecrets.Contains(s)).Order(StringComparer.Ordinal)
                .Select(s => new Problem("profile.secret_unknown", $"Secret '{s}' is not a secret of profile '{profile.Name}'; it has {Names(declaredSecrets)}.")),
        ];
    }

    /// <summary>True when <paramref name="value"/> can be read as <paramref name="type"/>.</summary>
    public static bool IsValid(ParameterType type, string value) => type switch
    {
        ParameterType.WholeNumber => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
        ParameterType.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
        ParameterType.Flag => bool.TryParse(value, out _),
        ParameterType.Duration => Durations.TryParse(value, out _),
        ParameterType.Url => Uri.TryCreate(value, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp),
        _ => true,
    };

    private static string Describe(ParameterType type) => type switch
    {
        ParameterType.WholeNumber => "a whole number",
        ParameterType.Number => "a number",
        ParameterType.Flag => "true or false",
        ParameterType.Duration => "an ISO 8601 duration such as P14D or PT12H",
        ParameterType.Url => "an http or https address",
        ParameterType.List => "a comma-separated list",
        _ => "text",
    };

    private static IEnumerable<Problem> Duplicates(Profile profile)
    {
        var names = profile.AllQueries.Select(q => q.Name).Concat(profile.Datasets.Select(d => d.Name));
        var ids = profile.Checks.Select(c => c.Id);
        return names.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => new Problem("profile.name_duplicate", $"'{g.Key}' names more than one query or dataset."))
            .Concat(ids.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1)
                .Select(g => new Problem("profile.check_duplicate", $"Check '{g.Key}' is declared more than once.")));
    }

    private static IEnumerable<Problem> Requires(string? requires, string houndVersion)
    {
        if (requires is null)
        {
            return [];
        }

        var wanted = Version(requires.TrimStart('>', '=', ' '));
        var have = Version(houndVersion);
        return (wanted, have) switch
        {
            (null, _) => [new Problem("profile.requires_invalid", $"requires.hound '{requires}' must be a version such as \">=1.2\".")],
            ({ } w, { } h) when w > h => [new Problem("profile.requires_newer", $"The profile needs hound {requires}; this is hound {houndVersion}. Update hound or use an older profile.")],
            _ => [],
        };
    }

    private static Version? Version(string text)
    {
        var core = text.Split('+', '-')[0];
        return System.Version.TryParse(core.Contains('.', StringComparison.Ordinal) ? core : core + ".0", out var version) ? version : null;
    }

    /// <summary>Every placeholder names a parameter, a variable or a secret the profile declares; item placeholders only in a <c>for_each</c> query.</summary>
    private static IEnumerable<Problem> Templates(Profile profile)
    {
        var values = profile.Parameters.Select(p => p.Name).Concat(profile.AllQueries.SelectMany(q => q.Vars.Keys)).ToHashSet(StringComparer.Ordinal);
        var secrets = profile.Secrets.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var connection = profile.Connection;
        var texts = new[] { connection.BaseUrl }.Concat(connection.AllowedHosts).Concat(connection.Auth.Texts)
            .Concat(new[] { connection.Tls.ClientCertificate, connection.Tls.PinnedThumbprint }.OfType<string>())
            .Select(t => (Text: t, Where: "connection", Items: false))
            .Concat(profile.AllQueries.SelectMany(q => q.Texts.Select(t => (Text: t, Where: $"query '{q.Name}'", Items: q.ForEach is not null))))
            .Concat(profile.Checks.SelectMany(c => CheckTexts(c).Select(t => (Text: t, Where: $"check '{c.Id}'", Items: false))));
        foreach (var (text, where, items) in texts)
        {
            var parsed = Template.Parse(text);
            if (parsed is Result<IReadOnlyList<TemplatePart>>.Fail failed)
            {
                foreach (var problem in failed.Errors)
                {
                    yield return new Problem(problem.Code, $"{where}: {problem.Message}");
                }

                continue;
            }

            foreach (var reference in Template.References(text))
            {
                var problem = reference.Kind switch
                {
                    ReferenceKind.Value when !values.Contains(reference.Name) => new Problem("profile.parameter_unknown", $"{where}: ${{{reference.Name}}} is not a parameter or a query variable."),
                    ReferenceKind.Secret when !secrets.Contains(reference.Name) => new Problem("profile.secret_unknown", $"{where}: ${{secrets.{reference.Name}}} is not a declared secret."),
                    ReferenceKind.Item when !items => new Problem("profile.item_without_for_each", $"{where}: ${{item...}} can only be used in a query with for_each."),
                    _ => null,
                };
                if (problem is not null)
                {
                    yield return problem;
                }
            }
        }
    }

    private static IEnumerable<string> CheckTexts(Check check) =>
        check.Where.Concat(check.Each is { } each ? [each] : []).SelectMany(t => t.Operands)
            .Concat(check.Require switch { RequireCountAtMost m => [m.Count], RequireCountAtLeast l => [l.Count], _ => [] })
            .Concat(check.Describe is { } describe ? [describe] : [])
            .Concat(check.With.Values);

    private static IEnumerable<Problem> Paths(Profile profile)
    {
        var paths = profile.AllQueries.SelectMany(q => QueryPaths(q).Select(p => (Path: p, Where: $"query '{q.Name}'")))
            .Concat(profile.Datasets.SelectMany(d => DatasetPaths(d).Select(p => (Path: p, Where: $"dataset '{d.Name}'"))))
            .Concat(profile.Checks.SelectMany(c => c.Where.Concat(c.Each is { } e ? [e] : []).Select(t => (Path: t.Field, Where: $"check '{c.Id}'"))));
        return paths.SelectMany(p => JsonPath.Parse(p.Path).Match(_ => [], problems => problems.Select(problem => new Problem(problem.Code, $"{p.Where}: {problem.Message}"))));
    }

    private static IEnumerable<string> QueryPaths(Query query) =>
        new[] { query.Items }.Concat(query.Vars.Values)
            .Concat(query.Paging switch
            {
                NextLinkPaging n => [n.Next],
                PageNumberPaging p => [p.TotalPages],
                CursorPaging c => [c.Cursor],
                OffsetPaging { Total: { } total } => [total],
                _ => [],
            })
            .Concat(query.Async is { } job ? [job.StatusUrl, job.ResultUrl, job.Done.Field] : []);

    private static IEnumerable<string> DatasetPaths(Dataset dataset) =>
        dataset.Joins.SelectMany(j => new[] { j.Left, j.Right })
            .Concat(dataset.Flatten is { } flatten ? [flatten] : [])
            .Concat(dataset.Where.Select(t => t.Field))
            .Concat(dataset.Fields.Values);

    private static IEnumerable<Problem> References(Profile profile)
    {
        var queries = profile.Queries.Select(q => q.Name).ToHashSet(StringComparer.Ordinal);
        var allQueries = profile.AllQueries.Select(q => q.Name).ToHashSet(StringComparer.Ordinal);
        var datasets = profile.Datasets.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        var readable = queries.Union(datasets, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var known = $"queries and datasets: {Names(readable)}";

        foreach (var query in profile.AllQueries.Where(q => q.ForEach is not null && !allQueries.Contains(q.ForEach)))
        {
            yield return new Problem("profile.reference_unknown", $"Query '{query.Name}' has for_each '{query.ForEach}', which names no query.");
        }

        foreach (var dataset in profile.Datasets)
        {
            if (!readable.Contains(dataset.From) || dataset.From == dataset.Name)
            {
                yield return new Problem("profile.reference_unknown", $"Dataset '{dataset.Name}' is from '{dataset.From}', which names no other query or dataset; {known}.");
            }

            foreach (var join in dataset.Joins.Where(j => !readable.Contains(j.With) || j.With == dataset.Name))
            {
                yield return new Problem("profile.reference_unknown", $"Dataset '{dataset.Name}' joins '{join.With}', which names no other query or dataset; {known}.");
            }
        }

        foreach (var check in profile.Checks)
        {
            if (check.Over.Count == 0)
            {
                yield return new Problem("profile.reference_unknown", $"Check '{check.Id}' has no 'over': name the dataset or query it reads.");
            }

            foreach (var over in check.Over.Where(o => !readable.Contains(o)))
            {
                yield return new Problem("profile.reference_unknown", $"Check '{check.Id}' is over '{over}', which names nothing; {known}.");
            }
        }
    }

    /// <summary>Queries wait for the queries whose variables they use and the query they run for each item of; that graph must have no cycle.</summary>
    private static IEnumerable<Problem> Cycles(Profile profile)
    {
        var graph = QueryGraph.Dependencies(profile);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in graph.Keys)
        {
            if (FindCycle(start, graph, []) is { } cycle && cycle.All(reported.Add))
            {
                yield return new Problem("profile.dependency_cycle", $"Queries depend on each other in a circle: {string.Join(" -> ", cycle.Append(cycle[0]))}.");
            }
        }
    }

    private static List<string>? FindCycle(string node, IReadOnlyDictionary<string, IReadOnlyList<string>> graph, List<string> path)
    {
        var at = path.IndexOf(node);
        if (at >= 0)
        {
            return path[at..];
        }

        path.Add(node);
        foreach (var next in graph.GetValueOrDefault(node, []))
        {
            if (FindCycle(next, graph, path) is { } cycle)
            {
                return cycle;
            }
        }

        path.RemoveAt(path.Count - 1);
        return null;
    }

    private static IEnumerable<Problem> Checks(Profile profile, ValidationContext context)
    {
        foreach (var check in profile.Checks)
        {
            if (CheckId.Create(check.Id) is Result<CheckId>.Fail failed)
            {
                yield return new Problem("profile.check_id_invalid", failed.Errors[0].Message);
            }

            if (check.History is { } history && (check.Code is null || !check.Over.Contains(history, StringComparer.Ordinal) || profile.Queries.All(q => q.Name != history)))
            {
                yield return new Problem("profile.history_invalid", $"Check '{check.Id}': history '{history}' must name a query the check is over, and only a code check reads history.");
            }

            if (check.Code is { } code)
            {
                if (!context.CodeChecks.Contains(code, StringComparer.Ordinal))
                {
                    yield return new Problem("profile.code_check_unknown", $"Check '{check.Id}' names code check '{code}', which is not built into this hound; built in: {Names(context.CodeChecks)}.");
                }

                continue;
            }

            if (check.WhenEmpty is null)
            {
                yield return new Problem("profile.when_empty_missing", $"Check '{check.Id}' must say what an empty list means: when_empty: not_applicable, fail or unable_to_assess.");
            }

            if (check.Over.Count > 1)
            {
                yield return new Problem("profile.over_invalid", $"Check '{check.Id}' reads one dataset or query; build a dataset to combine '{string.Join("', '", check.Over)}'.");
            }

            if (check.Each is null && check.Require is RequireAll or RequireAny or RequireNone or RequireRatio)
            {
                yield return new Problem("profile.each_missing", $"Check '{check.Id}' needs 'each': the test applied to every row in scope.");
            }

            foreach (var problem in check.Where.Concat(check.Each is { } e ? [e] : []).SelectMany(t => Operands(check.Id, t)))
            {
                yield return problem;
            }
        }
    }

    /// <summary>Literal operands are checked now; placeholders are checked when an instance fills them in.</summary>
    private static IEnumerable<Problem> Operands(string id, FieldTest test)
    {
        var literal = test.Operands.Where(o => !Template.HasReferences(o)).ToArray();
        var single = test.Operator is not (TestOperator.In or TestOperator.VersionIn);
        if (single && test.Operands.Count != 1)
        {
            yield return new Problem("profile.operand_invalid", $"Check '{id}': '{test.Operator}' takes one value.");
        }

        foreach (var operand in literal)
        {
            var valid = test.Operator switch
            {
                TestOperator.Matches or TestOperator.AnyMatches => IsRegex(operand),
                TestOperator.Within or TestOperator.OlderThan => Durations.TryParse(operand, out _),
                TestOperator.AtLeast or TestOperator.AtMost => double.TryParse(operand, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                TestOperator.Exists => bool.TryParse(operand, out _),
                _ => true,
            };
            if (!valid)
            {
                yield return new Problem("profile.operand_invalid", $"Check '{id}': '{operand}' is not a valid value for {test.Operator}.");
            }
        }
    }

    private static bool IsRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Literal addresses (request paths, the base URL and the token URL) must be on an allowed host, over https. Allowed hosts that are
    /// placeholders count with their parameters' defaults; addresses with a placeholder before the path are checked at run time.
    /// </summary>
    private static IEnumerable<Problem> Hosts(Profile profile)
    {
        var allowed = profile.Connection.AllowedHosts.Select(h => WithDefaults(h, profile.Parameters)).OfType<string>().ToArray();
        var addresses = new[] { (Text: profile.Connection.BaseUrl, Where: "connection base_url") }
            .Concat(profile.Connection.Auth is OAuthClientCredentials oauth ? [(oauth.TokenUrl, "auth token_url")] : [])
            .Concat(profile.AllQueries.Select(q => (Text: q.Path, Where: $"query '{q.Name}'")));
        foreach (var (text, where) in addresses)
        {
            var literal = text.Split("${")[0];
            var scheme = literal.IndexOf("://", StringComparison.Ordinal);
            var hostComplete = !Template.HasReferences(text) || (scheme >= 0 && literal.IndexOf('/', scheme + 3) >= 0);
            if (scheme < 0 || !hostComplete || !Uri.TryCreate(literal, UriKind.Absolute, out var url) || url.Host.Length == 0)
            {
                continue;
            }

            if (!HostRules.IsSecure(url))
            {
                yield return new Problem("profile.host_insecure", $"{where}: {url.GetLeftPart(UriPartial.Authority)} is not https; credentials are only sent over https.");
            }
            else if (!HostRules.IsAllowed(url, allowed))
            {
                yield return new Problem("profile.host_not_allowed", $"{where}: {url.Host} is not in allowed_hosts ({string.Join(", ", profile.Connection.AllowedHosts)}).");
            }
        }
    }

    /// <summary>An allowed host entry with its parameter placeholders filled in from their defaults, or null when one has none.</summary>
    private static string? WithDefaults(string entry, IReadOnlyList<ParameterSpec> parameters)
    {
        var defaults = parameters.Where(p => p.Default is not null).ToDictionary(p => p.Name, p => p.Default!, StringComparer.Ordinal);
        return Template.Parse(entry).Match(
            parts => parts.All(p => p is LiteralPart || (p is ReferencePart { Kind: ReferenceKind.Value } r && defaults.ContainsKey(r.Name)))
                ? string.Concat(parts.Select(p => p is LiteralPart l ? l.Text : defaults[((ReferencePart)p).Name]))
                : null,
            _ => null);
    }

    private static string Names(IEnumerable<string> names) => names.Any() ? string.Join(", ", names.Order(StringComparer.Ordinal)) : "none";
}

/// <summary>ISO 8601 durations, such as <c>P14D</c>, <c>PT12H</c> or <c>P1Y</c>.</summary>
public static class Durations
{
    public static bool TryParse(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith('P'))
        {
            return false;
        }

        try
        {
            duration = XmlConvert.ToTimeSpan(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
