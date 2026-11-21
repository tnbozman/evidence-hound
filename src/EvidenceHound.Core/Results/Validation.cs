namespace EvidenceHound.Core.Results;

/// <summary>One validation invariant: when <see cref="Holds"/> is false the value is invalid for the reason in <see cref="Problem"/>.</summary>
public readonly record struct Invariant(bool Holds, Problem Problem);

/// <summary>Builds a value only when every invariant holds, collecting every broken invariant otherwise.</summary>
public static class Validation
{
    public static Result<T> Validate<T>(Func<T> create, params ReadOnlySpan<Invariant> invariants)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(create);

        var problems = new List<Problem>();
        foreach (var invariant in invariants)
        {
            if (!invariant.Holds)
            {
                problems.Add(invariant.Problem);
            }
        }

        return problems.Count == 0 ? Result.Ok(create()) : Result.Fail<T>(problems);
    }
}
