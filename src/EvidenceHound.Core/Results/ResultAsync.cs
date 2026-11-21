namespace EvidenceHound.Core.Results;

/// <summary>Bind for steps that do I/O, so a pipeline of awaited steps reads top to bottom and stops at the first failure.</summary>
public static class ResultAsync
{
    public static Task<Result<TOut>> BindAsync<T, TOut>(this Result<T> result, Func<T, Task<Result<TOut>>> bind)
        where T : notnull
        where TOut : notnull
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);
        return result.Match(bind, problems => Task.FromResult(Result.Fail<TOut>(problems)));
    }

    public static async Task<Result<TOut>> BindAsync<T, TOut>(this Task<Result<T>> pending, Func<T, Task<Result<TOut>>> bind)
        where T : notnull
        where TOut : notnull
    {
        ArgumentNullException.ThrowIfNull(pending);
        return await (await pending).BindAsync(bind);
    }
}
