using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s06-11: what CISO Assistant holds, written out as scope files that plan back to no changes.</summary>
public sealed class ScopeExportTests
{
    private static readonly string E8 = "urn:intuitem:risk:framework:essential-eight-2023";

    private static readonly FrameworkSnapshot Snapshot = new(
        "urn:intuitem:risk:library:essential-eight-2023",
        E8,
        3,
        [new ImplementationGroup("1", "ML1"), new ImplementationGroup("2", "ML2")],
        [
            new SnapshotNode($"{E8}:ism-1173", "ISM-1173", null, ["2"], "MFA for privileged users"),
            new SnapshotNode($"{E8}:ism-1486", "ISM-1486", null, ["1", "2"], "Browsers do not process Java"),
            new SnapshotNode($"{E8}:ism-1504", "ISM-1504", null, ["1", "2"], "MFA for online services"),
        ]);

    [Fact]
    public void Export_then_plan_is_empty()
    {
        var files = ScopeExport.Write(Held());

        var (scope, problems) = Parse(files);
        problems.ShouldBeEmpty(string.Join("\n", problems.Select(p => $"{p.Code} {p.At.File}:{p.At.Line} {p.Message}")));
        ScopeValidator.Validate(scope, Snapshots(files), []).ShouldBeEmpty();
        var declared = ScopeDesired.From(scope, new ScopeInputs(Snapshots(files), null, new Dictionary<string, IReadOnlyList<ScopeFile>>()));
        var plan = ScopePlanner.Plan(declared, Held().Objects, prune: false);
        plan.Changes.ShouldBeEmpty(string.Join("\n", plan.Changes.Select(c => $"{c.Action} {c.Kind} {c.Key}: {string.Join(", ", c.Fields.Select(f => $"{f.Field} {f.Old} -> {f.New}"))}")));
    }

    [Fact]
    public void Hand_written_rows_documents_and_controls_are_exported_but_computed_results_and_collector_evidence_are_not()
    {
        var files = ScopeExport.Write(Held());

        var audit = files["audits/ex-e8-2026.yaml"];
        audit.ShouldContain("ref: ISM-1486");
        audit.ShouldContain("Java is not installed");
        audit.ShouldNotContain("ISM-1504\n    status", Case.Sensitive);
        audit.ShouldNotContain("ev-0001");
        audit.ShouldContain("id: identity-policy");
        audit.ShouldContain("ref_id: AC-MFA");
        files["setup.yaml"].ShouldContain("snapshot: frameworks/essential-eight-2023.json");
        files.Keys.ShouldBe(["audits/ex-e8-2026.yaml", "controls.yaml", "frameworks/essential-eight-2023.json", "setup.yaml"], ignoreOrder: true);
    }

    [Fact]
    public void Output_is_sorted_and_stable()
    {
        var held = Held();
        var shuffled = held with { Objects = [.. held.Objects.Reverse()] };

        var files = ScopeExport.Write(shuffled);

        files.ShouldBe(ScopeExport.Write(held));
        files["setup.yaml"].IndexOf("Example 365", StringComparison.Ordinal).ShouldBeLessThan(files["setup.yaml"].IndexOf("Example laptops", StringComparison.Ordinal));
    }

