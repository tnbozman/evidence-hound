using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Secrets;
using FsCheck.Xunit;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>Assess phase: a declarative check over one dataset or query gives pass, fail, partial, not applicable or unable to assess.</summary>
public sealed class CheckEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 2, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("fail", CheckOutcome.Fail)]
    [InlineData("not_applicable", CheckOutcome.NotApplicable)]
    [InlineData("unable_to_assess", CheckOutcome.Error)]
    public void All_over_empty_uses_when_empty(string whenEmpty, CheckOutcome expected)
    {
        var check = Check($"each: {{ field: $.enabled, equals: true }}\nrequire: all\nwhen_empty: {whenEmpty}");

        Evaluate(check, "[]").Outcome.ShouldBe(expected);
    }

    [Fact]
    public void Missing_field_is_unable_to_assess()
    {
        var check = Check("each: { field: $.enabled, equals: true }\nrequire: all\nwhen_empty: fail");

        var finding = Evaluate(check, """[{ "id": "a", "enabled": true }, { "id": "b" }]""");

        finding.Outcome.ShouldBe(CheckOutcome.Error);
        finding.Observation.ShouldStartWith("Unable to assess:");
        finding.Observation.ShouldContain("$.enabled");
        finding.Observation.ShouldContain("b");
    }

    [Fact]
    public void Missing_pass_treats_absent_field_as_pass()
    {
        var check = Check("each: { field: $.upgrade, older_than: P14D, missing: pass, negate: true }\nrequire: all\nwhen_empty: not_applicable");

        Evaluate(check, """[{ "id": "a" }, { "id": "b", "upgrade": "2026-11-20T00:00:00Z" }]""").Outcome.ShouldBe(CheckOutcome.Pass);
        Evaluate(check, """[{ "id": "a" }, { "id": "b", "upgrade": "2026-10-01T00:00:00Z" }]""").Outcome.ShouldBe(CheckOutcome.Fail);
    }

    [Theory]
    [InlineData(10, CheckOutcome.Pass)]
    [InlineData(9, CheckOutcome.Partial)]
    [InlineData(8, CheckOutcome.Partial)]
    [InlineData(7, CheckOutcome.Fail)]
    public void Ratio_band_gives_partial(int passing, CheckOutcome expected)
    {
        var check = Check("each: { field: $.enabled, equals: true }\nrequire: { ratio_at_least: 0.95, partial_at: 0.8 }\nwhen_empty: not_applicable");
        var rows = new JsonArray([.. Enumerable.Range(0, 10).Select(i => (JsonNode)new JsonObject { ["id"] = $"r{i}", ["enabled"] = i < passing })]);

        Evaluate(check, rows.ToJsonString()).Outcome.ShouldBe(expected);
    }

    [Fact]
    public void Failed_source_is_unable_to_assess_naming_the_cause()
    {
        var check = Check("each: { field: $.enabled, equals: true }\nrequire: all\nwhen_empty: fail");

        var finding = CheckEvaluator.Evaluate(check, new RowSource.Failed("things", "Paging stopped at its page cap of 3 pages."), Renderer(), Now);

        finding.Outcome.ShouldBe(CheckOutcome.Error);
        finding.Observation.ShouldBe("Unable to assess: 'things' could not be read. Paging stopped at its page cap of 3 pages.");
    }

    [Fact]
    public void Unavailable_source_is_not_applicable_with_the_reason()
    {
        var check = Check("each: { field: $.enabled, equals: true }\nrequire: all\nwhen_empty: fail");

        var finding = CheckEvaluator.Evaluate(check, new RowSource.Unavailable("things", "The API answered 403; the licence may be missing."), Renderer(), Now);

        finding.Outcome.ShouldBe(CheckOutcome.NotApplicable);
        finding.Observation.ShouldContain("403");
    }

    [Fact]
    public void Rows_that_matched_no_required_join_are_unable_to_assess()
    {
        var check = Check("each: { field: $.enabled, equals: true }\nrequire: all\nwhen_empty: fail");
        var source = new RowSource.Rows("things", Rows("""[{ "enabled": true }]"""), ["things", "roles"], Unmatched: 2);

        var finding = CheckEvaluator.Evaluate(check, source, Renderer(), Now);

        finding.Outcome.ShouldBe(CheckOutcome.Error);
        finding.Observation.ShouldContain("2 rows");
    }

    [Fact]
    public void Wrong_type_is_unable_to_assess()
    {
        var check = Check("each: { field: $.count, at_least: 2 }\nrequire: all\nwhen_empty: fail");

        Evaluate(check, """[{ "id": "a", "count": "lots" }]""").Outcome.ShouldBe(CheckOutcome.Error);
    }

    [Theory]
    [InlineData("{ field: $.v, equals: on }", "\"on\"", "\"off\"")]
    [InlineData("{ field: $.v, equals: 3 }", "3", "4")]
    [InlineData("{ field: $.v, not_equals: off }", "\"on\"", "\"off\"")]
    [InlineData("{ field: $.v, in: [a, b] }", "\"b\"", "\"c\"")]
    [InlineData("{ field: $.v, in: \"${allowed}\" }", "\"y\"", "\"z\"")]
    [InlineData("{ field: $.v, matches: \"^ab+$\" }", "\"abbb\"", "\"ac\"")]
    [InlineData("{ field: $.v, any_matches: \"^Owner$\" }", "[\"Member\", \"Owner\"]", "[\"Member\"]")]
    [InlineData("{ field: $.v, exists: true }", "1", null)]
    [InlineData("{ field: $.v, version_in: \"${supported}\" }", "\"15.1.2\"", "\"13.9\"")]
    [InlineData("{ field: $.v, version_at_least: \"14.2\" }", "\"14.10\"", "\"14.1.9\"")]
    [InlineData("{ field: $.v, within: P7D }", "\"2026-11-20T00:00:00Z\"", "\"2026-11-01T00:00:00Z\"")]
    [InlineData("{ field: $.v, older_than: P7D }", "\"2026-11-01T00:00:00Z\"", "\"2026-11-20T00:00:00Z\"")]
    [InlineData("{ field: $.v, at_least: 2 }", "2", "1.5")]
    [InlineData("{ field: $.v, at_most: 2 }", "2", "3")]
    [InlineData("{ field: $.v, equals: on, negate: true }", "\"off\"", "\"on\"")]
    public void Operators_pass_and_fail(string test, string passing, string? failing)
    {
        var check = Check($"each: {test}\nrequire: all\nwhen_empty: fail");

        Evaluate(check, $$"""[{ "v": {{passing}} }]""").Outcome.ShouldBe(CheckOutcome.Pass);
        Evaluate(check, failing is null ? "[{}]" : $$"""[{ "v": {{failing}} }]""").Outcome.ShouldBe(CheckOutcome.Fail);
    }

    [Fact]
    public void Count_requirement_counts_rows_in_scope()
    {
        var check = Check("where: { field: $.role, equals: owner }\nrequire: { count_at_most: \"${max}\" }\nwhen_empty: not_applicable\ndescribe: \"{matched} of {total} users are owners; at most ${max}.\"");
        const string rows = """[{ "role": "owner" }, { "role": "owner" }, { "role": "member" }]""";

        var finding = Evaluate(check, rows, ("max", "1"));

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldStartWith("2 of 3 users are owners; at most 1.");
        Evaluate(check, rows, ("max", "2")).Outcome.ShouldBe(CheckOutcome.Pass);
    }

    [Fact]
    public void Observation_fills_counts_lists_offending_rows_capped_and_cites_the_evidence()
    {
        var check = Check("each: { field: $.enabled, equals: true }\nrequire: all\nwhen_empty: fail\ndescribe: \"{passed} of {total} things are enabled; {failed} are not.\"");
        var rows = new JsonArray([.. Enumerable.Range(1, 25).Select(i => (JsonNode)new JsonObject { ["id"] = $"thing-{i:00}", ["enabled"] = i > 24 })]);

        var finding = CheckEvaluator.Evaluate(check, new RowSource.Rows("things", rows, ["things"], 0), Renderer(), Now, "things.json");

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldStartWith("1 of 25 things are enabled; 24 are not. Failing: thing-01, thing-02,");
        finding.Observation.ShouldContain("thing-20 and 4 more.");
        finding.Observation.ShouldNotContain("thing-21");
        finding.Observation.ShouldEndWith("Evidence: things.json.");
    }

    [Property]
    public void Evaluation_is_deterministic_for_the_same_rows_and_clock(bool[] enabled)
    {
        var check = Check("each: { field: $.enabled, equals: true }\nrequire: { ratio_at_least: 0.9, partial_at: 0.5 }\nwhen_empty: not_applicable");
        var rows = Flags(enabled);

        Evaluate(check, rows).ShouldBe(Evaluate(check, rows));
    }

    [Property]
    public void Adding_a_failed_query_never_turns_a_fail_into_a_pass(bool[] enabled, bool[] roles)
    {
        var profile = ProfileParser.Parse(TwoQueries, "two.yaml").Value();
        var users = new QueryResult.Collected("users", Rows(Flags(enabled)), 1, 1, "GET v1/users");
        var before = Assess(profile, users, new QueryResult.Collected("roles", Rows(Flags(roles)), 1, 1, "GET v1/roles"));
        var after = Assess(profile, users, new QueryResult.Failed("roles", "The API answered 500."));

        foreach (var (was, now) in before.Zip(after))
        {
            if (was == CheckOutcome.Fail)
            {
                now.ShouldNotBe(CheckOutcome.Pass);
            }
        }
    }

    private static readonly string TwoQueries = """
        profile: two
        version: "1.0.0"
        connection:
          base_url: https://api.example.com/
          allowed_hosts: [api.example.com]
          auth: { type: none }
        queries:
          users:
            get: v1/users
          roles:
            get: v1/roles
        datasets:
          joined:
            from: users
            join:
              - with: roles
                on: { left: $.enabled, right: $.enabled }
                as: same
                optional: true
        checks:
          - id: example.users.enabled
            over: users
            each: { field: $.enabled, equals: true }
            require: all
            when_empty: fail
          - id: example.joined.enabled
            over: joined
            each: { field: $.enabled, equals: true }
            require: all
            when_empty: fail
        """;

    private static List<CheckOutcome> Assess(Profile profile, QueryResult users, QueryResult roles)
    {
        var sources = Extraction.Build(profile, new Dictionary<string, QueryResult>(StringComparer.Ordinal) { ["users"] = users, ["roles"] = roles }, includeIdentities: false);
        return [.. profile.Checks.Select(c => CheckEvaluator.Evaluate(c, sources[c.Over[0]], Renderer(), Now).Outcome)];
    }

    private static string Flags(bool[] values) =>
        new JsonArray([.. values.Select((v, i) => (JsonNode)new JsonObject { ["id"] = $"r{i}", ["enabled"] = v })]).ToJsonString();

    private static Check Check(string body)
    {
        var indented = string.Join("\n", body.Split('\n').Select(line => "    " + line));
        var yaml = $"""
            profile: checks
            version: "1.0.0"
            parameters:
              allowed: {"{"} type: list, default: "x, y" {"}"}
              supported: {"{"} type: list, default: "14, 15" {"}"}
              max: {"{"} type: int, default: "1" {"}"}
            connection:
              base_url: https://api.example.com/
              allowed_hosts: [api.example.com]
              auth: {"{"} type: none {"}"}
            queries:
              things:
                get: v1/things
            checks:
              - id: example.thing.tested
                over: things
            {indented}
            """;
        return ProfileParser.Parse(yaml, "checks.yaml").Value().Checks[0];
    }

    private static Renderer Renderer(params (string Name, string Value)[] parameters)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["allowed"] = "x, y", ["supported"] = "14, 15", ["max"] = "1" };
        foreach (var (name, value) in parameters)
        {
            values[name] = value;
        }

        return new Renderer(values, new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, Secret>(StringComparer.Ordinal));
    }

    private static JsonArray Rows(string json) => JsonNode.Parse(json)!.AsArray();

    private static Finding Evaluate(Check check, string rows, params (string Name, string Value)[] parameters) =>
        CheckEvaluator.Evaluate(check, new RowSource.Rows("things", Rows(rows), ["things"], 0), Renderer(parameters), Now);
}
