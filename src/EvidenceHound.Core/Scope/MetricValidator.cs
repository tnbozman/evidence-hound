using System.Globalization;
using System.Text.RegularExpressions;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Checks <c>scope/metrics.yaml</c> offline (s16-02). A definition is quantitative with a unit or qualitative with levels written worst first,
/// and its target is a level or a number its unit allows. A formula's inputs are declared definitions (or library URNs), and every computed
/// instance has an instance of each input in its own domain, or below it when it sits in the root folder, because CISO Assistant reads an
/// input from the instance's folder down. Instances name declared definitions and controls; widgets name declared instances and objects. Pure.
/// </summary>
public static partial class MetricValidator
{
    private static readonly int MaxDecimals = 6;
    private static readonly string[] ReservedKeys = ["previous", "now", "today", "metrics"];

    public static IReadOnlyList<ScopeProblem> Validate(ScopeDocument scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var metrics = scope.Metrics;
        if (metrics.Definitions.Count == 0 && metrics.Instances.Count == 0 && metrics.Dashboards.Count == 0)
        {
            return [];
        }

        var definitions = metrics.Definitions.GroupBy(d => d.RefId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var domains = scope.Setup.Domains.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. Duplicates(metrics.Definitions, d => d.RefId, d => d.At, "metric.duplicate", "metric definition"),
            .. Duplicates(metrics.Instances, i => i.RefId, i => i.At, "metric.duplicate", "metric instance"),
            .. Duplicates(metrics.Dashboards, d => d.RefId, d => d.At, "dashboard.duplicate", "dashboard"),
            .. metrics.Definitions.SelectMany(d => Definition(d, definitions, domains)),
            .. metrics.Instances.SelectMany(i => Instance(i, definitions, scope, domains)),
            .. metrics.Dashboards.SelectMany(d => Dashboard(d, scope, domains)),
        ];
    }

    private static IEnumerable<ScopeProblem> Definition(ScopeMetricDefinition definition, Dictionary<string, ScopeMetricDefinition> definitions, HashSet<string> domains)
    {
        var name = $"metric definition '{definition.RefId}'";
        if (Domain(definition.Domain, domains, name) is { } unknown)
        {
            yield return unknown;
        }

        if ((definition.Unit is null) == (definition.Levels.Count == 0))
        {
            yield return new ScopeProblem(
                "metric.kind_ambiguous",
                definition.At,
                $"{name} must be quantitative with a 'unit' or qualitative with 'levels', and not both.",
                "Give a number's unit, such as percentage or count, or list the levels worst first, such as [Missing, Drafted, Approved].");
        }

        if (definition.Unit is { } unit && !ScopeMetrics.Units.Contains(unit, StringComparer.Ordinal))
        {
            yield return new ScopeProblem(
                "metric.unit_unknown",
                definition.At,
                $"{name} has unit '{unit}', which CISO Assistant does not ship.",
                $"Use one of {string.Join(", ", ScopeMetrics.Units)}, or add the unit to CISO Assistant's metric unit terminologies first.")
            {
                Warning = true,
            };
        }

        if (definition.Qualitative && (definition.Levels.Count < 2 || definition.Levels.Distinct(StringComparer.Ordinal).Count() != definition.Levels.Count))
        {
            var repeated = definition.Levels.GroupBy(l => l, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => $"'{g.Key}'");
            yield return new ScopeProblem(
                "metric.levels_unordered",
                definition.At,
                definition.Levels.Count < 2
                    ? $"{name} has one level, so there is nothing to order."
                    : $"{name} lists {string.Join(", ", repeated)} more than once, so its levels have no order.",
                "List two or more different levels, worst first, such as [Missing, Drafted, Approved, Reviewed].");
        }

        if (definition.Qualitative && definition.Direction is "lower_is_better")
        {
            yield return new ScopeProblem(
                "metric.levels_unordered",
                definition.At,
                $"{name} lists its levels worst first, so a higher level is better, but it says lower_is_better.",
                "Leave 'direction' out (or say higher_is_better) and keep the levels worst first.");
        }

        if (definition.Decimals > MaxDecimals)
        {
            yield return new ScopeProblem("metric.decimals_invalid", definition.At, $"{name} shows {definition.Decimals} decimals; CISO Assistant shows at most {MaxDecimals}.", "Use 0 to 6 decimals.");
        }

        if (Target(definition.Target, definition, name, definition.At) is { } target)
        {
            yield return target;
        }

        foreach (var problem in Formula(definition, definitions, name))
        {
            yield return problem;
        }
    }

