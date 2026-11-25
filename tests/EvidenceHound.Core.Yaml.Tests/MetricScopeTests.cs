using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s16-02: metric definitions, metric instances and dashboards in <c>scope/metrics.yaml</c>.</summary>
public class MetricScopeTests
{
    public const string Path = "scope/metrics.yaml";

    public const string Metrics = """
        definitions:
          - ref_id: EX-MET-MFA-COVERED
            name: Accounts with MFA
            unit: users
            direction: higher_is_better
          - ref_id: EX-MET-MFA-TOTAL
            name: Staff accounts
            unit: users
          - ref_id: EX-MET-MFA
            name: MFA coverage
            description: Share of staff accounts that sign in with MFA.
            unit: percentage
            direction: higher_is_better
            decimals: 1
            frequency: monthly
            target: 95
            formula:
              expression: covered / total * 100
              inputs:
                - {key: covered, definition: EX-MET-MFA-COVERED, combine: sum}
                - {key: total, definition: EX-MET-MFA-TOTAL, combine: sum}
          - ref_id: EX-MET-POLICY
            name: Policy maturity
            levels: [Missing, Drafted, Approved, Reviewed]
            target: Approved
        instances:
          - ref_id: EX-MET-MFA-COVERED-CORP
            name: Accounts with MFA
            definition: EX-MET-MFA-COVERED
            domain: Example Pty Ltd
            frequency: monthly
          - ref_id: EX-MET-MFA-TOTAL-CORP
            name: Staff accounts
            definition: EX-MET-MFA-TOTAL
            domain: Example Pty Ltd
            frequency: monthly
          - ref_id: EX-MET-MFA-CORP
            name: MFA coverage
            definition: EX-MET-MFA
            domain: Example Pty Ltd
            frequency: monthly
            grace_days: 5
            target: 95
            owners: [it@example.com]
            controls: [AC-MFA]
          - ref_id: EX-MET-POLICY-CORP
            name: Policy maturity
            definition: EX-MET-POLICY
            domain: Example Pty Ltd
            frequency: quarterly
        dashboards:
          - ref_id: EX-DASH-COMMITTEE
            name: Security committee
            domain: Example Pty Ltd
            widgets:
              - {title: MFA coverage, metric: EX-MET-MFA-CORP, chart: line, time_range: last_year}
              - {title: Baseline progress, audit: EXAMPLE-BASELINE-2026, builtin: progress, x: 6}
        """;

