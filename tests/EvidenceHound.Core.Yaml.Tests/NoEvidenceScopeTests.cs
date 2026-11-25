using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// s15-14: CISO Assistant leaves not-assessed rows out of its compliance percentage, so an audit whose manual rows wait read 100%. With no
/// evidence there is no compliance: a row the scope leaves not assessed is applied as non-compliant, its observation saying why.
/// </summary>
public class NoEvidenceScopeTests
{
    private const string Open = """
        assessments:
          - ref: EX-1.2
            status: to_do
            result: not_assessed
            observation: "Manual evidence: the admin MFA review each quarter."
        unlisted:
          result: not_assessed
        """;

    [Fact]
    public void A_row_with_no_evidence_is_applied_non_compliant_and_says_so()
    {
        var rows = Rows(ScopeFixture.Audit.Replace(Closed, Open, StringComparison.Ordinal));

        var manual = rows["EXAMPLE-BASELINE-2026/EX-1.2"];
        (manual["status"], manual["result"]).ShouldBe(("to_do", "non_compliant"));
        manual["observation"].ShouldBe("No evidence yet: Manual evidence: the admin MFA review each quarter.");
        var unlisted = rows["EXAMPLE-BASELINE-2026/EX-1.1"];
        unlisted["result"].ShouldBe("non_compliant");
        unlisted["observation"]!.ShouldStartWith("No evidence yet");
    }

    [Fact]
    public void An_audit_can_opt_out_and_keep_them_not_assessed()
    {
        var rows = Rows(ScopeFixture.Audit.Replace(Closed, Open + "no_evidence: not_assessed\n", StringComparison.Ordinal));

        rows["EXAMPLE-BASELINE-2026/EX-1.2"]["result"].ShouldBe("not_assessed");
        rows["EXAMPLE-BASELINE-2026/EX-1.2"]["observation"].ShouldBe("Manual evidence: the admin MFA review each quarter.");
        rows["EXAMPLE-BASELINE-2026/EX-1.1"]["result"].ShouldBe("not_assessed");
    }

    [Fact]
    public void Not_applicable_and_assessed_rows_are_left_as_written()
    {
        var rows = Rows(ScopeFixture.Audit.Replace("result: not_assessed", "result: not_applicable\n  reason: Out of this system's scope.", StringComparison.Ordinal));

        rows["EXAMPLE-BASELINE-2026/EX-1.2"]["result"].ShouldBe("compliant");
        rows["EXAMPLE-BASELINE-2026/EX-1.1"]["result"].ShouldBe("not_applicable");
    }

    private const string Closed = """
        assessments:
          - ref: EX-1.2
            status: done
            result: compliant
            observation: Reviewed by hand.
        unlisted:
          result: not_assessed
        """;

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Rows(string audit)
    {
        var parsed = ScopeFixture.Parse(audit: audit);
        parsed.Problems.ShouldBeEmpty();
        var inputs = new ScopeInputs(ScopeFixture.Snapshots, [new ScopeTarget("EXAMPLE-BASELINE-2026", [], [], new SourceLine("hound.yaml", 1))], new Dictionary<string, IReadOnlyList<ScopeFile>>());
        return ScopeDesired.From(parsed.Scope, inputs).Where(o => o.Kind == ScopeKind.Assessment).ToDictionary(o => o.Key, o => o.Fields);
    }
}
