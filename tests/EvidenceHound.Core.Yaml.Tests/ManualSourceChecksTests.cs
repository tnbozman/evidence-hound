namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s12-01: the checks people attest, read from manual collectors in a config or a manual source template.</summary>
public class ManualSourceChecksTests
{
    [Fact]
    public void Reads_manual_collectors_from_a_config_and_ignores_others()
    {
        var checks = ManualSourceChecks.Read("""
            collectors:
              - name: example-m365
                type: m365
                settings: { checks: identity.mfa.enforced }
              - name: backups
                type: manual
                settings:
                  checks: backup.restore_tested, backup.retained
            """, "hound.yaml").Value();

        checks.Select(c => c.Value).ShouldBe(["backup.restore_tested", "backup.retained"]);
    }

    [Fact]
    public void Reads_a_manual_source_template_list()
    {
        var checks = ManualSourceChecks.Read("""
            - name: ai-governance
              type: manual
              settings:
                checks: ai.policy_approved
            """, "source.yaml").Value();

        checks.Select(c => c.Value).ShouldBe(["ai.policy_approved"]);
    }

    [Fact]
    public void Bad_check_name_names_the_file()
    {
        var result = ManualSourceChecks.Read("- { type: manual, settings: { checks: Not A Check } }", "source.yaml");

        result.Match(_ => "", ps => ps[0].Message).ShouldStartWith("source.yaml: ");
    }
}
