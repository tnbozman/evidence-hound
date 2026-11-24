using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Tests.Checks;
using EvidenceHound.Core.Tests.Scope;

namespace EvidenceHound.Core.Tests.Ssp;

/// <summary>s11-05: an unsigned plan is a draft; a signed version is the reviewed draft, reproduced from its snapshot and checked by hash.</summary>
public class SspVersionTests
{
    private static readonly FrameworkSnapshot EssentialEight = ProgrammeOutlineTests.Snapshot("essential-eight-2023.json");

    [Fact]
    public void Unsigned_plan_is_marked_draft()
    {
        var markdown = SspLayout.Markdown(Document(), SspLayoutTests.Texts());

        markdown.Split('\n')[0].ShouldBe("# System security plan: Example corporate IT (DRAFT)");
        markdown.ShouldContain("> **DRAFT.** Not approved: version 0.1 has no sign-off in `scope/ssp.yaml`.");
        markdown.TrimEnd().Split('\n')[^1].ShouldBe("_DRAFT: not approved._");
    }

    [Fact]
    public void Signed_version_reproduces_its_hash()
    {
        var snapshot = new SspSnapshot(Document(), SspLayoutTests.Texts());
        var draft = SspLayout.Markdown(snapshot.Document, snapshot.Sections);
        var signOff = new SspSignOff("0.1", "ceo@example.com", "2026-11-24", SspVersion.Hash(draft), SspVersion.Hash(SspSnapshotJson.Write(snapshot)));

        var reread = SspSnapshotJson.Read(SspSnapshotJson.Write(snapshot), "ssp-0.1.json").Value();
        var signed = SspVersion.Sign(reread, signOff).Value();

        SspVersion.Hash(SspLayout.Markdown(reread.Document, reread.Sections)).ShouldBe(signOff.Sha256);
        SspSnapshotJson.Write(reread).ShouldBe(SspSnapshotJson.Write(snapshot));
        signed.ShouldNotContain("DRAFT", Case.Sensitive);
        signed.Split('\n')[0].ShouldBe("# System security plan: Example corporate IT");
        signed.ShouldContain($"> Approved by ceo@example.com on 2026-11-24. SHA-256 of the reviewed draft: {signOff.Sha256}.");
    }

    [Fact]
    public void A_changed_snapshot_does_not_sign()
    {
        var snapshot = new SspSnapshot(Document(), SspLayoutTests.Texts());
        var signOff = new SspSignOff("0.1", "ceo@example.com", "2026-11-24", SspVersion.Hash(SspLayout.Markdown(snapshot.Document, snapshot.Sections)), null);
        var changed = snapshot with { Sections = new Dictionary<string, string>(snapshot.Sections) { ["purpose"] = "Something else." } };

        SspVersion.Sign(changed, signOff).Problems().Select(p => p.Code).ShouldBe(["ssp.signoff_hash_mismatch"]);
    }

    [Fact]
    public void A_sign_off_for_another_version_does_not_sign()
    {
        var snapshot = new SspSnapshot(Document(), SspLayoutTests.Texts());
        var signOff = new SspSignOff("1.0", "ceo@example.com", "2026-11-24", SspVersion.Hash(SspLayout.Markdown(snapshot.Document, snapshot.Sections)), null);

        SspVersion.Sign(snapshot, signOff).Problems().Select(p => p.Code).ShouldBe(["ssp.signoff_version_mismatch"]);
    }

    private static SspDocument Document() => SspLayoutTests.Example() with { Annex = SspAnnex.Of(SspLayoutTests.Example(), EssentialEight, ["1"]) };
}
