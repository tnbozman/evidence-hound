using EvidenceHound.Core.Scope;
using FsCheck.Xunit;

namespace EvidenceHound.Core.Tests.Ssp;

/// <summary>s11-03: each requirement's status, statement, responsible party and evidence are stated the same way every time, and what is missing is a gap.</summary>
public class SspStatementTests
{
    [Theory]
    [InlineData("compliant", null, "Implemented")]
    [InlineData("partially_compliant", null, "Partially implemented")]
    [InlineData("non_compliant", null, "Not implemented")]
    [InlineData("not_assessed", null, "Not implemented")]
    [InlineData(null, null, "Not implemented")]
    [InlineData("non_compliant", "2026-12-31", "Planned")]
    [InlineData("not_assessed", "2026-12-31", "Planned")]
    [InlineData("partially_compliant", "2026-12-31", "Partially implemented")]
    [InlineData("not_applicable", null, "Not applicable")]
    public void Status_follows_requirement_result(string? result, string? eta, string status)
    {
        var row = SspStatements.Complete(Row("ISM-1", result, "Checked.", "AC-A"), Controls(Control("AC-A", "Does A.", "it@example.com", eta)));

        row.Status.Value.ShouldBe(status);
        row.Status.Source.ShouldBe(row.Result.Source);
    }

    [Fact]
    public void Not_applicable_without_justification_is_a_gap()
    {
        var bare = SspStatements.Complete(Row("ISM-1", "not_applicable", null), Controls());
        var justified = SspStatements.Complete(Row("ISM-1", "not_applicable", "No Java on any laptop, by policy."), Controls());

        bare.Gaps.ShouldBe(["not applicable without a justification"]);
        justified.Gaps.ShouldBeEmpty();
        justified.Statement.Value.ShouldBe("No Java on any laptop, by policy.");
    }

    [Fact]
    public void Statement_joins_control_descriptions_in_ref_order()
    {
        var row = SspStatements.Complete(
            Row("ISM-1", "compliant", "Reviewed in March.", "AC-B", "AC-A"),
            Controls(Control("AC-A", "Does A.", "it@example.com"), Control("AC-B", "Does B.", "ceo@example.com")));

        row.Statement.Value.ShouldBe("AC-A: Does A.\nAC-B: Does B.\nReviewed in March.");
        row.Statement.Source.Reference.ShouldBe("applied controls AC-A, AC-B; requirement assessment EX/ISM-1");
        row.Responsible.Value.ShouldBe("ceo@example.com, it@example.com");
        row.Gaps.ShouldBeEmpty();
    }

    [Fact]
    public void A_row_with_no_statement_or_owner_is_a_gap()
    {
        var row = SspStatements.Complete(Row("ISM-1", "compliant", null, "AC-A"), Controls(Control("AC-A", null, null)));

        row.Gaps.ShouldBe(["no implementation statement", "no responsible party"]);
    }

    [Fact]
    public void A_requirement_not_implemented_is_on_the_poam_not_a_gap()
    {
        // Nothing is in place to describe yet; the POA&M carries it. A Planned row still needs someone responsible.
        SspStatements.Complete(Row("ISM-1", "not_assessed", null), Controls()).Gaps.ShouldBeEmpty();
        SspStatements.Complete(Row("ISM-1", "non_compliant", null, "AC-A"), Controls(Control("AC-A", null, null, "2026-12-31"))).Gaps.ShouldBe(["no responsible party"]);
    }

    [Fact]
    public void The_document_lists_its_gaps_by_requirement()
    {
        var document = Document(
            SspStatements.Complete(Row("ISM-2", "not_applicable", null), Controls()),
            SspStatements.Complete(Row("ISM-1", "compliant", "Fine.", "AC-A"), Controls(Control("AC-A", "Does A.", "it@example.com"))));

        document.Gaps.ShouldBe([new SspGap("ISM-2", "not applicable without a justification")]);
    }

    /// <summary>Generating twice from the same inputs gives byte-identical output.</summary>
    [Property(MaxTest = 100)]
    public void Generating_twice_gives_identical_output(byte[] results, byte[] owners)
    {
        string?[] resultCodes = ["compliant", "partially_compliant", "non_compliant", "not_assessed", "not_applicable", null];
        var controls = Controls(Control("AC-A", "Does A.", "it@example.com", "2026-12-31"), Control("AC-B", null, null));
        SspDocument Make() => Document(controls, [.. Enumerable.Range(0, Math.Min(results.Length, owners.Length)).Select(i => SspStatements.Complete(
            Row($"ISM-{i}", resultCodes[results[i] % resultCodes.Length], owners[i] % 3 == 0 ? null : "Seen.", owners[i] % 2 == 0 ? ["AC-A"] : ["AC-B", "AC-A"]),
            controls))]);

        SspJson.Write(Make()).ShouldBe(SspJson.Write(Make()));
        Poam.Markdown(Poam.Of(Make())).ShouldBe(Poam.Markdown(Poam.Of(Make())));
    }

    internal static SspRequirement Row(string refId, string? result, string? observation, params string[] controls)
    {
        var source = new SspSource(SspSource.CisoAssistant, $"requirement assessment EX/{refId}");
        return new SspRequirement(
            new SspValue(refId, source),
            new SspValue(result, source),
            new SspValue(observation, source),
            new SspValue("policy", source),
            new SspValue(controls.Length == 0 ? null : string.Join(", ", controls), source));
    }

    internal static SspControl Control(string refId, string? description, string? owner, string? eta = null, string? status = "in_progress")
    {
        var source = new SspSource(SspSource.CisoAssistant, $"applied control {refId}");
        return new SspControl(new SspValue(refId, source), new SspValue(refId + " name", source), new SspValue(owner, source), new SspValue(status, source), new SspValue(description, source))
        {
            Eta = new SspValue(eta, source),
        };
    }

    internal static IReadOnlyDictionary<string, SspControl> Controls(params SspControl[] controls) => controls.ToDictionary(c => c.RefId.Value!);

    internal static SspDocument Document(params SspRequirement[] rows) => Document(Controls(), rows);

    internal static SspDocument Document(IReadOnlyDictionary<string, SspControl> controls, params SspRequirement[] rows)
    {
        var repo = new SspSource(SspSource.Repo, "scope/ssp.yaml line 2");
        SspValue Repo(string value) => new(value, repo);
        return new SspDocument("essential-eight", "ism", Repo("Example corporate IT"), Repo("0.1"), Repo("urn:x"), Repo("EX"), Repo("Corporate IT"), [], [], [], [.. controls.Values], rows, [], []);
    }
}
