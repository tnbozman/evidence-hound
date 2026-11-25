namespace EvidenceHound.Core.Scope;

/// <summary><c>scope/metrics.yaml</c> (s16-02): what the organisation measures, its targets and owners, and the dashboards that show it.</summary>
public sealed record ScopeMetrics(IReadOnlyList<ScopeMetricDefinition> Definitions, IReadOnlyList<ScopeMetricInstance> Instances, IReadOnlyList<ScopeDashboard> Dashboards)
{
    public static ScopeMetrics None { get; } = new([], [], []);

    /// <summary>The units CISO Assistant ships for metric definitions. Another unit must be added to CISO Assistant's terminologies first.</summary>
    public static IReadOnlyList<string> Units { get; } = ["count", "users", "bytes", "percentage", "score", "days", "hours", "events per second"];

    /// <summary>How often a value is collected, by its period.</summary>
    public static IReadOnlyList<string> Frequencies { get; } = ["15min", "hourly", "daily", "weekly", "monthly", "quarterly", "yearly"];

    /// <summary>Which way is better: up, down, or neither (the value is informational).</summary>
    public static IReadOnlyList<string> Directions { get; } = ["higher_is_better", "lower_is_better", "none"];

    /// <summary>How a formula combines the values of an input's instances in the domain.</summary>
    public static IReadOnlyList<string> Combines { get; } = ["one", "sum", "avg", "min", "max", "count"];

    /// <summary>What a run can measure for an instance with <c>source: hound</c> (s16-03), each a percentage.</summary>
    public static IReadOnlyList<string> Measures { get; } = ["check_pass_rate", "requirements_compliant_pct", "evidence_freshness_pct", "collector_success_rate"];
}

/// <summary>
/// A metric definition: what is measured. Quantitative with a <see cref="Unit"/>, or qualitative with <see cref="Levels"/> written worst
/// first. A computed metric has a <see cref="Formula"/> over other definitions, read per period from their instances in its domain.
/// </summary>
public sealed record ScopeMetricDefinition(
    string RefId, string Name, string? Description, ScopeRef? Domain, string? Unit, IReadOnlyList<string> Levels, string? Direction, int? Decimals,
    string? Frequency, string? Target, MetricFormula? Formula, SourceLine At)
{
    public bool Qualitative => Levels.Count > 0;
}

/// <summary>A formula: a CEL expression over named inputs, each another definition (by ref_id or library URN) and how its instances combine.</summary>
public sealed record MetricFormula(string Expression, IReadOnlyList<MetricInput> Inputs, SourceLine At);

public sealed record MetricInput(string Key, ScopeRef Definition, string Combine, SourceLine At);

/// <summary>
/// A metric instance: one definition measured in one domain (the root folder when it names none), how often, its target, who owns it and the
/// applied controls it tells about. A definition named by URN comes from a library loaded in CISO Assistant.
/// </summary>
public sealed record ScopeMetricInstance(
    string RefId, string Name, string? Description, ScopeRef Definition, ScopeRef? Domain, string? Frequency, int? GraceDays, string? Target,
    IReadOnlyList<ScopeRef> Owners, IReadOnlyList<ScopeRef> Controls, SourceLine At)
{
    /// <summary>What each run measures for it (s16-03), when it declares <c>source: hound</c>; otherwise people or a formula fill it in.</summary>
    public MetricSource? Source { get; init; }
}

/// <summary>
/// A measure a run computes for a metric instance (s16-03): <see cref="Measure"/> is one of <see cref="ScopeMetrics.Measures"/>,
/// <see cref="Checks"/> are check ids or patterns such as <c>identity.mfa.*</c>, and <see cref="Audit"/> names the audit for
/// <c>requirements_compliant_pct</c>.
/// </summary>
public sealed record MetricSource(string Measure, IReadOnlyList<string> Checks, ScopeRef? Audit, SourceLine At);

/// <summary>A dashboard and its widgets, by <c>ref_id</c>, in a domain (the root folder when it names none).</summary>
public sealed record ScopeDashboard(string RefId, string Name, string? Description, ScopeRef? Domain, IReadOnlyList<ScopeWidget> Widgets, SourceLine At);

/// <summary>
/// A dashboard widget, keyed by its title on its dashboard: a metric instance's values, a builtin metric of an audit, risk study or domain, or
/// text. Its place on the 12-column grid is <see cref="X"/>, <see cref="Y"/>, <see cref="Width"/> and <see cref="Height"/>.
/// </summary>
public sealed record ScopeWidget(
    string Title, ScopeRef? Metric, WidgetBuiltin? Builtin, string? Text, string Chart, string TimeRange, string Aggregation, int X, int Y, int Width, int Height, SourceLine At)
{
    public static IReadOnlyList<string> Charts { get; } = ["kpi_card", "donut", "pie", "bar", "line", "area", "gauge", "sparkline", "small_multiples", "table", "text"];

    public static IReadOnlyList<string> TimeRanges { get; } = ["last_hour", "last_24_hours", "last_7_days", "last_30_days", "last_90_days", "last_year", "all_time"];

    public static IReadOnlyList<string> Aggregations { get; } = ["none", "avg", "sum", "min", "max", "count", "last"];
}

/// <summary>A builtin metric CISO Assistant computes for an object: <see cref="Of"/> is <c>audit</c>, <c>risk_study</c> or <c>domain</c>.</summary>
public sealed record WidgetBuiltin(string Of, ScopeRef Target, string Metric)
{
    /// <summary>The builtin metrics of each kind of object, as CISO Assistant names them.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Metrics { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["audit"] = ["progress", "score", "total_requirements", "status_breakdown", "result_breakdown"],
        ["risk_study"] = ["total_scenarios", "treatment_breakdown", "current_level_breakdown", "residual_level_breakdown", "qualifications_breakdown"],
        ["domain"] =
        [
            "controls_status_breakdown", "controls_category_breakdown", "incidents_severity_breakdown", "incidents_status_breakdown", "incidents_detection_breakdown",
            "incidents_qualifications_breakdown", "total_controls", "total_incidents", "task_templates_status_breakdown", "security_exceptions_status_breakdown",
            "security_exceptions_severity_breakdown", "total_security_exceptions", "total_risk_acceptances", "total_frameworks_in_use",
            "risk_scenarios_qualifications_breakdown", "total_assets", "assets_type_breakdown", "total_evidence", "evidence_status_breakdown",
            "total_vulnerabilities", "vulnerabilities_severity_breakdown", "vulnerabilities_status_breakdown", "controls_no_status", "controls_eta_missed",
            "audits_avg_assessed", "non_compliant_items", "partially_compliant_items", "total_risk_scenarios", "risk_scenarios_high_current",
            "risk_scenarios_high_residual", "incidents_open", "findings_open", "evidence_expired", "tasks_overdue", "task_templates_overdue",
            "controls_eta_breakdown", "controls_priority_breakdown", "total_audits", "audits_status_breakdown", "audits_avg_progress", "projects_total",
            "projects_status_breakdown",
        ],
    };

    /// <summary>CISO Assistant's model for each kind of object.</summary>
    public static IReadOnlyDictionary<string, string> Models { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["audit"] = "ComplianceAssessment",
        ["risk_study"] = "RiskAssessment",
        ["domain"] = "Folder",
    };
}