    private static IEnumerable<ScopeProblem> Formula(ScopeMetricDefinition definition, Dictionary<string, ScopeMetricDefinition> definitions, string name)
    {
        if (definition.Formula is not { } formula)
        {
            yield break;
        }

        if (formula.Inputs.Count == 0)
        {
            yield return new ScopeProblem("metric.input_unknown", formula.At, $"{name} has a formula with no inputs.", "List the metrics the expression reads under 'inputs', each with a key and a definition.");
        }

        foreach (var input in formula.Inputs)
        {
            if (!InputKey().IsMatch(input.Key) || ReservedKeys.Contains(input.Key, StringComparer.Ordinal) || formula.Inputs.Count(i => i.Key == input.Key) > 1)
            {
                yield return new ScopeProblem(
                    "metric.input_invalid",
                    input.At,
                    $"{name} has input key '{input.Key}', which is not a unique name of letters, digits and _, or is one of {string.Join(", ", ReservedKeys)}.",
                    "Name each input once, such as covered or total.");
            }

            if (IsUrn(input.Definition.Value))
            {
                continue;
            }

            if (!definitions.TryGetValue(input.Definition.Value, out var read))
            {
                yield return new ScopeProblem(
                    "metric.input_unknown",
                    input.Definition.At,
                    $"{name} reads '{input.Definition.Value}', which metrics.yaml does not declare.",
                    "Declare that definition and an instance of it in the same domain, or name a library definition by its URN.");
            }
            else if (read.Qualitative)
            {
                yield return new ScopeProblem("metric.input_invalid", input.Definition.At, $"{name} reads '{read.RefId}', which has levels; a formula reads numbers only.", "Read a quantitative metric.");
            }
            else if (read.RefId == definition.RefId)
            {
                yield return new ScopeProblem("metric.input_invalid", input.Definition.At, $"{name} reads itself.", "Read other metrics.");
            }
        }
    }

    private static IEnumerable<ScopeProblem> Instance(ScopeMetricInstance instance, Dictionary<string, ScopeMetricDefinition> definitions, ScopeDocument scope, HashSet<string> domains)
    {
        var name = $"metric instance '{instance.RefId}'";
        if (Domain(instance.Domain, domains, name) is { } unknown)
        {
            yield return unknown;
        }

        var controls = scope.Controls.Select(c => c.RefId).ToHashSet(StringComparer.Ordinal);
        foreach (var control in instance.Controls.Where(c => !controls.Contains(c.Value)))
        {
            yield return new ScopeProblem("metric.control_unknown", control.At, $"{name} tells about control '{control.Value}', which controls.yaml does not declare.", "Use a control's ref_id from controls.yaml.");
        }

        if (IsUrn(instance.Definition.Value))
        {
            yield break;
        }

        if (!definitions.TryGetValue(instance.Definition.Value, out var definition))
        {
            yield return new ScopeProblem(
                "metric.definition_unknown",
                instance.Definition.At,
                $"{name} measures '{instance.Definition.Value}', which metrics.yaml does not declare.",
                "Declare the definition under 'definitions', or name a library definition by its URN.");
            yield break;
        }

        if (Target(instance.Target, definition, name, instance.At) is { } target)
        {
            yield return target;
        }

        var where = instance.Domain?.Value;
        foreach (var input in definition.Formula?.Inputs ?? [])
        {
            if (IsUrn(input.Definition.Value) || !definitions.ContainsKey(input.Definition.Value))
            {
                continue;
            }

            // An instance in the root folder reads every domain below it; one in a domain reads only that domain.
            var found = scope.Metrics.Instances.Any(i => i.Definition.Value == input.Definition.Value && (where is null || i.Domain?.Value == where));
            if (!found)
            {
                yield return new ScopeProblem(
                    "metric.input_outside_domain",
                    instance.At,
                    $"computed {name} in '{where ?? scope.Setup.RootFolder}' reads '{input.Key}' ({input.Definition.Value}), which has no instance in that domain or below it.",
                    $"Declare an instance of {input.Definition.Value} in '{where ?? scope.Setup.RootFolder}', or move this instance to the root folder so it reads every domain.");
            }
        }
    }

    private static IEnumerable<ScopeProblem> Dashboard(ScopeDashboard dashboard, ScopeDocument scope, HashSet<string> domains)
    {
        var name = $"dashboard '{dashboard.RefId}'";
        if (Domain(dashboard.Domain, domains, name) is { } unknown)
        {
            yield return unknown;
        }

        var instances = scope.Metrics.Instances.Select(i => i.RefId).ToHashSet(StringComparer.Ordinal);
        foreach (var duplicate in dashboard.Widgets.GroupBy(w => w.Title, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1)))
        {
            yield return new ScopeProblem("dashboard.widget_duplicate", duplicate.At, $"{name} has more than one widget titled '{duplicate.Title}'.", "Give each widget its own title: it is how hound finds the widget again.");
        }

