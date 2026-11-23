using EvidenceHound.Onboarding.Explain;

namespace EvidenceHound.Onboarding.Tests.Explain;

/// <summary>
/// <c>hound explain</c>: the plan or diff is read in code (counts, objects, what needs a careful look). No model is asked.
/// </summary>
public sealed class ExplainTests
{
    internal const string Plan = """
        ## Scope plan

        2 to create, 1 to update, 1 to delete. No drift.

        | Change | Kind | Object | Fields |
        | --- | --- | --- | --- |
        | Create | audit | EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023 | name: EXAMPLE-ASSETS essential-eight-2023 |
        | Create | assessment | EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023/EX-2 | result: not_applicable |
        | Update | contract | CTR-EXAMPLE | status: draft → active |
        | Delete | control | CTL-OLD |  |

        ### Access review

        | User | Note |
        | --- | --- |
        | alex@corp.example.org | MFA is off |
        """;

    [Fact]
    public void Summary_counts_match_the_plan()
    {
        var facts = ChangeReader.Read(Plan, "plan.md").Value();

        facts.CountsText.ShouldBe("2 to create, 1 to update, 1 to delete, 0 drifted.");
        facts.Objects.ShouldBe(["EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023", "EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023/EX-2", "CTR-EXAMPLE", "CTL-OLD"]);
        facts.Topics.ShouldBe([ChangeTopic.Audit, ChangeTopic.NotApplicable, ChangeTopic.ThirdParty], ignoreOrder: true);
        facts.Flags.ShouldContain("Deletes control CTL-OLD.");
        facts.Flags.ShouldContain("Marks EXAMPLE-ASSETS-ESSENTIAL-EIGHT-2023/EX-2 not applicable.");

        var text = Explanation.Render(facts);

        text.ShouldContain("The plan is the source of truth: 2 to create, 1 to update, 1 to delete, 0 drifted.");
        text.ShouldContain("- Deletes control CTL-OLD.");
        text.ShouldNotContain("### Summary");

        // A JSON scope plan and a diff give their own counts.
        const string json = """{"changes":[{"kind":"contract","key":"CTR-EXAMPLE","action":"delete","fields":[],"state":"a1b2"}],"drift":[]}""";
        ChangeReader.Read(json, "plan.json").Value().CountsText.ShouldBe("0 to create, 0 to update, 1 to delete, 0 drifted.");
        var diff = ChangeReader.Diff("result: not_assessed\n", "result: not_applicable\n", "scope/audits/example.yaml");
        var diffFacts = ChangeReader.Read(diff, "diff").Value();
        diffFacts.CountsText.ShouldBe("1 file changed, 1 line added, 1 line removed.");
        diffFacts.Flags.ShouldContain("Marks 1 requirement(s) not applicable in scope/audits/example.yaml.");
        diffFacts.Topics.ShouldBe([ChangeTopic.Audit, ChangeTopic.NotApplicable], ignoreOrder: true);
    }

    [Fact]
    public void System_without_an_owner_is_flagged()
    {
        const string plan = """
            ## Scope plan

            2 to create, 1 to update, 0 to delete. No drift.

            | Change | Kind | Object | Fields |
            | --- | --- | --- | --- |
            | Create | system | SYS-NEW | name: New system<br>owner: <br>type: primary |
            | Create | system | SYS-OWNED | name: Owned<br>owner: it@example.com |
            | Update | system | SYS-OLD | owner: it@example.com → (none) |
            """;

        var flags = ChangeReader.Read(plan, "plan.md").Value().Flags;

        flags.ShouldContain("System SYS-NEW has no owner.");
        flags.ShouldContain("System SYS-OLD has no owner.");
        flags.ShouldNotContain(f => f.Contains("SYS-OWNED", StringComparison.Ordinal));

        const string json = """{"changes":[{"kind":"system","key":"SYS-NEW","action":"create","fields":[{"field":"owner","old":null,"new":null}]}],"drift":[]}""";
        ChangeReader.Read(json, "plan.json").Value().Flags.ShouldContain("System SYS-NEW has no owner.");
    }

    [Fact]
    public void Check_removed_from_a_profile_but_still_mapped_is_flagged()
    {
        const string diff = """
            --- a/profiles/example.yaml
            +++ b/profiles/example.yaml
            @@ -1,4 +1,2 @@
             checks:
            -  - id: devices.supported_os
            -    rule: all_pass
               - id: devices.encrypted
            """;
        var mappings = new Dictionary<string, string>
        {
            ["mappings/example.yaml"] = "requirements:\n  - ref: EX-4\n    checks: [devices.supported_os]\n",
            ["mappings/other.yaml"] = "requirements:\n  - ref: EX-5\n    checks: [devices.encrypted]\n",
        };

        var facts = ChangeReader.Read(diff, "diff", mappings).Value();

        facts.Flags.ShouldContain("Removes check devices.supported_os from profiles/example.yaml, but mappings/example.yaml still uses it.");
        facts.Flags.ShouldNotContain(f => f.Contains("other.yaml", StringComparison.Ordinal));
        ChangeReader.Read(diff, "diff").Value().Flags.ShouldNotContain(f => f.Contains("still uses", StringComparison.Ordinal));
    }
}
