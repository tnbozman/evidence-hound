using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>Reading what CISO Assistant holds under the root folder, from recorded v4.3.4 response shapes: lists find the objects, <c>/object/</c> gives their IDs and codes.</summary>
public sealed class ScopeStateReaderTests : IDisposable
{
    private static readonly string Fingerprint = "0123456789abcdef";
    private static readonly ScopeStateQuery Query = new(
        "Global",
        ["urn:intuitem:risk:framework:essential-eight-2023", "urn:example:risk:library:mapping-example"],
        new Dictionary<string, IReadOnlyList<RequirementKey>> { ["EX-E8-2026"] = [RequirementKey.Ref("ISM-1486")] });

    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public ScopeStateReaderTests() => RecordInstance();

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Reads_domains_perimeters_systems_audits_documents_controls_and_rows()
    {
        var held = (await _ca.Scope.ReadAsync(Query, _ct)).Value().ToDictionary(a => (a.Held.Kind, a.Held.Key));

        held.Keys.ShouldBe(
        [
            (ScopeKind.Framework, "urn:intuitem:risk:framework:essential-eight-2023"),
            (ScopeKind.Framework, "urn:example:risk:library:mapping-example"),
            (ScopeKind.Domain, "Example Pty Ltd"),
            (ScopeKind.Perimeter, "Example Pty Ltd/Corporate IT"),
            (ScopeKind.System, "Example 365"),
            (ScopeKind.System, "Example laptops"),
            (ScopeKind.Audit, "EX-E8-2026"),
            (ScopeKind.Document, "identity-policy"),
            (ScopeKind.Control, "AC-MFA"),
            (ScopeKind.Assessment, "EX-E8-2026/ISM-1486"),
        ],
        ignoreOrder: true);
        held[(ScopeKind.Framework, "urn:intuitem:risk:framework:essential-eight-2023")].Held.Fields.ShouldBe(new Dictionary<string, string> { ["version"] = "2" });
        held[(ScopeKind.Framework, "urn:example:risk:library:mapping-example")].Held.Fields.ShouldBe(new Dictionary<string, string> { ["version"] = "1" });
        held[(ScopeKind.Audit, "EX-E8-2026")].Locked.ShouldBeTrue();
        held[(ScopeKind.System, "Example laptops")].Locked.ShouldBeFalse();
        var domain = held[(ScopeKind.Domain, "Example Pty Ltd")];
        (domain.Held.Fields.ShouldHaveSingleItem(), domain.AppliedState).ShouldBe((new KeyValuePair<string, string>("description", "The whole company."), Fingerprint));
        held[(ScopeKind.Perimeter, "Example Pty Ltd/Corporate IT")].Held.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Corporate IT",
            ["ref_id"] = "PER-CORP",
            ["domain"] = "Example Pty Ltd",
        }, ignoreOrder: true);
        held[(ScopeKind.System, "Example laptops")].ShouldSatisfyAllConditions(
            s => s.Held.Fields.ShouldBe(new Dictionary<string, string>
            {
                ["ref_id"] = "SYS-LAPTOPS",
                ["type"] = "supporting",
                ["description"] = "Staff laptops.",
                ["owner"] = "it@example.com",
                ["domain"] = "Example Pty Ltd",
                ["parents"] = "Example 365",
            }, ignoreOrder: true),
            s => s.AppliedState.ShouldBeNull());
        held[(ScopeKind.Audit, "EX-E8-2026")].Held.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Example Essential Eight ML2 2026",
            ["framework"] = "urn:intuitem:risk:framework:essential-eight-2023",
            ["groups"] = "1, 2",
            ["domain"] = "Example Pty Ltd",
            ["perimeter"] = "Corporate IT",
            ["eta"] = "2026-06-30",
            ["authors"] = "it@example.com",
            ["status"] = "in_review",
            ["locked"] = "true",
        }, ignoreOrder: true);
        held[(ScopeKind.Document, "identity-policy")].Held.Fields.ShouldBe(
            new Dictionary<string, string> { ["name"] = "Identity policy", ["domain"] = "Example Pty Ltd", ["hashes"] = "aaaa, bbbb" }, ignoreOrder: true);
        held[(ScopeKind.Control, "AC-MFA")].Held.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Multi-factor authentication",
            ["domain"] = "Example Pty Ltd",
            ["owner"] = "it@example.com",
            ["priority"] = "1",
            ["category"] = "technical",
            ["systems"] = "Example 365",
            ["documents"] = "identity-policy",
        }, ignoreOrder: true);

        // s11-02: the status CISO Assistant owns is read for the system security plan, never compared or written.
        held[(ScopeKind.Control, "AC-MFA")].Observed.ShouldBe(new Dictionary<string, string> { ["status"] = "active" });
        held[(ScopeKind.Assessment, "EX-E8-2026/ISM-1486")].Held.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["status"] = "done",
            ["result"] = "compliant",
            ["observation"] = "Reviewed by hand.",
            ["controls"] = "AC-MFA",
            ["documents"] = "identity-policy",
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task Objects_outside_the_root_folder_and_its_domains_are_left_out()
    {
        var held = (await _ca.Scope.ReadAsync(Query, _ct)).Value();

        held.ShouldNotContain(a => a.Held.Key.Contains("Other", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reading_sends_only_gets()
    {
        await _ca.Scope.ReadAsync(Query, _ct);

        _ca.Server.LogEntries.ShouldNotBeEmpty();
        _ca.Server.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Missing_root_folder_is_a_problem()
    {
        var result = await _ca.Scope.ReadAsync(Query with { RootFolder = "Elsewhere" }, _ct);

        result.Problems().ShouldHaveSingleItem().Code.ShouldBe("ciso_assistant.not_found");
    }

    [Fact]
    public async Task Reading_everything_gives_every_row_of_every_audit_and_a_snapshot_of_its_framework()
    {
        RecordFramework();

        var state = (await _ca.Export.ReadAllAsync("Global", _ct)).Value();

        state.RootFolder.ShouldBe("Global");
        state.Objects.Where(o => o.Held.Kind == ScopeKind.Assessment).Select(o => o.Held.Key).ShouldBe(["EX-E8-2026/ISM-1486", "EX-E8-2026/ISM-1504"], ignoreOrder: true);
        state.Objects.Where(o => o.Held.Kind == ScopeKind.Framework).Select(o => (o.Held.Key, o.Held.Fields["version"]))
            .ShouldBe([("urn:intuitem:risk:framework:essential-eight-2023", "2")]);
        var snapshot = state.Frameworks.ShouldHaveSingleItem();
        (snapshot.LibraryUrn, snapshot.FrameworkUrn, snapshot.Version).ShouldBe(("urn:intuitem:risk:library:essential-eight-2023", "urn:intuitem:risk:framework:essential-eight-2023", 2));
        snapshot.Groups.Select(g => g.RefId).ShouldBe(["1", "2"]);
        snapshot.Nodes.Select(n => (n.RefId, n.Title, string.Join(",", n.Groups))).ShouldBe([("ISM-1486", "Java is blocked", "1,2"), ("ISM-1504", "ISM-1504", "1,2")]);
        _ca.Server.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task An_audit_without_a_ref_id_is_keyed_by_one_made_from_its_name()
    {
        RecordFramework();
        _ca.Server.Given(Request.Create().WithPath("/api/compliance-assessments/").UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = 1, ["results"] = new JsonArray(Listed("ca-noref", "f-ex")) }.ToJsonString()));
        Object("compliance-assessments", "ca-noref", new JsonObject
        {
            ["id"] = "ca-noref",
            ["name"] = "Example review 2026",
            ["ref_id"] = "",
            ["framework"] = "fw-e8",
            ["perimeter"] = "p-corp",
            ["folder"] = "f-ex",
        });

        var state = (await _ca.Export.ReadAllAsync("Global", _ct)).Value();

        state.Objects.Where(o => o.Held.Kind == ScopeKind.Audit).Select(o => o.Held.Key).ShouldBe(["EXAMPLE-REVIEW-2026"]);
    }

    [Fact]
    public async Task A_framework_without_group_definitions_takes_its_groups_from_its_nodes_and_its_library_from_the_loaded_libraries()
    {
        RecordFramework();
        _ca.Server.Given(Request.Create().WithPath("/api/frameworks/fw-e8/object/").UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["id"] = "fw-e8", ["urn"] = "urn:intuitem:risk:framework:essential-eight-2023", ["library"] = "lib-e8" }.ToJsonString()));
        _ca.Server.Given(Request.Create().WithPath("/api/frameworks/").UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject
            {
                ["count"] = 1,
                ["results"] = new JsonArray(new JsonObject { ["id"] = "fw-e8", ["urn"] = "urn:intuitem:risk:framework:essential-eight-2023", ["library"] = new JsonObject { ["id"] = "lib-e8", ["str"] = "Essential Eight" } }),
            }.ToJsonString()));

        var snapshot = (await _ca.Export.ReadAllAsync("Global", _ct)).Value().Frameworks.ShouldHaveSingleItem();

        snapshot.Groups.Select(g => g.RefId).ShouldBe(["1", "2"]);
        snapshot.LibraryUrn.ShouldBe("urn:intuitem:risk:library:essential-eight-2023");
    }

    /// <summary>The Essential Eight framework's own record and its requirement nodes, one of them not assessable.</summary>
    private void RecordFramework()
    {
        Object("frameworks", "fw-e8", new JsonObject
        {
            ["id"] = "fw-e8",
            ["urn"] = "urn:intuitem:risk:framework:essential-eight-2023",
            ["library"] = "lib-e8",
            ["implementation_groups_definition"] = new JsonArray(new JsonObject { ["ref_id"] = "1", ["name"] = "ML1" }, new JsonObject { ["ref_id"] = "2", ["name"] = "ML2" }),
        });
        List("requirement-nodes",
            new JsonObject { ["urn"] = "urn:intuitem:risk:req_node:essential-eight-2023:node1", ["ref_id"] = null, ["name"] = "Patch applications", ["assessable"] = false },
            new JsonObject
            {
                ["urn"] = "urn:intuitem:risk:req_node:essential-eight-2023:ism-1486",
                ["ref_id"] = "ISM-1486",
                ["name"] = "Java is blocked",
                ["assessable"] = true,
                ["parent_urn"] = "urn:intuitem:risk:req_node:essential-eight-2023:node1",
                ["implementation_groups"] = new JsonArray("1", "2"),
            },
            new JsonObject
            {
                ["urn"] = "urn:intuitem:risk:req_node:essential-eight-2023:ism-1504",
                ["ref_id"] = "ISM-1504",
                ["name"] = null,
                ["assessable"] = true,
                ["implementation_groups"] = new JsonArray("1", "2"),
            });
    }

    [Fact]
    public async Task Reads_security_exceptions_and_risk_acceptances_with_their_approval_observed()
    {
        // s15-03: links come from both sides (a control's or row's security_exceptions); status and expiry are CISO Assistant's, only observed.
        List("risk-assessments", Listed("ra-study", "f-ex"));
        Object("risk-assessments", "ra-study", new JsonObject { ["id"] = "ra-study", ["name"] = "Example risks", ["ref_id"] = "EX-RISK-2026", ["folder"] = "f-ex" });
        List("risk-matrices");
        List("threats");
        _ca.Server.Given(Request.Create().WithPath("/api/risk-scenarios/").WithParam("risk_assessment", "ra-study").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject
            {
                ["count"] = 1,
                ["results"] = new JsonArray(new JsonObject
                {
                    ["id"] = "rs-2",
                    ["ref_id"] = "R-02",
                    ["name"] = "Backup administrator leaves",
                    ["treatment"] = "accept",
                    ["security_exceptions"] = new JsonArray(new JsonObject { ["id"] = "se-1", ["str"] = "EXC-01" }),
                }),
            }.ToJsonString()));
        List("security-exceptions", Listed("se-1", "f-ex"));
        Object("security-exceptions", "se-1", new JsonObject
        {
            ["id"] = "se-1",
            ["ref_id"] = "EXC-01",
            ["name"] = "Legacy finance app without MFA",
            ["folder"] = "f-ex",
            ["severity"] = 2,
            ["status"] = "approved",
            ["expiration_date"] = "2027-03-31",
            ["owners"] = new JsonArray("act-it"),
            ["assets"] = new JsonArray("a-365"),
            ["observation"] = "Office network only.",
        });
        List("risk-acceptances", Listed("rac-1", "f-ex"));
        Object("risk-acceptances", "rac-1", new JsonObject
        {
            ["id"] = "rac-1",
            ["ref_id"] = "ACC-EX-RISK-2026-R-02",
            ["name"] = "Accept: Backup administrator leaves",
            ["folder"] = "f-ex",
            ["state"] = "submitted",
            ["expiry_date"] = "2027-06-30",
            ["risk_scenarios"] = new JsonArray("rs-2"),
            ["justification"] = "Training a second.",
        });

        var held = (await _ca.Scope.ReadAsync(Query with { Risks = true }, _ct)).Value().ToDictionary(a => (a.Held.Kind, a.Held.Key));

        var exception = held[(ScopeKind.SecurityException, "EXC-01")];
        exception.Held.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Legacy finance app without MFA",
            ["domain"] = "Example Pty Ltd",
            ["severity"] = "medium",
            ["owners"] = "it@example.com",
            ["expiration_date"] = "2027-03-31",
            ["observation"] = "Office network only.",
            ["systems"] = "Example 365",
            ["controls"] = "AC-MFA",
            ["requirements"] = "EX-E8-2026/ISM-1486",
        });
        exception.Observed.ShouldBe(new Dictionary<string, string> { ["status"] = "approved", ["expiration_date"] = "2027-03-31" });
        var acceptance = held[(ScopeKind.RiskAcceptance, "EX-RISK-2026/R-02")];
        acceptance.Held.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Accept: Backup administrator leaves",
            ["domain"] = "Example Pty Ltd",
            ["scenario"] = "EX-RISK-2026/R-02",
            ["justification"] = "Training a second.",
        });
        acceptance.Observed.ShouldBe(new Dictionary<string, string> { ["status"] = "submitted", ["expiration_date"] = "2027-06-30" });
        held[(ScopeKind.RiskScenario, "EX-RISK-2026/R-02")].Held.Fields["exceptions"].ShouldBe("EXC-01");
    }

    private void RecordInstance()
    {
        List("folders", Folder("f-global", "Global", null), Folder("f-ex", "Example Pty Ltd", "f-global"), Folder("f-other", "Other Pty Ltd", "f-other-parent"));
        Object("folders", "f-ex", new JsonObject { ["id"] = "f-ex", ["name"] = "Example Pty Ltd", ["description"] = $"The whole company.\n[hound scope {Fingerprint}]", ["parent_folder"] = "f-global" });
        List("users", new JsonObject { ["id"] = "u-it", ["email"] = "it@example.com" });
        // Owners, authors and reviewers are actors: each user, team or entity has one, and a user's actor names the user as "specific".
        List("actors", new JsonObject { ["id"] = "act-it", ["str"] = "it@example.com", ["type"] = "user", ["specific"] = new JsonObject { ["id"] = "u-it", ["str"] = "it@example.com" } });
        List("frameworks",
            new JsonObject { ["id"] = "fw-e8", ["urn"] = "urn:intuitem:risk:framework:essential-eight-2023", ["library"] = new JsonObject { ["id"] = "lib-e8", ["urn"] = "urn:intuitem:risk:library:essential-eight-2023" } },
            new JsonObject { ["id"] = "fw-x", ["urn"] = "urn:example:risk:framework:not-in-scope", ["library"] = new JsonObject { ["id"] = "lib-x", ["urn"] = "urn:example:risk:library:not-in-scope" } });
        List("loaded-libraries",
            new JsonObject { ["id"] = "lib-e8", ["urn"] = "urn:intuitem:risk:library:essential-eight-2023", ["version"] = 2 },
            new JsonObject { ["id"] = "lib-map", ["urn"] = "urn:example:risk:library:mapping-example", ["version"] = 1 },
            new JsonObject { ["id"] = "lib-x", ["urn"] = "urn:example:risk:library:not-in-scope", ["version"] = 1 });
        List("evidence-revisions", new JsonObject { ["id"] = "rev-2", ["attachment_hash"] = "bbbb" }, new JsonObject { ["id"] = "rev-1", ["attachment_hash"] = "aaaa" });
        List("perimeters", Listed("p-corp", "f-ex"), Listed("p-other", "f-other"));
        Object("perimeters", "p-corp", new JsonObject { ["id"] = "p-corp", ["name"] = "Corporate IT", ["ref_id"] = "PER-CORP", ["description"] = "", ["folder"] = "f-ex" });
        List("assets", Listed("a-365", "f-ex"), Listed("a-laptops", "f-ex"), Listed("a-other", "f-other"));
        Object("assets", "a-365", new JsonObject { ["id"] = "a-365", ["name"] = "Example 365", ["ref_id"] = "SYS-365", ["type"] = "PR", ["folder"] = "f-ex", ["parent_assets"] = new JsonArray(), ["owner"] = new JsonArray() });
        Object("assets", "a-laptops", new JsonObject
        {
            ["id"] = "a-laptops",
            ["name"] = "Example laptops",
            ["ref_id"] = "SYS-LAPTOPS",
            ["type"] = "SP",
            ["description"] = "Staff laptops.",
            ["folder"] = "f-ex",
            ["parent_assets"] = new JsonArray("a-365"),
            ["owner"] = new JsonArray("act-it"),
        });
        List("compliance-assessments", Listed("ca-e8", "f-ex"));
        Object("compliance-assessments", "ca-e8", new JsonObject
        {
            ["id"] = "ca-e8",
            ["name"] = "Example Essential Eight ML2 2026",
            ["ref_id"] = "EX-E8-2026",
            ["framework"] = "fw-e8",
            ["perimeter"] = "p-corp",
            ["folder"] = "f-ex",
            ["selected_implementation_groups"] = new JsonArray("2", "1"),
            ["eta"] = "2026-06-30",
            ["due_date"] = null,
            ["status"] = "in_review",
            ["is_locked"] = true,
            ["authors"] = new JsonArray("act-it"),
            ["reviewers"] = new JsonArray(),
        });
        List("evidences", Listed("ev-policy", "f-ex"));
        Object("evidences", "ev-policy", new JsonObject { ["id"] = "ev-policy", ["name"] = "Identity policy", ["ref_id"] = "identity-policy", ["folder"] = "f-ex" });
        List("applied-controls", Listed("ac-mfa", "f-ex"));
        Object("applied-controls", "ac-mfa", new JsonObject
        {
            ["id"] = "ac-mfa",
            ["name"] = "Multi-factor authentication",
            ["ref_id"] = "AC-MFA",
            ["folder"] = "f-ex",
            ["owner"] = new JsonArray("act-it"),
            ["status"] = "active",
            ["priority"] = 1,
            ["category"] = "technical",
            ["eta"] = null,
            ["assets"] = new JsonArray("a-365"),
            ["evidences"] = new JsonArray("ev-policy"),
            ["security_exceptions"] = new JsonArray("se-1"),
        });
        List("requirement-assessments",
            new JsonObject
            {
                ["id"] = "ra-1486",
                ["status"] = "done",
                ["result"] = "compliant",
                ["observation"] = "Reviewed by hand.",
                ["applied_controls"] = new JsonArray("ac-mfa"),
                ["evidences"] = new JsonArray(new JsonObject { ["id"] = "ev-policy", ["str"] = "Identity policy" }),
                ["security_exceptions"] = new JsonArray(new JsonObject { ["id"] = "se-1", ["str"] = "EXC-01" }),
                ["requirement"] = new JsonObject { ["urn"] = "urn:intuitem:risk:req_node:essential-eight-2023:ism-1486", ["ref_id"] = "ISM-1486", ["implementation_groups"] = new JsonArray("1") },
            },
            new JsonObject
            {
                ["id"] = "ra-1504",
                ["status"] = "to_do",
                ["result"] = "not_assessed",
                ["observation"] = "",
                ["requirement"] = new JsonObject { ["urn"] = "urn:intuitem:risk:req_node:essential-eight-2023:ism-1504", ["ref_id"] = "ISM-1504", ["implementation_groups"] = new JsonArray("1") },
            });
    }

    private static JsonObject Folder(string id, string name, string? parent) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["parent_folder"] = parent is null ? null : new JsonObject { ["id"] = parent, ["str"] = "parent" },
    };

    /// <summary>A list entry as the read serializers give it: the folder as an {"id", "str"} object.</summary>
    private static JsonObject Listed(string id, string folder) => new() { ["id"] = id, ["folder"] = new JsonObject { ["id"] = folder, ["str"] = "folder" } };

    private void List(string resource, params JsonObject[] items) =>
        _ca.Server.Given(Request.Create().WithPath($"/api/{resource}/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = items.Length, ["results"] = new JsonArray(items) }.ToJsonString()));

    private void Object(string resource, string id, JsonObject body) =>
        _ca.Server.Given(Request.Create().WithPath($"/api/{resource}/{id}/object/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body.ToJsonString()));
}