    public static ScopeParse Parse(string metrics = Metrics) =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            metrics: new ScopeSource(Path, metrics));

    private static IReadOnlyList<ScopeProblem> Problems(string metrics = Metrics)
    {
        var parsed = Parse(metrics);
        return [.. parsed.Problems, .. MetricValidator.Validate(parsed.Scope)];
    }

    [Fact]
    public void Definitions_instances_and_dashboards_are_read_and_valid()
    {
        var metrics = Parse().Scope.Metrics;

        metrics.Definitions.Select(d => d.RefId).ShouldBe(["EX-MET-MFA-COVERED", "EX-MET-MFA-TOTAL", "EX-MET-MFA", "EX-MET-POLICY"]);
        var coverage = metrics.Definitions[2];
        (coverage.Unit, coverage.Direction, coverage.Decimals, coverage.Frequency, coverage.Target).ShouldBe(("percentage", "higher_is_better", 1, "monthly", "95"));
        coverage.Formula.ShouldNotBeNull().Inputs.Select(i => (i.Key, i.Definition.Value, i.Combine)).ShouldBe([("covered", "EX-MET-MFA-COVERED", "sum"), ("total", "EX-MET-MFA-TOTAL", "sum")]);
        metrics.Definitions[3].Levels.ShouldBe(["Missing", "Drafted", "Approved", "Reviewed"]);
        var instance = metrics.Instances[2];
        (instance.Definition.Value, instance.Domain?.Value, instance.GraceDays, instance.Target).ShouldBe(("EX-MET-MFA", "Example Pty Ltd", 5, "95"));
        instance.Owners.Select(o => o.Value).ShouldBe(["it@example.com"]);
        instance.Controls.Select(c => c.Value).ShouldBe(["AC-MFA"]);
        var dashboard = metrics.Dashboards.ShouldHaveSingleItem();
        dashboard.Widgets.Select(w => (w.Title, w.Metric?.Value, w.Builtin?.Metric, w.Chart)).ShouldBe([("MFA coverage", "EX-MET-MFA-CORP", null, "line"), ("Baseline progress", null, "progress", "kpi_card")]);
        Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Formula_input_must_be_declared_in_domain()
    {
        var problems = Problems(Metrics
            .Replace("- {key: total, definition: EX-MET-MFA-TOTAL, combine: sum}", "- {key: total, definition: EX-MET-STAFF, combine: sum}", StringComparison.Ordinal)
            .Replace("  - ref_id: EX-MET-MFA-COVERED-CORP\n    name: Accounts with MFA\n    definition: EX-MET-MFA-COVERED\n    domain: Example Pty Ltd\n", "  - ref_id: EX-MET-MFA-COVERED-CORP\n    name: Accounts with MFA\n    definition: EX-MET-MFA-COVERED\n", StringComparison.Ordinal));

        problems.Select(p => p.Code).ShouldBe(["metric.input_unknown", "metric.input_outside_domain"], ignoreOrder: true);
        problems.Single(p => p.Code == "metric.input_unknown").Message.ShouldContain("'EX-MET-STAFF'");
        problems.Single(p => p.Code == "metric.input_outside_domain").Message.ShouldContain("'EX-MET-MFA-CORP'");
        problems.Single(p => p.Code == "metric.input_outside_domain").Message.ShouldContain("'Example Pty Ltd'");
    }

    [Fact]
    public void Levels_must_be_ordered()
    {
        var problems = Problems(Metrics.Replace("levels: [Missing, Drafted, Approved, Reviewed]", "levels: [Missing, Approved, Drafted, Approved]", StringComparison.Ordinal));

        var problem = problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe("metric.levels_unordered");
        problem.Message.ShouldContain("'Approved'");
        Problems(Metrics.Replace("levels: [Missing, Drafted, Approved, Reviewed]", "levels: [Approved]", StringComparison.Ordinal))
            .ShouldHaveSingleItem().Code.ShouldBe("metric.levels_unordered");
    }

    [Fact]
    public void A_target_must_be_within_the_definitions_levels_or_units()
    {
        Problems(Metrics.Replace("target: Approved", "target: Signed", StringComparison.Ordinal)).ShouldHaveSingleItem().Code.ShouldBe("metric.target_invalid");
        Problems(Metrics.Replace("    grace_days: 5\n    target: 95\n", "    grace_days: 5\n    target: 120\n", StringComparison.Ordinal)).ShouldHaveSingleItem().Code.ShouldBe("metric.target_invalid");
        Problems(Metrics.Replace("    decimals: 1\n    frequency: monthly\n    target: 95\n", "    decimals: 1\n    frequency: monthly\n    target: most\n", StringComparison.Ordinal))
            .ShouldHaveSingleItem().Code.ShouldBe("metric.target_invalid");
    }

    [Fact]
    public void A_definition_is_quantitative_with_a_unit_or_qualitative_with_levels()
    {
        Problems(Metrics.Replace("    name: Policy maturity\n    levels: [Missing, Drafted, Approved, Reviewed]\n", "    name: Policy maturity\n    unit: score\n    levels: [Missing, Drafted, Approved, Reviewed]\n", StringComparison.Ordinal))
            .ShouldHaveSingleItem().Code.ShouldBe("metric.kind_ambiguous");
        Problems(Metrics.Replace("    name: Policy maturity\n    levels: [Missing, Drafted, Approved, Reviewed]\n    target: Approved\n", "    name: Policy maturity\n", StringComparison.Ordinal))
            .ShouldHaveSingleItem().Code.ShouldBe("metric.kind_ambiguous");
    }

    [Fact]
    public void Instances_and_widgets_name_what_the_scope_declares()
    {
        var problems = Problems(Metrics
            .Replace("    definition: EX-MET-POLICY\n", "    definition: EX-MET-POLICIES\n", StringComparison.Ordinal)
            .Replace("metric: EX-MET-MFA-CORP", "metric: EX-MET-MFA-ALL", StringComparison.Ordinal)
            .Replace("audit: EXAMPLE-BASELINE-2026, builtin: progress", "audit: EXAMPLE-BASELINE-2026, builtin: velocity", StringComparison.Ordinal)
            .Replace("controls: [AC-MFA]", "controls: [AC-SSO]", StringComparison.Ordinal));

        problems.Select(p => p.Code).ShouldBe(["metric.definition_unknown", "metric.control_unknown", "dashboard.metric_unknown", "dashboard.builtin_unknown"], ignoreOrder: true);
    }

    [Fact]
    public void A_library_definition_is_named_by_urn()
    {
        var problems = Problems(Metrics.Replace("    definition: EX-MET-POLICY\n", "    definition: urn:intuitem:risk:metric_definition:example-maturity\n", StringComparison.Ordinal));

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Desired_objects_carry_what_the_plan_compares()
    {
        var objects = ScopeDesired.From(Parse().Scope).Where(o => o.Kind is ScopeKind.MetricDefinition or ScopeKind.MetricInstance or ScopeKind.Dashboard or ScopeKind.DashboardWidget)
            .ToDictionary(o => (o.Kind, o.Key));

        var coverage = objects[(ScopeKind.MetricDefinition, "EX-MET-MFA")].Fields;
        coverage["unit"].ShouldBe("percentage");
        coverage["direction"].ShouldBe("higher_is_better");
        coverage["inputs"].ShouldBe("covered=EX-MET-MFA-COVERED sum, total=EX-MET-MFA-TOTAL sum");
        coverage["expression"].ShouldBe("covered / total * 100");
        objects[(ScopeKind.MetricDefinition, "EX-MET-POLICY")].Fields["levels"].ShouldBe("Missing, Drafted, Approved, Reviewed");
        objects[(ScopeKind.MetricDefinition, "EX-MET-POLICY")].Fields["direction"].ShouldBe("higher_is_better");
        var instance = objects[(ScopeKind.MetricInstance, "EX-MET-MFA-CORP")];
        (instance.Fields["definition"], instance.Fields["domain"], instance.Fields["target"], instance.Fields["grace_days"], instance.Fields["owners"], instance.Fields["controls"])
            .ShouldBe(("EX-MET-MFA", "Example Pty Ltd", "95", "5", "it@example.com", "AC-MFA"));
        objects[(ScopeKind.Dashboard, "EX-DASH-COMMITTEE")].Fields["name"].ShouldBe("Security committee");
        var widget = objects[(ScopeKind.DashboardWidget, "EX-DASH-COMMITTEE/Baseline progress")].Fields;
        (widget["builtin"], widget["chart"], widget["position"]).ShouldBe(("audit EXAMPLE-BASELINE-2026 progress", "kpi_card", "6, 0, 6, 2"));
    }
}
