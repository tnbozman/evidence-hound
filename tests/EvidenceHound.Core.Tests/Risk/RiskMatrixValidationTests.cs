using EvidenceHound.Core.Risk;

namespace EvidenceHound.Core.Tests.Risk;

public class RiskMatrixValidationTests
{
    private static RiskLevel Level(string name, int? id = null) => new(id, name, null);

    private static RiskMatrix Matrix(
        IReadOnlyList<RiskLevel>? probability = null, IReadOnlyList<IReadOnlyList<int>>? grid = null, string urn = "urn:example:risk:matrix:example-2x2") =>
        new(
            urn,
            "example-2x2",
            "Example 2x2",
            probability ?? [Level("Unlikely"), Level("Likely")],
            [Level("Minor"), Level("Major")],
            [Level("Low"), Level("High")],
            grid ?? [[0, 0], [0, 1]]);

    [Fact]
    public void A_well_formed_matrix_has_no_problems() =>
        RiskMatrixValidation.Check(Matrix(), "example").ShouldBeEmpty();

    [Fact]
    public void Levels_must_start_at_zero_without_gaps()
    {
        // The library format numbers levels by position; an explicit id must match it, so 1, 2 or 0, 2 is a mistake.
        var problems = RiskMatrixValidation.Check(Matrix(probability: [Level("Unlikely", 1), Level("Likely", 2)]), "example");

        problems.ShouldContain(p => p.Code == "library.matrix_level_gap" && p.Message.Contains("probability") && p.Message.Contains("Unlikely"));
        RiskMatrixValidation.Check(Matrix(probability: [Level("Unlikely", 0), Level("Likely", 1)]), "example").ShouldBeEmpty();
        RiskMatrixValidation.Check(Matrix(probability: [Level("Unlikely", 0), Level("Likely", 2)]), "example")
            .ShouldHaveSingleItem().Code.ShouldBe("library.matrix_level_gap");
    }

    [Fact]
    public void Grid_cell_must_name_a_risk_level()
    {
        var problems = RiskMatrixValidation.Check(Matrix(grid: [[0, 0], [0, 2]]), "example");

        var problem = problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe("library.matrix_grid_unknown_risk");
        problem.Message.ShouldContain("Likely");
        problem.Message.ShouldContain("Major");
    }

    [Fact]
    public void Grid_needs_one_row_per_probability_and_one_cell_per_impact()
    {
        RiskMatrixValidation.Check(Matrix(grid: [[0, 0]]), "example").ShouldContain(p => p.Code == "library.matrix_grid_shape");
        RiskMatrixValidation.Check(Matrix(grid: [[0, 0], [0]]), "example").ShouldContain(p => p.Code == "library.matrix_grid_shape" && p.Message.Contains("Likely"));
    }

    [Fact]
    public void Urn_must_be_a_matrix_urn_of_the_packager()
    {
        RiskMatrixValidation.Check(Matrix(urn: "urn:example:risk:framework:example-2x2"), "example").ShouldContain(p => p.Code == "library.matrix_urn_invalid");
        RiskMatrixValidation.Check(Matrix(urn: "urn:other:risk:matrix:example-2x2"), "example").ShouldContain(p => p.Code == "library.matrix_urn_invalid");
    }
}
