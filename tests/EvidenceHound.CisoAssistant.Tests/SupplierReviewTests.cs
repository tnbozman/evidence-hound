using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>
/// s09-37: a supplier review's conclusion is approved in the config repo with <c>hound approve</c> and written to CISO Assistant by
/// <c>hound scope apply</c>. A review whose conclusion the scope does not declare is left as CISO Assistant holds it.
/// </summary>
public sealed class SupplierReviewTests : IDisposable
{
    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public SupplierReviewTests()
    {
        SupplierClientTests.Arrange(_ca);
        SupplierClientTests.Respond(_ca, "PATCH", "/api/entity-assessments/ea-1/", new JsonObject { ["id"] = "ea-1" });
    }

    [Fact]
    public async Task Conclusion_in_config_is_applied_to_ciso_assistant()
    {
        var query = new ScopeStateQuery("Global", [], new Dictionary<string, IReadOnlyList<RequirementKey>>()) { ThirdParties = true };
        var held = (await _ca.Scope.ReadAsync(query, _ct)).Value();
        var review = held.Single(a => a.Held.Kind == ScopeKind.EntityAssessment);
        review.Held.Fields["conclusion"].ShouldBe("ok");

        static ScopeObject Declared(string? conclusion) => new(ScopeKind.EntityAssessment, "EA-EXCLOUD-2026-06", new Dictionary<string, string?>
        {
            ["name"] = "Example Cloud review 2026",
            ["entity"] = "SUP-EXCLOUD",
            ["solutions"] = "SOL-EXCLOUD-HOST",
            ["domain"] = "Example Pty Ltd",
            ["perimeter"] = "Suppliers",
            ["due_date"] = "2026-06-30",
            ["conclusion"] = conclusion,
        }.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value!, StringComparer.Ordinal));

        // The same conclusion, or none declared: the conclusion is not a change, and one made in CISO Assistant is left alone.
        ScopePlanner.Plan([Declared("ok")], [review], prune: false).Changes.SelectMany(c => c.Fields).ShouldBeEmpty();
        ScopePlanner.Plan([Declared(null)], [review], prune: false).Changes.SelectMany(c => c.Fields).ShouldBeEmpty();

        // A different conclusion in config is planned field by field, and the write sends it.
        var change = ScopePlanner.Plan([Declared("blocker")], [review], prune: false).Changes.ShouldHaveSingleItem();
        change.Action.ShouldBe(ScopeAction.Update);
        change.Fields.ShouldBe([new FieldChange("conclusion", "ok", "blocker")]);

        (await _ca.Writer.WriteAsync(new ScopeWrite(change, Declared("blocker"), "Global"), _ct)).Value();

        var sent = _ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == "PATCH" && e.RequestMessage.Path == "/api/entity-assessments/ea-1/");
        JsonNode.Parse(sent.RequestMessage!.Body!)!["conclusion"]!.GetValue<string>().ShouldBe("blocker");
    }

    public void Dispose() => _ca.Dispose();
}
