using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s15-07: the reports an incident owes, from <c>scope/reporting.yaml</c> and the data kinds of the systems it affects.</summary>
public class ReportingObligationTests
{
    public const string Reporting = """
        obligations:
          - id: dfars-7012
            name: DFARS 252.204-7012 cyber incident report
            report_to: DoD, through the DIBNet portal
            within: 72h
            when: {data: [cui]}
            reference: DFARS 252.204-7012(c)
          - id: ndb-assessment
            name: Notifiable Data Breaches assessment
            report_to: The privacy officer assesses; the OAIC and the people affected are told if it is eligible
            within: 30d
            when: {data: [pii]}
            reference: Privacy Act 1988 s 26WH
          - id: reportcyber
            name: Report to ASD's ACSC through ReportCyber
            report_to: ASD's ACSC
            within: 1d
            when: {always: true}
        """;

    private static readonly string Setup = ScopeFixture.Setup
        .Replace("        owner: it@example.com\n        tags:\n          essentials_domain: identity\n",
            "        owner: it@example.com\n        data:\n          - kind: pii\n            role: stores\n        tags:\n          essentials_domain: identity\n", StringComparison.Ordinal)
        .Replace("        parents: [Example 365]\n", "        parents: [Example 365]\n        data:\n          - kind: cui\n            detail: CUI//SP-CTI\n            role: processes\n", StringComparison.Ordinal);

    private static readonly DateTimeOffset Discovered = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);

    private static (IReadOnlyList<ReportingObligation> Obligations, IReadOnlyList<ScopeProblem> Problems) Parse(string reporting = Reporting) =>
        ScopeLoader.ParseReporting(new ScopeSource("scope/reporting.yaml", reporting));

    private static ScopeSystem[] Systems() =>
        ScopeLoader.Parse(new ScopeSource("scope/setup.yaml", Setup), null, []).Scope.Setup.Domains.SelectMany(d => d.Systems).ToArray();

    private static IncidentFacts Incident(params string[] systems) => new("INC-1", "Ransomware on the file server", Discovered, systems, []);

    [Fact]
    public void Cui_incident_owes_a_72_hour_dfars_report()
    {
        var owed = ReportingObligations.Owed(Parse().Obligations, Systems(), Incident("Example laptops"), Discovered.AddHours(10));

        owed.Select(o => o.Obligation.Id).ShouldBe(["reportcyber", "dfars-7012"]);
        var dfars = owed.Single(o => o.Obligation.Id == "dfars-7012");
        dfars.Due.ShouldBe(Discovered.AddHours(72));
        dfars.Why.ShouldContain("Example laptops handles cui");
        (dfars.Overdue, dfars.ReportedAt).ShouldBe((false, null));
    }

    [Fact]
    public void Pii_breach_owes_an_ndb_assessment_within_30_days()
    {
        var owed = ReportingObligations.Owed(Parse().Obligations, Systems(), Incident("Example 365"), Discovered.AddDays(31));

        var ndb = owed.Single(o => o.Obligation.Id == "ndb-assessment");
        ndb.Due.ShouldBe(Discovered.AddDays(30));
        ndb.Overdue.ShouldBeTrue();
        owed.ShouldNotContain(o => o.Obligation.Id == "dfars-7012");
    }

    [Fact]
    public void A_report_recorded_in_the_timeline_is_made_and_never_overdue()
    {
        var reported = Incident("Example laptops") with { Timeline = [new IncidentEntry(Discovered.AddHours(50), "Reported: dfars-7012, DIBNet report 12345")] };

        var dfars = ReportingObligations.Owed(Parse().Obligations, Systems(), reported, Discovered.AddDays(5)).Single(o => o.Obligation.Id == "dfars-7012");

        (dfars.ReportedAt, dfars.Overdue).ShouldBe((Discovered.AddHours(50), false));
    }

    [Fact]
    public void Obligations_are_checked()
    {
        var (_, problems) = Parse(Reporting
            .Replace("within: 30d", "within: a month", StringComparison.Ordinal)
            .Replace("when: {data: [cui]}", "when: {data: [secret]}", StringComparison.Ordinal)
            .Replace("id: reportcyber", "id: dfars-7012", StringComparison.Ordinal));

        problems.Select(p => p.Code).ShouldBe(["scope.value_invalid", "scope.duration_invalid", "reporting.duplicate"], ignoreOrder: true);
    }
}
