using EvidenceHound.Core.Sprs;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s17-05: the methodology's weights live in a data file with its source cited, not in code.</summary>
public class SprsMethodologyTests
{
    private static readonly string File171 = Path.Combine(AppContext.BaseDirectory, "files", "frameworks", "sprs", "dod-assessment-methodology-1.2.1.yaml");

    [Fact]
    public void Weights_come_from_the_data_file()
    {
        var methodology = SprsMethodologyReader.Parse(File.ReadAllText(File171), "frameworks/sprs/dod-assessment-methodology-1.2.1.yaml").Value();

        methodology.Version.ShouldBe("1.2.1");
        methodology.Start.ShouldBe(110);
        methodology.Weights.Count.ShouldBe(110);
        (methodology.Start - methodology.Weights.Sum(w => w.Points ?? 0)).ShouldBe(-203, "the methodology's lowest possible score");
        methodology.Weights.Single(w => w.Requirement == "3.5.3").Partial.ShouldBe(new SprsPartial(3, "MFA is implemented for remote and privileged users, but not for general users"));
        methodology.Weights.Single(w => w.Requirement == "3.12.4").Points.ShouldBeNull();
        methodology.Weights.Single(w => w.Requirement == "3.1.16").NotApplicableWhen.ShouldBe("wireless access is not permitted");
    }

    [Fact]
    public void A_weight_outside_5_3_and_1_is_refused_with_its_line()
    {
        const string yaml = """
            methodology: NIST SP 800-171 DoD Assessment Methodology
            version: 1.2.1
            published: "2020-06-24"
            start: 110
            requirements:
              - {ref: "3.1.1", points: 4}
            """;

        var problem = SprsMethodologyReader.Parse(yaml, "weights.yaml").Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("sprs.points_invalid");
        problem.Message.ShouldContain("line 6");
    }
}
