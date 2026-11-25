using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>Writing scope changes to CISO Assistant, from recorded v4.3.4 request and response shapes.</summary>
public sealed class ScopeClientTests : IDisposable
{
    private static readonly string State = "0123456789abcdef";
    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"hound-scope-{Guid.NewGuid():N}");
    private static readonly string E8 = "urn:intuitem:risk:framework:essential-eight-2023";
    private static readonly string E8Library = "urn:intuitem:risk:library:essential-eight-2023";

    public ScopeClientTests()
    {
        List("folders", Folder("f-global", "Global", null), Folder("f-ex", "Example Pty Ltd", "f-global"));
        List("assets", new JsonObject { ["id"] = "a-365", ["name"] = "Example 365", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" } });
        List("perimeters", new JsonObject { ["id"] = "p-corp", ["name"] = "Corporate IT", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" } });
        List("users", new JsonObject { ["id"] = "u-it", ["email"] = "it@example.com" });
        // Owners, authors and reviewers are actors: each user, team or entity has one, and a user's actor names the user as "specific".
        List("actors", new JsonObject { ["id"] = "act-it", ["str"] = "it@example.com", ["type"] = "user", ["specific"] = new JsonObject { ["id"] = "u-it", ["str"] = "it@example.com" } });
        foreach (var resource in new[] { "folders", "perimeters", "assets" })
        {
            _ca.Server.Given(Request.Create().WithPath($"/api/{resource}/").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(201).WithBody(new JsonObject { ["id"] = $"new-{resource}" }.ToJsonString()));
        }

        _ca.Server.Given(Request.Create().WithPath("/api/folders/f-ex/").UsingPatch()).RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));
    }

    public void Dispose()
    {
        _ca.Dispose();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Creates_folder_perimeter_and_asset()
    {
        var domain = Declared(ScopeKind.Domain, "Example Labs", ("description", "The lab."));
        var perimeter = Declared(ScopeKind.Perimeter, "Example Pty Ltd/Office network", ("name", "Office network"), ("ref_id", "PER-OFFICE"), ("domain", "Example Pty Ltd"));
        var system = Declared(ScopeKind.System, "Example laptops",
            ("ref_id", "SYS-LAPTOPS"), ("type", "supporting"), ("description", "Staff laptops."), ("owner", "it@example.com"), ("domain", "Example Pty Ltd"), ("parents", "Example 365"));

        (await Create(domain)).Value();
        (await Create(perimeter)).Value();
        (await Create(system)).Value();

        Posted("folders").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Example Labs",
            ["description"] = $"The lab.\n[hound scope {State}]",
            ["parent_folder"] = "f-global",
            ["content_type"] = "DO",
            // Off by default for a folder made through the API; without it the domain has no groups to give its roles (s07-04).
            ["create_iam_groups"] = true,
        }));
        Posted("perimeters").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Office network",
            ["ref_id"] = "PER-OFFICE",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-ex",
        }));
        Posted("assets").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Example laptops",
            ["ref_id"] = "SYS-LAPTOPS",
            ["type"] = "SP",
            ["description"] = $"Staff laptops.\n[hound scope {State}]",
            ["folder"] = "f-ex",
            ["parent_assets"] = new JsonArray("a-365"),
            ["owner"] = new JsonArray("act-it"),
        }));
    }

    [Fact]
    public async Task Update_patches_the_object_found_by_name_with_its_marker()
    {
        var domain = Declared(ScopeKind.Domain, "Example Pty Ltd", ("description", "The whole company."));

        var result = await _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(ScopeKind.Domain, domain.Key, ScopeAction.Update, [], State), domain, "Global"), _ct);

        result.Value();
        var patch = _ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == "PATCH");
        (patch.RequestMessage!.Path, Json(JsonNode.Parse(patch.RequestMessage.Body!)!)).ShouldBe(
            ("/api/folders/f-ex/", Json(new JsonObject { ["name"] = "Example Pty Ltd", ["description"] = $"The whole company.\n[hound scope {State}]", ["create_iam_groups"] = true })));
    }

    [Fact]
    public async Task Unknown_owner_is_a_problem_and_nothing_is_sent()
    {
        var system = Declared(ScopeKind.System, "Example tablets", ("type", "supporting"), ("owner", "nobody@example.com"), ("domain", "Example Pty Ltd"));

        var result = await Create(system);

        result.Problems().ShouldHaveSingleItem().Message.ShouldContain("nobody@example.com");
        _ca.Server.LogEntries.ShouldNotContain(e => e.RequestMessage!.Method != "GET");
    }

    [Fact]
    public async Task Missing_domain_is_a_problem()
    {
        var perimeter = Declared(ScopeKind.Perimeter, "Example Labs/Lab", ("name", "Lab"), ("domain", "Example Labs"));

        var result = await Create(perimeter);

        result.Problems().ShouldHaveSingleItem().Code.ShouldBe("ciso_assistant.not_found");
    }

    [Fact]
    public async Task Imports_stored_library_by_urn()
    {
        Respond("POST", $"/api/stored-libraries/{E8Library}/import/", 200, new JsonObject { ["status"] = "success" });
        var framework = new ScopeObject(ScopeKind.Framework, E8, new Dictionary<string, string> { ["version"] = "3" }) { Initial = new Dictionary<string, string> { ["library"] = E8Library } };

        (await Write(framework, ScopeAction.Create, null)).Value();

        Sent().ShouldBe([("POST", $"/api/stored-libraries/{E8Library}/import/")]);
    }

    [Fact]
    public async Task Uploads_custom_library()
    {
        const string library = "urn:example:risk:library:example-baseline";
        List("stored-libraries");
        Respond("POST", "/api/stored-libraries/upload/", 201, new JsonObject { ["urn"] = library });
        var file = await FileAsync("example-baseline.yaml", "urn: urn:example:risk:library:example-baseline\n");
        var framework = new ScopeObject(ScopeKind.Framework, "urn:example:risk:framework:example-baseline", new Dictionary<string, string>())
        {
            Initial = new Dictionary<string, string> { ["library"] = library, ["file"] = "example-baseline.yaml" },
            Files = [file],
        };

        (await Write(framework, ScopeAction.Create, null)).Value();

        // CISO Assistant v4.3.4 loads an uploaded library; importing it again is rejected as "already been loaded".
        Sent().ShouldBe([("POST", "/api/stored-libraries/upload/")]);
        var upload = Logged("POST", "/api/stored-libraries/upload/");
        upload.Body.ShouldBe("urn: urn:example:risk:library:example-baseline\n");
        upload.Headers!["Content-Disposition"].ShouldHaveSingleItem().ShouldContain("filename=example-baseline.yaml");
    }

    [Fact]
    public async Task Updates_custom_library_to_a_raised_version()
    {
        // s15-01: CISO Assistant stores the newer version on upload, then updates its loaded copy on request (GET, by URN).
        const string library = "urn:example:risk:library:example-risk";
        Respond("POST", "/api/stored-libraries/upload/", 201, new JsonObject { ["urn"] = library });
        Respond("GET", $"/api/loaded-libraries/{library}/update/", 200, new JsonObject { ["status"] = "success" });
        var file = await FileAsync("example-risk.yaml", "urn: urn:example:risk:library:example-risk\nversion: 2\n");
        var declared = new ScopeObject(ScopeKind.Framework, library, new Dictionary<string, string> { ["version"] = "2" })
        {
            Initial = new Dictionary<string, string> { ["library"] = library, ["file"] = "example-risk.yaml" },
            Files = [file],
        };

        (await Write(declared, ScopeAction.Update, null)).Value();

        Sent().ShouldBe([("POST", "/api/stored-libraries/upload/")]);
        Logged("GET", $"/api/loaded-libraries/{library}/update/").ShouldNotBeNull();
    }

    [Fact]
    public async Task System_sends_its_security_and_recovery_objectives_only_when_declared()
    {
        // s15-04: objectives on CISO Assistant's 0-4 scale, recovery objectives in seconds; left out when the scope declares none, so the UI's stay.
        List("assets");
        Respond("POST", "/api/assets/", 201, new JsonObject { ["id"] = "a-new" });
        var system = Declared(ScopeKind.System, "Example tablets", ("type", "supporting"), ("domain", "Example Pty Ltd"),
            ("objectives", "availability=2, confidentiality=3"), ("recovery", "mtd=3d, rto=4h"));

        (await Create(system)).Value();

        var posted = JsonNode.Parse(Logged("POST", "/api/assets/").Body!)!;
        Json(posted["security_objectives"]!).ShouldBe(Json(new JsonObject
        {
            ["objectives"] = new JsonObject
            {
                ["availability"] = new JsonObject { ["value"] = 2, ["is_enabled"] = true },
                ["confidentiality"] = new JsonObject { ["value"] = 3, ["is_enabled"] = true },
            },
        }));
        Json(posted["disaster_recovery_objectives"]!).ShouldBe(Json(new JsonObject
        {
            ["objectives"] = new JsonObject { ["mtd"] = new JsonObject { ["value"] = 259200 }, ["rto"] = new JsonObject { ["value"] = 14400 } },
        }));
    }

    [Fact]
    public async Task An_asset_assessment_update_leaves_the_flags_set_by_checks()
    {
        // s15-05: a flag named by checks is not declared; each run sets it, so the scope's update must not reset it to false.
        List("business-impact-analysis", new JsonObject { ["id"] = "bia-1", ["ref_id"] = "EX-BIA-2026" });
        List("assets", new JsonObject { ["id"] = "a-365", ["name"] = "Example 365" });
        List("applied-controls");
        List("asset-assessments", new JsonObject { ["id"] = "aa-1", ["asset"] = new JsonObject { ["id"] = "a-365", ["str"] = "Example 365" } });
        Respond("PATCH", "/api/asset-assessments/aa-1/", 200, new JsonObject { ["id"] = "aa-1" });
        var assessment = Declared(ScopeKind.AssetAssessment, "EX-BIA-2026/Example 365",
            ("bia", "EX-BIA-2026"), ("system", "Example 365"), ("recovery_documented", "true"), ("observation", "Restores are tested monthly."));

        (await _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(assessment.Kind, assessment.Key, ScopeAction.Update, [new FieldChange("observation", null, "Restores are tested monthly.")], State), assessment, "Global"), _ct)).Value();

        var patched = JsonNode.Parse(Logged("PATCH", "/api/asset-assessments/aa-1/").Body!)!.AsObject();
        patched["recovery_documented"]!.GetValue<bool>().ShouldBeTrue();
        patched.ContainsKey("recovery_tested").ShouldBeFalse();
        patched.ContainsKey("recovery_targets_met").ShouldBeFalse();
    }

    [Fact]
    public async Task Creates_bia_asset_assessment_and_thresholds()
    {
        // s15-04 (BiaClientTests in the plan): the BIA on its matrix, one asset assessment per system, thresholds by seconds and impact position.
        List("risk-matrices", new JsonObject { ["id"] = "rm-4x4", ["urn"] = "urn:example:risk:matrix:example-4x4" });
        List("business-impact-analysis", new JsonObject { ["id"] = "bia-1", ["ref_id"] = "EX-BIA-2026" });
        List("assets", new JsonObject { ["id"] = "a-365", ["name"] = "Example 365" }, new JsonObject { ["id"] = "a-laptops", ["name"] = "Example laptops" });
        List("applied-controls", new JsonObject { ["id"] = "ac-backup", ["ref_id"] = "AC-BACKUP" });
        List("asset-assessments", new JsonObject { ["id"] = "aa-1", ["asset"] = new JsonObject { ["id"] = "a-365", ["str"] = "Example 365" } });
        List("escalation-thresholds");
        List("terminologies", new JsonObject { ["id"] = "t-rep", ["name"] = "Reputation", ["field_path"] = "qualifications" });
        Respond("POST", "/api/business-impact-analysis/", 201, new JsonObject { ["id"] = "bia-new" });
        Respond("POST", "/api/asset-assessments/", 201, new JsonObject { ["id"] = "aa-new" });
        Respond("POST", "/api/escalation-thresholds/", 201, new JsonObject { ["id"] = "et-new" });
        var bia = Declared(ScopeKind.Bia, "EX-BIA-2026-NEW",
            ("name", "Example BIA"), ("domain", "Example Pty Ltd"), ("perimeter", "Corporate IT"), ("matrix", "urn:example:risk:matrix:example-4x4"),
            ("version", "1.0"), ("status", "in_progress"), ("due_date", "2026-12-31"), ("authors", "it@example.com"));
        var assessment = Declared(ScopeKind.AssetAssessment, "EX-BIA-2026/Example laptops",
            ("bia", "EX-BIA-2026"), ("system", "Example laptops"), ("dependencies", "Example 365"), ("controls", "AC-BACKUP"),
            ("recovery_documented", "true"), ("recovery_tested", "false"), ("recovery_targets_met", "false"), ("observation", "Staff use phones."));
        var threshold = Declared(ScopeKind.EscalationThreshold, "EX-BIA-2026/Example 365/1d",
            ("assessment", "EX-BIA-2026/Example 365"), ("point_in_time", "86400"), ("impact", "Major"), ("qualifications", "Reputation"), ("justification", "Customers notice.")) with
        {
            Levels = new Dictionary<string, int> { ["quali_impact"] = 2 },
        };

        (await Create(bia)).Value();
        (await Create(assessment)).Value();
        (await Create(threshold)).Value();

        Posted("business-impact-analysis").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Example BIA",
            ["ref_id"] = "EX-BIA-2026-NEW",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-ex",
            ["perimeter"] = "p-corp",
            ["risk_matrix"] = "rm-4x4",
            ["version"] = "1.0",
            ["status"] = "in_progress",
            ["due_date"] = "2026-12-31",
            ["authors"] = new JsonArray("act-it"),
            ["reviewers"] = new JsonArray(),
        }));
        Posted("asset-assessments").ShouldBe(Json(new JsonObject
        {
            ["bia"] = "bia-1",
            ["asset"] = "a-laptops",
            ["dependencies"] = new JsonArray("a-365"),
            ["associated_controls"] = new JsonArray("ac-backup"),
            ["recovery_documented"] = true,
            ["recovery_tested"] = false,
            ["recovery_targets_met"] = false,
            ["observation"] = "Staff use phones.",
        }));
        Posted("escalation-thresholds").ShouldBe(Json(new JsonObject
        {
            ["asset_assessment"] = "aa-1",
            ["point_in_time"] = 86400,
            ["quali_impact"] = 2,
            ["qualifications"] = new JsonArray("t-rep"),
            ["justification"] = "Customers notice.",
        }));
    }

    [Fact]
    public async Task Creates_risk_study_on_its_matrix_with_actors_and_marker()
    {
        // s15-02 (RiskClientTests in the plan): a risk study is a risk assessment in its domain and perimeter, on a matrix found by URN.
        List("risk-matrices", new JsonObject { ["id"] = "rm-4x4", ["urn"] = "urn:example:risk:matrix:example-4x4" });
        List("risk-assessments");
        Respond("POST", "/api/risk-assessments/", 201, new JsonObject { ["id"] = "ra-new" });
        var study = Declared(ScopeKind.RiskStudy, "EX-RISK-2026",
            ("name", "Example Pty Ltd risk register 2026"), ("domain", "Example Pty Ltd"), ("perimeter", "Corporate IT"), ("matrix", "urn:example:risk:matrix:example-4x4"),
            ("status", "in_progress"), ("eta", "2026-12-31"), ("authors", "it@example.com"));

        (await Create(study)).Value();

        Posted("risk-assessments").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Example Pty Ltd risk register 2026",
            ["ref_id"] = "EX-RISK-2026",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-ex",
            ["perimeter"] = "p-corp",
            ["risk_matrix"] = "rm-4x4",
            ["status"] = "in_progress",
            ["eta"] = "2026-12-31",
            ["authors"] = new JsonArray("act-it"),
            ["reviewers"] = new JsonArray(),
        }));
    }

    [Fact]
    public async Task Creates_risk_scenario_with_links_and_ratings_by_position()
    {
        List("risk-assessments", new JsonObject { ["id"] = "ra-1", ["ref_id"] = "EX-RISK-2026" });
        List("threats", new JsonObject { ["id"] = "th-2", ["urn"] = "urn:example:risk:threat:example-risk:thr-02" });
        List("applied-controls", new JsonObject { ["id"] = "ac-backup", ["ref_id"] = "AC-BACKUP" }, new JsonObject { ["id"] = "ac-mfa", ["ref_id"] = "AC-MFA" });
        List("risk-scenarios");
        Respond("POST", "/api/risk-scenarios/", 201, new JsonObject { ["id"] = "rs-new" });
        var scenario = Declared(ScopeKind.RiskScenario, "EX-RISK-2026/R-01",
            ("ref_id", "R-01"), ("name", "Ransomware encrypts the file shares"), ("study", "EX-RISK-2026"), ("systems", "Example 365"),
            ("threats", "urn:example:risk:threat:example-risk:thr-02"), ("existing_controls", "AC-BACKUP"), ("planned_controls", "AC-MFA"),
            ("current_probability", "Possible"), ("current_impact", "Major"), ("treatment", "mitigate"), ("justification", "Tested monthly.")) with
        {
            Levels = new Dictionary<string, int> { ["current_proba"] = 2, ["current_impact"] = 2 },
        };

        (await Create(scenario)).Value();

        Posted("risk-scenarios").ShouldBe(Json(new JsonObject
        {
            ["ref_id"] = "R-01",
            ["name"] = "Ransomware encrypts the file shares",
            ["description"] = $"[hound scope {State}]",
            ["risk_assessment"] = "ra-1",
            ["assets"] = new JsonArray("a-365"),
            ["threats"] = new JsonArray("th-2"),
            ["existing_applied_controls"] = new JsonArray("ac-backup"),
            ["applied_controls"] = new JsonArray("ac-mfa"),
            ["current_proba"] = 2,
            ["current_impact"] = 2,
            ["residual_proba"] = -1,
            ["residual_impact"] = -1,
            ["treatment"] = "mitigate",
            ["justification"] = "Tested monthly.",
        }));
    }

    [Fact]
    public async Task Creates_security_exception_with_links_and_never_a_status()
    {
        // s15-03: the exception carries its links to systems, controls and rows; its approval stays with an approver in CISO Assistant.
        List("security-exceptions");
        List("applied-controls", new JsonObject { ["id"] = "ac-mfa", ["ref_id"] = "AC-MFA" });
        List("compliance-assessments", new JsonObject { ["id"] = "ca-base", ["ref_id"] = "EXAMPLE-BASELINE-2026" });
        _ca.Server.Given(Request.Create().WithPath("/api/compliance-assessments/ca-base/object/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["id"] = "ca-base", ["selected_implementation_groups"] = new JsonArray() }.ToJsonString()));
        List("requirement-assessments", new JsonObject { ["id"] = "ra-1", ["requirement"] = new JsonObject { ["urn"] = "urn:example:req:ex-1.1", ["ref_id"] = "EX-1.1" } });
        Respond("POST", "/api/security-exceptions/", 201, new JsonObject { ["id"] = "se-new" });
        var exception = Declared(ScopeKind.SecurityException, "EXC-01",
            ("name", "Legacy finance app without MFA"), ("domain", "Example Pty Ltd"), ("severity", "medium"), ("owners", "it@example.com"),
            ("expiration_date", "2027-03-31"), ("observation", "Office network only."), ("systems", "Example 365"), ("controls", "AC-MFA"),
            ("requirements", "EXAMPLE-BASELINE-2026/EX-1.1"));

        (await Create(exception)).Value();

        Posted("security-exceptions").ShouldBe(Json(new JsonObject
        {
            ["ref_id"] = "EXC-01",
            ["name"] = "Legacy finance app without MFA",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-ex",
            ["severity"] = 2,
            ["owners"] = new JsonArray("act-it"),
            ["expiration_date"] = "2027-03-31",
            ["observation"] = "Office network only.",
            ["assets"] = new JsonArray("a-365"),
            ["applied_controls"] = new JsonArray("ac-mfa"),
            ["requirement_assessments"] = new JsonArray("ra-1"),
        }));
    }

    [Fact]
    public async Task Creates_risk_acceptance_request_without_a_state()
    {
        List("risk-assessments", new JsonObject { ["id"] = "ra-1", ["ref_id"] = "EX-RISK-2026" });
        List("risk-scenarios", new JsonObject { ["id"] = "rs-2", ["ref_id"] = "R-02" });
        List("risk-acceptances");
        Respond("POST", "/api/risk-acceptances/", 201, new JsonObject { ["id"] = "rac-new" });
        var acceptance = Declared(ScopeKind.RiskAcceptance, "EX-RISK-2026/R-02",
            ("name", "Accept: The only backup administrator leaves"), ("domain", "Example Pty Ltd"), ("scenario", "EX-RISK-2026/R-02"), ("justification", "Training a second."));

        (await Create(acceptance)).Value();

        Posted("risk-acceptances").ShouldBe(Json(new JsonObject
        {
            ["ref_id"] = "ACC-EX-RISK-2026-R-02",
            ["name"] = "Accept: The only backup administrator leaves",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-ex",
            ["risk_scenarios"] = new JsonArray("rs-2"),
            ["justification"] = "Training a second.",
        }));
    }

    [Fact]
    public async Task Creates_audit_with_groups_actors_and_marker()
    {
        List("frameworks", new JsonObject { ["id"] = "fw-e8", ["urn"] = E8 });
        List("compliance-assessments");
        Respond("POST", "/api/compliance-assessments/", 201, new JsonObject { ["id"] = "ca-new" });
        var audit = Declared(ScopeKind.Audit, "EX-E8-2026",
            ("name", "Essential Eight ML2 2026"), ("framework", E8), ("groups", "1, 2"), ("domain", "Example Pty Ltd"), ("perimeter", "Corporate IT"),
            ("eta", "2026-06-30"), ("due_date", "2026-07-31"), ("authors", "it@example.com"));

        (await Create(audit)).Value();

        Posted("compliance-assessments").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Essential Eight ML2 2026",
            ["ref_id"] = "EX-E8-2026",
            ["description"] = $"[hound scope {State}]",
            ["framework"] = "fw-e8",
            ["perimeter"] = "p-corp",
            ["folder"] = "f-ex",
            ["selected_implementation_groups"] = new JsonArray("1", "2"),
            ["eta"] = "2026-06-30",
            ["due_date"] = "2026-07-31",
            ["authors"] = new JsonArray("act-it"),
            ["reviewers"] = new JsonArray(),
        }));
    }

    [Fact]
    public async Task An_audit_names_its_systems_as_assets()
    {
        // s15-D2: an audit's systems were never written, so audits in CISO Assistant showed no systems.
        List("frameworks", new JsonObject { ["id"] = "fw-e8", ["urn"] = E8 });
        List("compliance-assessments");
        List("assets", new JsonObject { ["id"] = "a-365", ["name"] = "Example 365" }, new JsonObject { ["id"] = "a-laptops", ["name"] = "Example laptops" });
        Respond("POST", "/api/compliance-assessments/", 201, new JsonObject { ["id"] = "ca-new" });
        var audit = Declared(ScopeKind.Audit, "EX-E8-2026",
            ("name", "Essential Eight ML2 2026"), ("framework", E8), ("groups", "2"), ("domain", "Example Pty Ltd"), ("perimeter", "Corporate IT"),
            ("systems", "Example 365, Example laptops"));

        (await Create(audit)).Value();

        JsonNode.Parse(Posted("compliance-assessments"))!["assets"]!.ToJsonString().ShouldBe("[\"a-365\",\"a-laptops\"]");
    }

    [Fact]
    public async Task Opens_a_period_audit_from_the_previous_period_as_baseline()
    {
        List("frameworks", new JsonObject { ["id"] = "fw-e8", ["urn"] = E8 });
        List("compliance-assessments", new JsonObject { ["id"] = "ca-q3", ["ref_id"] = "EX-E8-Q1-2027FY-BACKUP" });
        Respond("POST", "/api/compliance-assessments/", 201, new JsonObject { ["id"] = "ca-q4" });
        var period = Declared(ScopeKind.Audit, "EX-E8-Q2-2027FY-BACKUP",
            ("name", "Backups Q2-2027FY"), ("framework", E8), ("groups", "1, 2"), ("domain", "Example Pty Ltd"), ("perimeter", "Corporate IT"),
            ("eta", "2026-12-31"), ("status", "in_progress")) with
        {
            Initial = new Dictionary<string, string> { ["baseline"] = "EX-E8-Q1-2027FY-BACKUP" },
        };

        (await Create(period)).Value();

        var posted = JsonNode.Parse(Posted("compliance-assessments"))!;
        (posted["baseline"]!.GetValue<string>(), posted["status"]!.GetValue<string>()).ShouldBe(("ca-q3", "in_progress"));
        posted["is_locked"].ShouldBeNull();
    }

    [Fact]
    public async Task Closing_a_period_sets_its_status_and_lock_in_one_write()
    {
        List("frameworks", new JsonObject { ["id"] = "fw-e8", ["urn"] = E8 });
        List("compliance-assessments", new JsonObject { ["id"] = "ca-q3", ["ref_id"] = "EX-E8-Q1-2027FY-BACKUP" });
        Respond("PATCH", "/api/compliance-assessments/ca-q3/", 200, new JsonObject());
        var period = Declared(ScopeKind.Audit, "EX-E8-Q1-2027FY-BACKUP",
            ("name", "Backups Q1-2027FY"), ("framework", E8), ("domain", "Example Pty Ltd"), ("perimeter", "Corporate IT"), ("status", "done"), ("locked", "true"));

        (await Write(period, ScopeAction.Update, State)).Value();

        Sent().ShouldBe([("PATCH", "/api/compliance-assessments/ca-q3/")]);
        var patched = JsonNode.Parse(Logged("PATCH", "/api/compliance-assessments/ca-q3/").Body!)!;
        (patched["status"]!.GetValue<string>(), patched["is_locked"]!.GetValue<bool>()).ShouldBe(("done", true));
        patched["baseline"].ShouldBeNull();
    }

    [Fact]
    public async Task An_audit_without_a_ref_id_is_found_by_its_name_and_given_the_ref_id_made_from_it()
    {
        List("frameworks", new JsonObject { ["id"] = "fw-e8", ["urn"] = E8 });
        List("compliance-assessments", new JsonObject { ["id"] = "ca-noref", ["ref_id"] = "", ["name"] = "Example review 2026" });
        Respond("PATCH", "/api/compliance-assessments/ca-noref/", 200, new JsonObject());
        var audit = Declared(ScopeKind.Audit, "EXAMPLE-REVIEW-2026", ("name", "Example review 2026"), ("framework", E8), ("domain", "Example Pty Ltd"), ("perimeter", "Corporate IT"));

        (await Write(audit, ScopeAction.Update, State)).Value();

        Sent().ShouldBe([("PATCH", "/api/compliance-assessments/ca-noref/")]);
        JsonNode.Parse(Logged("PATCH", "/api/compliance-assessments/ca-noref/").Body!)!["ref_id"]!.GetValue<string>().ShouldBe("EXAMPLE-REVIEW-2026");
    }

    [Fact]
    public async Task Updates_document_and_uploads_only_new_files()
    {
        var kept = await FileAsync("identity-policy.pdf", "policy v1");
        var added = await FileAsync("identity-standard.pdf", "standard v1");
        List("evidences", new JsonObject { ["id"] = "ev-policy", ["ref_id"] = "identity-policy", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" } });
        List("evidence-revisions", new JsonObject { ["id"] = "rev-1", ["attachment_hash"] = kept.Sha256 });
        Respond("PATCH", "/api/evidences/ev-policy/", 200, new JsonObject());
        Respond("POST", "/api/evidence-revisions/", 201, new JsonObject { ["id"] = "rev-2" });
        var document = Declared(ScopeKind.Document, "identity-policy", ("name", "Identity policy"), ("domain", "Example Pty Ltd"), ("hashes", $"{kept.Sha256}, {added.Sha256}")) with { Files = [kept, added] };

        (await Write(document, ScopeAction.Update, State)).Value();

        Sent().ShouldBe([("PATCH", "/api/evidences/ev-policy/"), ("POST", "/api/evidence-revisions/")]);
        Json(JsonNode.Parse(Logged("PATCH", "/api/evidences/ev-policy/").Body!)!).ShouldBe(Json(new JsonObject
        {
            ["name"] = "Identity policy",
            ["ref_id"] = "identity-policy",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-ex",
        }));
        Logged("POST", "/api/evidence-revisions/").Body!.ShouldContain("identity-standard.pdf");
    }

    [Fact]
    public async Task Creates_control_with_owners_systems_documents_and_initial_status()
    {
        List("applied-controls");
        List("evidences", new JsonObject { ["id"] = "ev-policy", ["ref_id"] = "identity-policy" });
        Respond("POST", "/api/applied-controls/", 201, new JsonObject { ["id"] = "ac-new" });
        var control = Declared(ScopeKind.Control, "AC-MFA",
            ("name", "Multi-factor authentication"), ("domain", "Example Pty Ltd"), ("owner", "it@example.com"), ("priority", "1"), ("category", "technical"),
            ("systems", "Example 365"), ("documents", "identity-policy")) with
        { Initial = new Dictionary<string, string> { ["status"] = "active" } };

        (await Create(control)).Value();

        Posted("applied-controls").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Multi-factor authentication",
            ["ref_id"] = "AC-MFA",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-ex",
            ["owner"] = new JsonArray("act-it"),
            ["priority"] = 1,
            ["category"] = "technical",
            ["assets"] = new JsonArray("a-365"),
            ["evidences"] = new JsonArray("ev-policy"),
            ["status"] = "active",
        }));
    }

    [Fact]
    public async Task Updating_a_control_clears_the_priority_and_eta_the_scope_no_longer_declares()
    {
        List("applied-controls", new JsonObject { ["id"] = "ac-backup", ["ref_id"] = "AC-BACKUP" });
        Respond("PATCH", "/api/applied-controls/ac-backup/", 200, new JsonObject());
        var control = Declared(ScopeKind.Control, "AC-BACKUP", ("name", "Daily backups"), ("domain", "Example Pty Ltd"));
        var change = new ScopeChange(ScopeKind.Control, "AC-BACKUP", ScopeAction.Update, [new FieldChange("priority", "1", null), new FieldChange("eta", "2026-12-31", null)], State);

        (await _ca.Writer.WriteAsync(new ScopeWrite(change, control, "Global"), _ct)).Value();

        var patched = JsonNode.Parse(Logged("PATCH", "/api/applied-controls/ac-backup/").Body!)!.AsObject();
        (patched.ContainsKey("priority"), patched["priority"]).ShouldBe((true, null));
        (patched.ContainsKey("eta"), patched["eta"]).ShouldBe((true, null));
        patched.ContainsKey("category").ShouldBeFalse("a field the change does not clear is left as it is");
    }

    [Fact]
    public async Task Writes_row_results_and_links_to_the_in_group_rows()
    {
        List("compliance-assessments", new JsonObject { ["id"] = "ca-e8", ["ref_id"] = "EX-E8-2026" });
        _ca.Server.Given(Request.Create().WithPath("/api/compliance-assessments/ca-e8/object/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["id"] = "ca-e8", ["selected_implementation_groups"] = new JsonArray("1", "2") }.ToJsonString()));
        List("requirement-assessments",
            new JsonObject { ["id"] = "ra-1504-ml2", ["requirement"] = new JsonObject { ["urn"] = $"{E8}:ml2:ism-1504", ["ref_id"] = "ISM-1504", ["implementation_groups"] = new JsonArray("2") } },
            new JsonObject { ["id"] = "ra-1504-ml3", ["requirement"] = new JsonObject { ["urn"] = $"{E8}:ml3:ism-1504", ["ref_id"] = "ISM-1504", ["implementation_groups"] = new JsonArray("3") } });
        List("applied-controls", new JsonObject { ["id"] = "ac-mfa", ["ref_id"] = "AC-MFA" });
        List("evidences", new JsonObject { ["id"] = "ev-policy", ["ref_id"] = "identity-policy" });
        Respond("PATCH", "/api/requirement-assessments/ra-1504-ml2/", 200, new JsonObject());
        var row = Declared(ScopeKind.Assessment, "EX-E8-2026/ISM-1504", ("status", "done"), ("result", "compliant"), ("observation", "Checked."), ("controls", "AC-MFA"), ("documents", "identity-policy"));

        (await Write(row, ScopeAction.Update, null)).Value();

        Sent().ShouldBe([("PATCH", "/api/requirement-assessments/ra-1504-ml2/")]);
        Json(JsonNode.Parse(Logged("PATCH", "/api/requirement-assessments/ra-1504-ml2/").Body!)!).ShouldBe(Json(new JsonObject
        {
            ["status"] = "done",
            ["result"] = "compliant",
            ["observation"] = "Checked.",
            ["applied_controls"] = new JsonArray("ac-mfa"),
            ["evidences"] = new JsonArray("ev-policy"),
        }));
    }

    private Task<Core.Results.Result<Core.Results.Unit>> Write(ScopeObject declared, ScopeAction action, string? state) =>
        _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(declared.Kind, declared.Key, action, [], state), declared, "Global"), _ct);

    private async Task<ScopeFile> FileAsync(string name, string content)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        await File.WriteAllTextAsync(path, content, _ct);
        return new ScopeFile(path, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))));
    }

    /// <summary>The writes sent, in order: every request but a GET.</summary>
    private (string Method, string Path)[] Sent() =>
        [.. _ca.Server.LogEntries.Where(e => e.RequestMessage!.Method != "GET").Select(e => (e.RequestMessage!.Method, e.RequestMessage.Path))];

    private WireMock.IRequestMessage Logged(string method, string path) =>
        _ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == method && e.RequestMessage.Path == path).RequestMessage!;

    private void Respond(string method, string path, int status, JsonObject body) =>
        _ca.Server.Given(Request.Create().WithPath(path).UsingMethod(method)).RespondWith(Response.Create().WithStatusCode(status).WithBody(body.ToJsonString()));

    private Task<Core.Results.Result<Core.Results.Unit>> Create(ScopeObject declared) =>
        _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(declared.Kind, declared.Key, ScopeAction.Create, [], State), declared, "Global"), _ct);

    private string Posted(string resource) =>
        Json(JsonNode.Parse(_ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == "POST" && e.RequestMessage.Path == $"/api/{resource}/").RequestMessage!.Body!)!);

    /// <summary>A JSON object as text with its keys sorted, so key order does not matter.</summary>
    private static string Json(JsonNode node) =>
        new JsonObject([.. node.AsObject().OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()))]).ToJsonString();

    private static ScopeObject Declared(ScopeKind kind, string key, params (string Field, string Value)[] fields) =>
        new(kind, key, fields.ToDictionary(f => f.Field, f => f.Value, StringComparer.Ordinal));

    private static JsonObject Folder(string id, string name, string? parent) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["parent_folder"] = parent is null ? null : new JsonObject { ["id"] = parent, ["str"] = "parent" },
    };

    private void List(string resource, params JsonObject[] items) =>
        _ca.Server.Given(Request.Create().WithPath($"/api/{resource}/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = items.Length, ["results"] = new JsonArray(items) }.ToJsonString()));
}
