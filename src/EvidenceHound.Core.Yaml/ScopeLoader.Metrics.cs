using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>Reads <c>scope/metrics.yaml</c>: metric definitions, metric instances and dashboards (s16-02).</summary>
public static partial class ScopeLoader
{
    private static readonly string[] MetricsKeys = ["definitions", "instances", "dashboards"];
    private static readonly string[] DefinitionKeys = ["ref_id", "name", "description", "domain", "unit", "levels", "direction", "decimals", "frequency", "target", "formula"];
    private static readonly string[] FormulaKeys = ["expression", "inputs"];
    private static readonly string[] InputKeys = ["key", "definition", "combine"];
    private static readonly string[] InstanceKeys = ["ref_id", "name", "description", "definition", "domain", "frequency", "grace_days", "target", "owners", "controls", "source", "measure", "checks", "audit"];
    private static readonly string[] DashboardKeys = ["ref_id", "name", "description", "domain", "widgets"];
    private static readonly string[] WidgetKeys = ["title", "metric", "builtin", "audit", "risk_study", "domain", "text", "chart", "time_range", "aggregation", "x", "y", "width", "height"];
    private static readonly string[] BuiltinOwners = ["audit", "risk_study", "domain"];

    private static ScopeMetrics ReadMetrics(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, MetricsKeys);
        return new ScopeMetrics(
            [.. file.Items(root, "definitions").Select(item => ReadDefinition(item, file)).OfType<ScopeMetricDefinition>()],
            [.. file.Items(root, "instances").Select(item => ReadInstance(item, file)).OfType<ScopeMetricInstance>()],
            [.. file.Items(root, "dashboards").Select(item => ReadDashboard(item, file)).OfType<ScopeDashboard>()]);
    }

    private static ScopeMetricDefinition? ReadDefinition(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, DefinitionKeys);
        var refId = file.Required(item, "ref_id");
        var name = file.Required(item, "name");
        var formula = file.Map(item, "formula") is { } map ? ReadFormula(map, file) : null;
        return refId is null || name is null
            ? null
            : new ScopeMetricDefinition(
                refId, name, file.Text(item, "description"), file.Ref(item, "domain"), file.Text(item, "unit"), [.. file.Refs(item, "levels").Select(l => l.Value)],
                file.OneOf(item, "direction", [.. ScopeMetrics.Directions], required: false), Whole(item, "decimals", file),
                file.OneOf(item, "frequency", [.. ScopeMetrics.Frequencies], required: false), file.Text(item, "target"), formula, file.At(item));
    }

    private static MetricFormula? ReadFormula(YamlMappingNode map, FileReader file)
    {
        file.Keys(map, FormulaKeys);
        var expression = file.Required(map, "expression");
        var inputs = file.Items(map, "inputs").Select(input =>
        {
            file.Keys(input, InputKeys);
            var key = file.Required(input, "key");
            var definition = file.Ref(input, "definition", required: true);
            var combine = file.OneOf(input, "combine", [.. ScopeMetrics.Combines], required: false) ?? "one";
            return key is null || definition is null ? null : new MetricInput(key, definition, combine, file.At(input));
        }).OfType<MetricInput>().ToArray();
        return expression is null ? null : new MetricFormula(expression, inputs, file.At(map));
    }

    private static ScopeMetricInstance? ReadInstance(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, InstanceKeys);
        var refId = file.Required(item, "ref_id");
        var name = file.Required(item, "name");
        var definition = file.Ref(item, "definition", required: true);
        var source = ReadMetricSource(item, refId, file);
        return refId is null || name is null || definition is null
            ? null
            : new ScopeMetricInstance(
                refId, name, file.Text(item, "description"), definition, file.Ref(item, "domain"), file.OneOf(item, "frequency", [.. ScopeMetrics.Frequencies], required: false),
                Whole(item, "grace_days", file), file.Text(item, "target"), file.Emails(item, "owners"), file.Refs(item, "controls"), file.At(item))
            {
                Source = source,
            };
    }

    /// <summary>s16-03: <c>source: hound</c> with its measure, and the checks or audit the measure reads.</summary>
    private static MetricSource? ReadMetricSource(YamlMappingNode item, string? refId, FileReader file)
    {
        var source = file.OneOf(item, "source", ["hound"], required: false);
        if (source is null)
        {
            var stray = MeasureKeys.Where(k => FileReader.Child(item, k) is not null).ToArray();
            if (stray.Length > 0)
            {
                file.Problem(
                    "metric.measure_invalid",
                    file.At(item),
                    $"metric instance '{refId}' has '{string.Join("', '", stray)}' but no 'source: hound', so no run measures it.",
                    "Add 'source: hound' for a run to fill it in, or remove the measure for people to enter its values.");
            }

            return null;
        }

        var measure = file.OneOf(item, "measure", [.. ScopeMetrics.Measures], required: true);
        var checks = file.Refs(item, "checks");
        foreach (var bad in checks.Where(c => !IsCheckPattern(c.Value)))
        {
            file.Problem("metric.measure_invalid", bad.At, $"'checks' names '{bad.Value}', which is not a check id or pattern.", "Name checks by id, such as identity.mfa.admins, or every check below one, such as identity.mfa.*.");
        }

        return measure is null ? null : new MetricSource(measure, [.. checks.Select(c => c.Value)], file.Ref(item, "audit"), file.At(item));
    }

    private static readonly string[] MeasureKeys = ["measure", "checks", "audit"];

    private static bool IsCheckPattern(string value) =>
        CheckId.Create(value.EndsWith(".*", StringComparison.Ordinal) ? value[..^2] : value) is Results.Result<CheckId>.Ok;

    private static ScopeDashboard? ReadDashboard(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, DashboardKeys);
        var refId = file.Required(item, "ref_id");
        var name = file.Required(item, "name");
        var widgets = file.Items(item, "widgets").Select(w => ReadWidget(w, file)).OfType<ScopeWidget>().ToArray();
        return refId is null || name is null ? null : new ScopeDashboard(refId, name, file.Text(item, "description"), file.Ref(item, "domain"), widgets, file.At(item));
    }

    private static ScopeWidget? ReadWidget(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, WidgetKeys);
        var title = file.Required(item, "title");
        var metric = file.Ref(item, "metric");
        var text = file.Text(item, "text");
        var owners = BuiltinOwners.Where(o => FileReader.Child(item, o) is not null).ToArray();
        var builtin = file.Text(item, "builtin") is { } key && owners is [var of] && file.Ref(item, of) is { } owner ? new WidgetBuiltin(of, owner, key) : null;
        var shown = (metric is null ? 0 : 1) + (FileReader.Child(item, "builtin") is null ? 0 : 1) + (text is null ? 0 : 1);
        if (shown != 1 || (FileReader.Child(item, "builtin") is not null && owners.Length != 1) || (FileReader.Child(item, "builtin") is null && owners.Length > 0))
        {
            file.Problem(
                "dashboard.widget_invalid",
                file.At(item),
                $"widget '{title}' must show one thing: a 'metric', a 'builtin' with one of 'audit', 'risk_study' or 'domain', or 'text'.",
                "For example: {title: MFA coverage, metric: EX-MET-MFA-CORP} or {title: Progress, audit: EX-E8-2026, builtin: progress}.");
            return null;
        }

        var chart = file.OneOf(item, "chart", [.. ScopeWidget.Charts], required: false) ?? (text is null ? "kpi_card" : "text");
        return title is null
            ? null
            : new ScopeWidget(
                title, metric, builtin, text, chart, file.OneOf(item, "time_range", [.. ScopeWidget.TimeRanges], required: false) ?? "last_30_days",
                file.OneOf(item, "aggregation", [.. ScopeWidget.Aggregations], required: false) ?? "none",
                Whole(item, "x", file) ?? 0, Whole(item, "y", file) ?? 0, Whole(item, "width", file) ?? 6, Whole(item, "height", file) ?? 2, file.At(item));
    }
}
