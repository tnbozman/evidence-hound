using EvidenceHound.Core.Results;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Result = EvidenceHound.Core.Results.Result;

namespace EvidenceHound.Core.Tests.Results;

public class ResultTests
{
    private static readonly Problem NotFound = new("not_found", "The thing was not found.");
    private static readonly Problem Refused = new("refused", "The API refused the request.");

    [Fact]
    public async Task BindAsync_on_ok_awaits_the_next_step()
    {
        var result = await Result.Ok(20).BindAsync(n => Task.FromResult(Result.Ok(n + 1)));

        result.ShouldBe(Result.Ok(21));
    }

    [Fact]
    public async Task BindAsync_on_fail_skips_the_next_step()
    {
        var called = false;

        var result = await Result.Fail<int>(NotFound).BindAsync(n =>
        {
            called = true;
            return Task.FromResult(Result.Ok(n + 1));
        });

        result.ShouldBe(Result.Fail<int>(NotFound));
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task BindAsync_chains_on_a_pending_result()
    {
        var result = await Task.FromResult(Result.Ok(1))
            .BindAsync(n => Task.FromResult(Result.Ok(n + 1)))
            .BindAsync(n => Task.FromResult(Result.Fail<int>(Refused)));

        result.ShouldBe(Result.Fail<int>(Refused));
    }

    [Fact]
    public void Map_on_ok_applies_function()
    {
        var result = Result.Ok(20).Map(n => n + 1);

        result.ShouldBe(Result.Ok(21));
    }

    [Fact]
    public void Map_on_fail_keeps_error()
    {
        var called = false;

        var result = Result.Fail<int>(NotFound).Map(n =>
        {
            called = true;
            return n + 1;
        });

        result.ShouldBe(Result.Fail<int>(NotFound));
        called.ShouldBeFalse();
    }

    [Fact]
    public void Bind_on_ok_returns_the_next_result()
    {
        Result.Ok(4).Bind(n => Result.Ok(n * 2)).ShouldBe(Result.Ok(8));
        Result.Ok(4).Bind(_ => Result.Fail<int>(Refused)).ShouldBe(Result.Fail<int>(Refused));
    }

    [Fact]
    public void Bind_short_circuits_on_first_fail()
    {
        var calls = 0;

        var result = Result.Ok(1)
            .Bind(_ => Result.Fail<int>(NotFound))
            .Bind(n =>
            {
                calls++;
                return Result.Fail<int>(Refused);
            });

        result.ShouldBe(Result.Fail<int>(NotFound));
        calls.ShouldBe(0);
    }

    [Fact]
    public void Match_calls_the_branch_for_the_case()
    {
        Result.Ok(3).Match(n => $"ok {n}", errors => $"fail {errors.Count}").ShouldBe("ok 3");
        Result.Fail<int>(NotFound).Match(n => $"ok {n}", errors => $"fail {errors[0].Code}").ShouldBe("fail not_found");
    }

    [Fact]
    public void Combine_of_oks_is_ok_of_values_in_order()
    {
        var combined = Result.Combine([Result.Ok(1), Result.Ok(2), Result.Ok(3)]);

        combined.Match(values => values, _ => []).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Combine_collects_all_errors()
    {
        var combined = Result.Combine([Result.Ok(1), Result.Fail<int>(NotFound), Result.Ok(3), Result.Fail<int>(Refused)]);

        combined.ShouldBe(Result.Fail<IReadOnlyList<int>>([NotFound, Refused]));
    }

    [Fact]
    public void Fail_with_several_errors_equals_fail_with_the_same_errors()
    {
        Result.Fail<int>([NotFound, Refused]).ShouldBe(Result.Fail<int>([NotFound, Refused]));
        Result.Fail<int>([NotFound, Refused]).GetHashCode().ShouldBe(Result.Fail<int>([NotFound, Refused]).GetHashCode());
        Result.Fail<int>([NotFound, Refused]).ShouldNotBe(Result.Fail<int>([Refused, NotFound]));
        Result.Fail<int>(NotFound).Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void Nulls_and_empty_error_lists_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => Result.Ok<string>(null!));
        Should.Throw<ArgumentNullException>(() => new Problem(null!, "message"));
        Should.Throw<ArgumentNullException>(() => new Problem("code", null!));
        Should.Throw<ArgumentException>(() => Result.Fail<int>([]));
    }

    [Property(Arbitrary = [typeof(ResultArbitrary)])]
    public void Map_identity_is_identity(Result<int> result) =>
        result.Map(x => x).ShouldBe(result);

    public static class ResultArbitrary
    {
        public static Arbitrary<Result<int>> Results()
        {
            var errors = ArbMap.Default.GeneratorFor<NonEmptyString>()
                .Two()
                .Select(pair => new Problem(pair.Item1.Get, pair.Item2.Get))
                .NonEmptyListOf()
                .Select(list => Result.Fail<int>(list));
            var oks = ArbMap.Default.GeneratorFor<int>().Select(Result.Ok);
            return Gen.OneOf(oks, errors).ToArbitrary();
        }
    }
}
