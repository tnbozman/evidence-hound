using System.Collections.Immutable;

namespace EvidenceHound.Core.Results;

/// <summary>Constructors and combinators for <see cref="Result{T}"/>.</summary>
public static class Result
{
    public static Result<T> Ok<T>(T value)
        where T : notnull => new Result<T>.Ok(value);

    public static Result<T> Fail<T>(Problem error)
        where T : notnull => new Result<T>.Fail([error]);

    public static Result<T> Fail<T>(IEnumerable<Problem> errors)
        where T : notnull => new Result<T>.Fail(errors);

    /// <summary>Turns a list of results into a result of a list, collecting every error rather than stopping at the first.</summary>
    public static Result<IReadOnlyList<T>> Combine<T>(IEnumerable<Result<T>> results)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(results);

        var values = ImmutableArray.CreateBuilder<T>();
        var errors = ImmutableArray.CreateBuilder<Problem>();
        foreach (var result in results)
        {
            switch (result)
            {
                case Result<T>.Ok ok:
                    values.Add(ok.Value);
                    break;
                case Result<T>.Fail fail:
                    errors.AddRange(fail.Errors);
                    break;
            }
        }

        return errors.Count == 0
            ? Ok<IReadOnlyList<T>>(values.ToImmutable())
            : Fail<IReadOnlyList<T>>(errors.ToImmutable());
    }
}

/// <summary>Either <see cref="Ok"/> with a value or <see cref="Fail"/> with one or more errors. Immutable and never null.</summary>
public abstract record Result<T>
    where T : notnull
{
    private Result()
    {
    }

    public TOut Match<TOut>(Func<T, TOut> ok, Func<IReadOnlyList<Problem>, TOut> fail)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ArgumentNullException.ThrowIfNull(fail);

        return this switch
        {
            Ok success => ok(success.Value),
            Fail failure => fail(failure.Errors),
            _ => throw new InvalidOperationException("Result is either Ok or Fail."),
        };
    }

    public Result<TOut> Map<TOut>(Func<T, TOut> map)
        where TOut : notnull
    {
        ArgumentNullException.ThrowIfNull(map);
        return Match(value => Result.Ok(map(value)), Result.Fail<TOut>);
    }

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind)
        where TOut : notnull
    {
        ArgumentNullException.ThrowIfNull(bind);
        return Match(bind, Result.Fail<TOut>);
    }

    public sealed record Ok : Result<T>
    {
        public Ok(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public T Value { get; }
    }

    public sealed record Fail : Result<T>
    {
        public Fail(IEnumerable<Problem> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            Errors = [.. errors];
            if (Errors.Count == 0)
            {
                throw new ArgumentException("A failed result needs at least one error.", nameof(errors));
            }
        }

        public IReadOnlyList<Problem> Errors { get; }

        public bool Equals(Fail? other) =>
            other is not null && Errors.SequenceEqual(other.Errors);

        public override int GetHashCode() =>
            Errors.Aggregate(0, (hash, error) => HashCode.Combine(hash, error));
    }
}
