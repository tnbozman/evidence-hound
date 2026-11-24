namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s10-04: the manual evidence guide takes a small team from the evidence plan to reminders and attested results, and the template index
/// names every Essential Eight template.
/// </summary>
public sealed class ManualEvidenceGuideTests
{
    private static string Docs => Path.Combine(ExampleRepoTests.RepoRoot, "docs");

    [Fact]
    public void The_guide_covers_the_plan_runbooks_mail_the_grc_steps_and_the_limits()
    {
        var guide = File.ReadAllText(Path.Combine(Docs, "manual-evidence.md"));

        foreach (var section in new[]
        {
            "## The evidence plan", "## Owner contacts", "## Runbooks", "## Turning mail on", "## The GRC team's steps", "## Attesting and publishing",
            "## Limits",
        })
        {
            guide.ShouldContain(section);
        }

        foreach (var term in new[]
        {
            "scope/evidence-plan.yaml", "EMAIL_", "notifications_enable_mailing", "disabled_email_templates", "task_node_overdue", "hound manual propose",
            "hound manual approve", "examples/manual-sources/essential-eight/", "scope.result_conflict",
        })
        {
            guide.ShouldContain(term);
        }
    }

    [Fact]
    public void The_template_index_lists_every_essential_eight_template_by_strategy()
    {
        var index = File.ReadAllText(Path.Combine(Docs, "collectors", "manual-templates.md"));

        foreach (var template in Directory.GetDirectories(Path.Combine(ExampleRepoTests.RepoRoot, "examples", "manual-sources", "essential-eight")).Select(Path.GetFileName))
        {
            index.ShouldContain($"essential-eight/{template}/");
        }
    }
}