        foreach (var widget in dashboard.Widgets)
        {
            if (widget.Metric is { } metric && !instances.Contains(metric.Value))
            {
                yield return new ScopeProblem("dashboard.metric_unknown", metric.At, $"{name} shows metric instance '{metric.Value}', which metrics.yaml does not declare.", "Use an instance's ref_id from 'instances'.");
            }

            if (widget.Builtin is { } builtin)
            {
                if (!WidgetBuiltin.Metrics[builtin.Of].Contains(builtin.Metric, StringComparer.Ordinal))
                {
                    yield return new ScopeProblem(
                        "dashboard.builtin_unknown",
                        widget.At,
                        $"{name} shows builtin '{builtin.Metric}', which CISO Assistant does not compute for a {builtin.Of.Replace('_', ' ')}.",
                        $"Use one of {string.Join(", ", WidgetBuiltin.Metrics[builtin.Of])}.");
                }

                if (!Declares(scope, builtin))
                {
                    yield return new ScopeProblem(
                        "dashboard.object_unknown",
                        builtin.Target.At,
                        $"{name} shows a builtin of {builtin.Of.Replace('_', ' ')} '{builtin.Target.Value}', which the scope does not declare.",
                        "Name an audit's or risk study's ref_id, or a domain from setup.yaml.");
                }
            }

            if (widget.X > 11 || widget.Width is < 1 or > 12 || widget.X + widget.Width > 12 || widget.Height < 1)
            {
                yield return new ScopeProblem(
                    "dashboard.widget_outside_grid",
                    widget.At,
                    $"{name} places widget '{widget.Title}' outside the 12-column grid (x {widget.X}, width {widget.Width}, height {widget.Height}).",
                    "Use x from 0 to 11, a width from 1 to 12 with x + width at most 12, and a height of 1 or more.");
            }
        }
    }

    private static bool Declares(ScopeDocument scope, WidgetBuiltin builtin) => builtin.Of switch
    {
        "audit" => scope.Audits.Any(a => a.RefId == builtin.Target.Value),
        "risk_study" => scope.RiskStudies.Any(r => r.RefId == builtin.Target.Value),
        _ => scope.Setup.Domains.Any(d => d.Name == builtin.Target.Value) || scope.Setup.RootFolder == builtin.Target.Value,
    };

    /// <summary>A target must be one of a qualitative definition's levels, or a number, from 0 to 100 for a percentage.</summary>
    private static ScopeProblem? Target(string? target, ScopeMetricDefinition definition, string name, SourceLine at)
    {
        if (target is null)
        {
            return null;
        }

        if (definition.Qualitative)
        {
            return definition.Levels.Contains(target, StringComparer.Ordinal)
                ? null
                : new ScopeProblem("metric.target_invalid", at, $"{name} targets '{target}', which is not a level of '{definition.RefId}'.", $"Target one of {string.Join(", ", definition.Levels)}.");
        }

        if (!double.TryParse(target, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsNaN(number) || double.IsInfinity(number))
        {
            return new ScopeProblem("metric.target_invalid", at, $"{name} targets '{target}', which is not a number.", "Write the target as a number, such as 95.");
        }

        return definition.Unit == "percentage" && number is < 0 or > 100
            ? new ScopeProblem("metric.target_invalid", at, $"{name} targets {target}%, which is outside 0 to 100.", "Write a percentage from 0 to 100.")
            : null;
    }

    private static ScopeProblem? Domain(ScopeRef? domain, HashSet<string> domains, string name) =>
        domain is null || domains.Contains(domain.Value)
            ? null
            : new ScopeProblem("metric.domain_unknown", domain.At, $"{name} names domain '{domain.Value}', which setup.yaml does not declare.", "Use a domain from setup.yaml, or leave it out for the root folder.");

    private static IEnumerable<ScopeProblem> Duplicates<T>(IEnumerable<T> items, Func<T, string> key, Func<T, SourceLine> at, string code, string what) =>
        items.GroupBy(key, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1))
            .Select(i => new ScopeProblem(code, at(i), $"{what} '{key(i)}' is declared more than once.", "Give each a different ref_id."));

    private static bool IsUrn(string value) => value.StartsWith("urn:", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex InputKey();
}