    [Fact]
    public void An_ai_system_exports_its_register_entry_from_its_labels_and_description()
    {
        // s11-12: the register entry reaches CISO Assistant as labels and description lines, so export reads it back from them.
        var entry = new AiRegistration(AiScreening.Elevated, new DateOnly(2026, 3, 2), AiUse.Customised, AiOversight.HumanOnTheLoop, "The sales lead can switch it off; staff draft by hand.", new SourceLine("setup.yaml", 1))
        {
            ScreeningYes = [1, 5],
            RegisteredOn = new DateOnly(2026, 3, 4),
            Version = "2.1",
            Purpose = "Drafts proposals: faster.",
            IntendedUses = ["Draft proposal text", "Summarise past bids"],
            Limitations = "Not for pricing.",
            Misuse = "None identified.",
            DataSources = ["Past proposals (text)"],
            Stakeholders = ["Sales team", "Customers"],
            ImpactAssessment = AiImpactAssessment.Approved,
            ImpactAssessedOn = new DateOnly(2026, 3, 20),
            InUseFrom = new DateOnly(2026, 4, 1),
        };
        var built = entry with { Use = AiUse.Built, Screening = AiScreening.Normal, ScreeningYes = [], Version = null, IntendedUses = [], Stakeholders = [], Oversight = AiOversight.HumanInTheLoop, ImpactAssessment = AiImpactAssessment.NotRequired, ImpactAssessedOn = null, LastTestedOn = new DateOnly(2026, 5, 1), RetiredOn = new DateOnly(2026, 10, 1) };
        var held = Held();
        held = held with
        {
            Objects =
            [
                .. held.Objects,
                AiSystem("Example proposal assistant", "Drafts proposals.", entry),
                AiSystem("Example ticket triage", null, built),
            ],
        };

        var files = ScopeExport.Write(held);

        files["setup.yaml"].ShouldContain("screening: elevated");
        files["setup.yaml"].ShouldContain("oversight: human_on_the_loop");
        var (scope, problems) = Parse(files);
        problems.ShouldBeEmpty(string.Join("\n", problems.Select(p => $"{p.Code} {p.At.File}:{p.At.Line} {p.Message}")));
        var declared = ScopeDesired.From(scope, new ScopeInputs(Snapshots(files), null, new Dictionary<string, IReadOnlyList<ScopeFile>>()));
        var plan = ScopePlanner.Plan(declared, held.Objects, prune: false);
        plan.Changes.ShouldBeEmpty(string.Join("\n", plan.Changes.Select(c => $"{c.Action} {c.Kind} {c.Key}: {string.Join(", ", c.Fields.Select(f => $"{f.Field} {f.Old} -> {f.New}"))}")));
    }

    [Fact]
    public void Ai_labels_without_a_register_entry_in_the_description_export_no_ai_block()
    {
        var held = Held();
        held = held with
        {
            Objects =
            [
                .. held.Objects,
                Managed(ScopeKind.System, "Example chat", ("ref_id", "SYS-CHAT"), ("type", "primary"), ("description", "Edited by hand in CISO Assistant."), ("domain", "Example Pty Ltd"),
                    ("labels", "ai-oversight-in-the-loop, ai-screening-normal, ai-system, ai-use-built")),
            ],
        };

        var files = ScopeExport.Write(held);

        files["setup.yaml"].ShouldNotContain("    ai:");
        files["setup.yaml"].ShouldContain("Edited by hand in CISO Assistant.");
    }

    [Fact]
    public void Objects_without_a_ref_id_are_given_one()
    {
        ScopeRefIds.For(ScopeKind.Audit, "Essential Eight review 2026").ShouldBe("ESSENTIAL-EIGHT-REVIEW-2026");
        ScopeRefIds.For(ScopeKind.Control, "Multi-factor authentication!").ShouldBe("MULTI-FACTOR-AUTHENTICATION");
        ScopeRefIds.For(ScopeKind.Document, "Backup & restore procedure").ShouldBe("backup-restore-procedure");
    }

    private static (ScopeDocument Scope, IReadOnlyList<ScopeProblem> Problems) Parse(IReadOnlyDictionary<string, string> files)
    {
        var parsed = ScopeLoader.Parse(
            new ScopeSource("setup.yaml", files["setup.yaml"]),
            files.TryGetValue("controls.yaml", out var controls) ? new ScopeSource("controls.yaml", controls) : null,
            [.. files.Where(f => f.Key.StartsWith("audits/", StringComparison.Ordinal)).Select(f => new ScopeSource(f.Key, f.Value))]);
        return (parsed.Scope, parsed.Problems);
    }

    private static Dictionary<string, FrameworkSnapshot> Snapshots(IReadOnlyDictionary<string, string> files) =>
        files.Where(f => f.Key.StartsWith("frameworks/", StringComparison.Ordinal))
            .Select(f => FrameworkSnapshotJson.Read(f.Value, f.Key).Match(s => s, _ => throw new InvalidOperationException(f.Key)))
            .ToDictionary(s => s.FrameworkUrn, StringComparer.Ordinal);

