using System.Globalization;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Metric definitions, instances, dashboards and widgets as the plan compares them (s16-02). Pure. Definitions, instances and dashboards are
/// keyed by <c>ref_id</c>, a widget by <c>dashboard/title</c>. An instance's frequency and target fall back to its definition's, as CISO
/// Assistant copies them; a qualitative target is its level's name. Samples are never declared: runs (s16-03) or people enter them.
/// </summary>
public static class MetricObjects
{
    public static IEnumerable<ScopeObject> From(ScopeMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        var definitions = metrics.Definitions.GroupBy(d => d.RefId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var definition in definitions.Values)
        {
            yield return new ScopeObject(ScopeKind.MetricDefinition, definition.RefId, Fields(
                ("name", definition.Name), ("description", definition.Description), ("domain", definition.Domain?.Value), ("unit", definition.Unit),
                ("levels", definition.Levels.Count == 0 ? null : string.Join(", ", definition.Levels)), ("direction", Direction(definition)),
                ("decimals", definition.Decimals?.ToString(CultureInfo.InvariantCulture)), ("frequency", definition.Frequency), ("target", Target(definition.Target, definition)),
                ("expression", definition.Formula?.Expression),
                ("inputs", ScopeDesired.Joined((definition.Formula?.Inputs ?? []).Select(i => $"{i.Key}={i.Definition.Value} {i.Combine}")))));
        }

        foreach (var instance in metrics.Instances.DistinctBy(i => i.RefId, StringComparer.Ordinal))
        {
            var definition = definitions.GetValueOrDefault(instance.Definition.Value);
            yield return new ScopeObject(ScopeKind.MetricInstance, instance.RefId, Fields(
                ("name", instance.Name), ("description", instance.Description), ("definition", instance.Definition.Value), ("domain", instance.Domain?.Value),
                ("frequency", instance.Frequency ?? definition?.Frequency), ("grace_days", instance.GraceDays?.ToString(CultureInfo.InvariantCulture)),
                ("target", Target(instance.Target ?? definition?.Target, definition)), ("owners", ScopeDesired.Joined(instance.Owners.Select(o => o.Value))),
                ("controls", ScopeDesired.Joined(instance.Controls.Select(c => c.Value)))));
        }

        foreach (var dashboard in metrics.Dashboards.DistinctBy(d => d.RefId, StringComparer.Ordinal))
        {
            yield return new ScopeObject(ScopeKind.Dashboard, dashboard.RefId, Fields(("name", dashboard.Name), ("description", dashboard.Description), ("domain", dashboard.Domain?.Value)));
            foreach (var widget in dashboard.Widgets.DistinctBy(w => w.Title, StringComparer.Ordinal))
            {
                yield return new ScopeObject(ScopeKind.DashboardWidget, WidgetKey(dashboard.RefId, widget.Title), Fields(
                    ("dashboard", dashboard.RefId), ("title", widget.Title), ("metric", widget.Metric?.Value),
                    ("builtin", widget.Builtin is { } b ? Builtin(b.Of, b.Target.Value, b.Metric) : null), ("text", widget.Text),
                    ("chart", widget.Chart), ("time_range", widget.TimeRange), ("aggregation", widget.Aggregation),
                    ("position", Position(widget.X, widget.Y, widget.Width, widget.Height))));
            }
        }
    }

    public static string WidgetKey(string dashboard, string title) => $"{dashboard}/{title}";

    /// <summary>A builtin widget's source as the plan shows it: <c>audit EX-E8-2026 progress</c>.</summary>
    public static string Builtin(string of, string target, string metric) => $"{of} {target} {metric}";

    /// <summary>A widget's place on the grid: x, y, width and height.</summary>
    public static string Position(int x, int y, int width, int height) => string.Create(CultureInfo.InvariantCulture, $"{x}, {y}, {width}, {height}");

    /// <summary>A number as the plan compares it (95, not 95.0), so the value CISO Assistant stores as a float reads the same.</summary>
    public static string? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value.ToString(CultureInfo.InvariantCulture) : text;

    /// <summary>Levels are written worst first, so a qualitative metric is higher_is_better unless it says otherwise; a number has no direction unless declared.</summary>
    private static string Direction(ScopeMetricDefinition definition) => definition.Direction ?? (definition.Qualitative ? "higher_is_better" : "none");

    private static string? Target(string? target, ScopeMetricDefinition? definition) => definition?.Qualitative == true ? target : Number(target);

    private static Dictionary<string, string> Fields(params (string Name, string? Value)[] fields) =>
        fields.Where(f => !string.IsNullOrEmpty(f.Value)).ToDictionary(f => f.Name, f => f.Value!, StringComparer.Ordinal);
}
