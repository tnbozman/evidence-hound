using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Reporting;

namespace EvidenceHound.Core.Tests.Reporting;

/// <summary>
/// s15-14: a programme's standing never reads as met while rows are unassessed. A run with a few automated rows compliant, many not
/// applicable and every manual row not assessed showed 100% compliant; a not-assessed row is a gap, in the percentage and in the level.
/// </summary>
public class StandingTests
{
    private static readonly ImplementationGroup[] Levels = [new("1", "Maturity level 1"), new("2", "Maturity level 2")];

    [Fact]
    public void Not_assessed_rows_block_the_level_and_count_as_not_met()
    {
        var programme = Programme(
            [.. Rows(3, ComplianceResult.Compliant, "1", "2"), .. Rows(61, ComplianceResult.NotApplicable, "1", "2"), .. Rows(43, ComplianceResult.NotAssessed, "1", "2")]);

        var standing = Standing.Of(programme);

        standing.Met.ShouldBeNull("no level is met while 43 rows are not assessed");
        (standing.Total, standing.Applicable, standing.Compliant, standing.Assessed, standing.NotAssessed).ShouldBe((107, 46, 3, 64, 43));
        var text = standing.Text();
        text.ShouldContain("Level met: none (level 0)");
        text.ShouldContain("not assessed: 43");
        text.ShouldContain("Compliant 3 of 46 applicable (6%)");
        text.ShouldNotContain("100%");
    }

    [Fact]
    public void A_level_is_met_only_when_every_row_at_it_and_below_is_compliant_or_not_applicable()
    {
        var programme = Programme([.. Rows(5, ComplianceResult.Compliant, "1", "2"), .. Rows(2, ComplianceResult.NotApplicable, "1", "2"), .. Rows(1, ComplianceResult.NotAssessed, "2")]);

        var standing = Standing.Of(programme);

        standing.Met!.RefId.ShouldBe("1");
        standing.Text().ShouldContain("Level met: Maturity level 1");
        standing.Text().ShouldContain("not assessed: 1");
    }

    [Fact]
    public void The_percentage_never_rounds_up_to_100_while_a_row_is_open()
    {
        var programme = Programme([.. Rows(199, ComplianceResult.Compliant, "1"), .. Rows(1, ComplianceResult.NotAssessed, "1")]);

        var text = Standing.Of(programme).Text();

        text.ShouldContain("(99%)");
        text.ShouldNotContain("100%");
        Standing.Of(Programme([.. Rows(4, ComplianceResult.Compliant, "1", "2"), .. Rows(2, ComplianceResult.NotApplicable, "1", "2")])).Text()
            .ShouldContain("Level met: Maturity level 2. Compliant 4 of 4 applicable (100%)");
    }

    private static ProgrammePosture Programme(IReadOnlyList<RequirementPosture> rows) =>
        new("e8", "Essential Eight", "urn:example:framework:e8", 1, "EXAMPLE-E8-2026", Levels, [], rows, []);

    private static IEnumerable<RequirementPosture> Rows(int count, ComplianceResult result, params string[] groups) =>
        Enumerable.Range(0, count).Select(i => new RequirementPosture($"urn:example:req:{result}:{string.Join('-', groups)}:{i}", $"R-{i}", null, groups, result));
}