    /// <summary>What the reader returns for a fictional instance that hound set up: every object managed, plus a run's results and evidence.</summary>
    private static ScopeExportState Held() => new(
        "Global",
        [
            Managed(ScopeKind.Framework, E8, ("version", "3")),
            Managed(ScopeKind.Domain, "Example Pty Ltd", ("description", "The whole company.")),
            Managed(ScopeKind.Perimeter, "Example Pty Ltd/Corporate IT", ("name", "Corporate IT"), ("ref_id", "PER-CORP"), ("description", "Everything staff use."), ("domain", "Example Pty Ltd")),
            Managed(ScopeKind.System, "Example 365", ("ref_id", "SYS-365"), ("type", "primary"), ("description", "Mail: files, identity \"and\" devices."), ("owner", "it@example.com"), ("domain", "Example Pty Ltd")),
            Managed(ScopeKind.System, "Example laptops", ("ref_id", "SYS-LAPTOPS"), ("type", "supporting"), ("owner", "it@example.com"), ("domain", "Example Pty Ltd"), ("parents", "Example 365")),
            Managed(ScopeKind.Audit, "EX-E8-2026", ("name", "Essential Eight ML2 2026"), ("framework", E8), ("groups", "1, 2"), ("domain", "Example Pty Ltd"),
                ("perimeter", "Corporate IT"), ("eta", "2026-06-30"), ("due_date", "2026-07-31"), ("authors", "it@example.com"), ("reviewers", "ceo@example.com"), ("systems", "Example 365, Example laptops"), ("status", "in_progress")),
            Managed(ScopeKind.Document, "identity-policy", ("name", "Identity and access policy"), ("domain", "Example Pty Ltd"), ("hashes", new string('a', 64))),
            new ActualObject(new ScopeObject(ScopeKind.Document, "ev-0001", Fields(("name", "mfa-admins.json"))), null),
            Managed(ScopeKind.Control, "AC-MFA", ("name", "Multi-factor authentication"), ("description", "Every user signs in with MFA."), ("domain", "Example Pty Ltd"),
                ("owner", "it@example.com"), ("priority", "1"), ("category", "technical"), ("systems", "Example 365"), ("documents", "ev-health, identity-policy")),
            Row("EX-E8-2026/ISM-1486", ("status", "done"), ("result", "compliant"), ("observation", "Java is not installed on any laptop.\nChecked at the March review.")),
            Row("EX-E8-2026/ISM-1504", ("status", "done"), ("result", "non_compliant"), ("observation", "3 users have no MFA.\n[hound run 20260115T093000Z state 0123456789abcdef]"),
                ("controls", "AC-MFA"), ("documents", "ev-0001, identity-policy")),
            Row("EX-E8-2026/ISM-1173", ("status", "to_do"), ("result", "not_assessed"), ("controls", "AC-MFA"), ("documents", "identity-policy")),
        ],
        [Snapshot]);

    private static ActualObject AiSystem(string name, string? description, AiRegistration entry) =>
        Managed(ScopeKind.System, name, ("ref_id", ScopeRefIds.For(ScopeKind.System, name)), ("type", "primary"), ("description", AiRegister.Description(description, entry)),
            ("owner", "it@example.com"), ("domain", "Example Pty Ltd"), ("labels", string.Join(", ", AiRegister.Labels(entry).Order(StringComparer.Ordinal))));

    private static ActualObject Managed(ScopeKind kind, string key, params (string Name, string? Value)[] fields)
    {
        var held = Fields(fields);
        // hound records an audit's state without its status, which only a period audit declares.
        var recorded = held.Where(f => f.Key != "status").ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        return new ActualObject(new ScopeObject(kind, key, held), kind is ScopeKind.Framework ? null : ScopeMarker.State(kind, recorded));
    }

    private static ActualObject Row(string key, params (string Name, string? Value)[] fields) => new(new ScopeObject(ScopeKind.Assessment, key, Fields(fields)), null);

    private static Dictionary<string, string> Fields(params (string Name, string? Value)[] fields) =>
        fields.Where(f => f.Value is not null).ToDictionary(f => f.Name, f => f.Value!, StringComparer.Ordinal);
}
