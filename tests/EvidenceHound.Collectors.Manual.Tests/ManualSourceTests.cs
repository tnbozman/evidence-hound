namespace EvidenceHound.Collectors.Manual.Tests;

public class ManualSourceTests
{
    [Fact]
    public void Reads_every_setting_and_trims_the_lists()
    {
        var source = ManualSource.FromSettings(new CollectorSettings(DropFolder.Settings("/srv/hound/manual/firewall", "*.txt, *.cfg"))).Value();

        source.DropFolder.ShouldBe("/srv/hound/manual/firewall");
        source.Patterns.ShouldBe(["*.txt", "*.cfg"]);
        source.Checks.Select(c => c.Value).ShouldBe([DropFolder.Reviewed, DropFolder.Exported]);
        source.DueDays.ShouldBe(30);
        source.Owner.ShouldBe("Alex Example");
    }

    [Fact]
    public void Accepts_files_matching_a_pattern_but_never_attestation_sidecars()
    {
        var source = ManualSource.FromSettings(new CollectorSettings(DropFolder.Settings("/srv/drop", "*.txt,firewall-??.cfg"))).Value();

        source.Accepts("export.txt").ShouldBeTrue();
        source.Accepts("firewall-01.cfg").ShouldBeTrue();
        source.Accepts("firewall-001.cfg").ShouldBeFalse();
        source.Accepts("notes.md").ShouldBeFalse();
        source.Accepts("export.txt.attest.yaml").ShouldBeFalse();
    }

    [Fact]
    public void Patterns_default_to_every_file()
    {
        var settings = DropFolder.Settings("/srv/drop");
        settings.Remove(ManualSettings.Patterns);

        var source = ManualSource.FromSettings(new CollectorSettings(settings)).Value();

        source.Accepts("anything.pdf").ShouldBeTrue();
        source.Accepts("anything.pdf.attest.yaml").ShouldBeFalse();
    }

    [Fact]
    public void Due_days_must_be_a_positive_whole_number()
    {
        var settings = DropFolder.Settings("/srv/drop");
        settings[ManualSettings.DueDays] = "0";

        var problem = ManualSource.FromSettings(new CollectorSettings(settings)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("manual.setting_invalid");
        problem.Message.ShouldContain(ManualSettings.DueDays);
    }
}
