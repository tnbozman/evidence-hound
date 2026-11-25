using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Risk;

/// <summary>
/// Checks a custom risk matrix offline, before CISO Assistant sees it: a matrix URN of the packager, levels numbered from 0 without gaps, and a
/// grid with one row per probability level and one cell per impact level, each naming a risk level. Pure.
/// </summary>
public static class RiskMatrixValidation
{
    public static IReadOnlyList<Problem> Check(RiskMatrix matrix, string packager)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(packager);
        return
        [
            .. Urn(matrix, packager),
            .. Levels(matrix, "probability", matrix.Probability),
            .. Levels(matrix, "impact", matrix.Impact),
            .. Levels(matrix, "risk", matrix.Risk),
            .. Grid(matrix),
        ];
    }

    private static IEnumerable<Problem> Urn(RiskMatrix matrix, string packager)
    {
        var prefix = $"urn:{packager}:risk:matrix:";
        if (!matrix.Urn.StartsWith(prefix, StringComparison.Ordinal) || matrix.Urn.Length == prefix.Length)
        {
            yield return new Problem("library.matrix_urn_invalid", $"risk matrix '{matrix.Urn}' needs a URN of the form {prefix}<id>.");
        }
    }

    private static IEnumerable<Problem> Levels(RiskMatrix matrix, string scale, IReadOnlyList<RiskLevel> levels)
    {
        if (levels.Count == 0)
        {
            yield return new Problem("library.matrix_level_gap", $"risk matrix '{matrix.Urn}' has no {scale} levels.");
        }

        foreach (var (level, position) in levels.Select((l, i) => (l, i)).Where(x => x.l.Id is { } id && id != x.i))
        {
            yield return new Problem(
                "library.matrix_level_gap",
                $"risk matrix '{matrix.Urn}': {scale} level '{level.Name}' is numbered {level.Id} but is at position {position}; number levels from 0 without gaps.");
        }
    }

    private static IEnumerable<Problem> Grid(RiskMatrix matrix)
    {
        if (matrix.Grid.Count != matrix.Probability.Count)
        {
            yield return new Problem(
                "library.matrix_grid_shape",
                $"risk matrix '{matrix.Urn}': the grid has {matrix.Grid.Count} rows but there are {matrix.Probability.Count} probability levels; give one row per probability level.");
        }

        foreach (var (row, p) in matrix.Grid.Select((r, i) => (r, i)).Take(matrix.Probability.Count))
        {
            var probability = matrix.Probability[p].Name;
            if (row.Count != matrix.Impact.Count)
            {
                yield return new Problem(
                    "library.matrix_grid_shape",
                    $"risk matrix '{matrix.Urn}': the row for probability '{probability}' has {row.Count} cells but there are {matrix.Impact.Count} impact levels.");
            }

            foreach (var (risk, i) in row.Select((r, i) => (r, i)).Take(matrix.Impact.Count).Where(x => x.r < 0 || x.r >= matrix.Risk.Count))
            {
                yield return new Problem(
                    "library.matrix_grid_unknown_risk",
                    $"risk matrix '{matrix.Urn}': the cell for probability '{probability}' and impact '{matrix.Impact[i].Name}' is {risk}, which is not a risk level (0 to {matrix.Risk.Count - 1}).");
            }
        }
    }
}
