using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>s09-34: one table of ref ID conventions, used by every prompt and by scope validation.</summary>
public class RefIdConventionTests
{
    [Theory]
    [InlineData(RefIdKind.Supplier, "SUP-")]
    [InlineData(RefIdKind.Solution, "SOL-")]
    [InlineData(RefIdKind.Contract, "CTR-")]
    [InlineData(RefIdKind.System, "SYS-")]
    [InlineData(RefIdKind.Control, "AC-")]
    [InlineData(RefIdKind.Perimeter, "PER-")]
    [InlineData(RefIdKind.EntityAssessment, "EA-")]
    public void Prefix_and_allowed_characters_per_kind(RefIdKind kind, string prefix)
    {
        RefIds.Prefix(kind).ShouldBe(prefix);

        // Only the part after the prefix is typed; it is upper-cased. Given in full, it is kept.
        RefIds.Complete(kind, "renewal-2026").ShouldBe($"{prefix}RENEWAL-2026");
        RefIds.Complete(kind, $"{prefix.ToLowerInvariant()}renewal").ShouldBe($"{prefix}RENEWAL");

        // Anything else is not a ref ID: spaces, other characters, nothing after the prefix.
        RefIds.Complete(kind, "my renewal").ShouldBeNull();
        RefIds.Complete(kind, "renewal_2026").ShouldBeNull();
        RefIds.Complete(kind, "").ShouldBeNull();
        RefIds.Complete(kind, prefix).ShouldBeNull();

        RefIds.Follows(kind, $"{prefix}RENEWAL-2026").ShouldBeTrue();
        RefIds.Follows(kind, "RENEWAL").ShouldBeFalse();
        RefIds.Follows(kind, $"{prefix}Renewal").ShouldBeFalse();
        RefIds.Follows(kind, $"{prefix}MY RENEWAL").ShouldBeFalse();
        RefIds.Rule(kind).ShouldContain(prefix);
        RefIds.Suggest(kind, "My renewal").ShouldBe($"{prefix}MY-RENEWAL");
    }

    [Fact]
    public void Audits_keep_their_own_form()
    {
        RefIds.Follows(RefIdKind.Audit, "EX-E8-2026").ShouldBeTrue();
        RefIds.Follows(RefIdKind.Audit, "OFFICE-FIREWALL-ESSENTIAL-EIGHT-2023").ShouldBeTrue();
        RefIds.Follows(RefIdKind.Audit, "Example audit").ShouldBeFalse();
        RefIds.Follows(RefIdKind.Audit, "ex-e8-2026").ShouldBeFalse();
        RefIds.Follows(RefIdKind.Audit, "AUDIT").ShouldBeFalse();
        RefIds.Rule(RefIdKind.Audit).ShouldContain("EX-E8-2026");
    }

    [Fact]
    public void Every_kind_is_in_the_table_once()
    {
        RefIds.Table.Select(c => c.Kind).Order().ShouldBe(Enum.GetValues<RefIdKind>().Order());
    }
}
