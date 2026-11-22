using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>What <c>scope plan</c> tells the reviewer about access: people without MFA, and service accounts holding more than they need.</summary>
public class AccessReviewTests
{
    private static readonly SourceLine At = new("scope/access.yaml", 1);

    private static readonly ScopeAccess Declared = new(
        [
            new ScopeUser("it@example.com", "Alex", "Example", Active: true, ServiceAccount: null, At),
            new ScopeUser("ceo@example.com", "Sam", "Example", Active: true, ServiceAccount: null, At),
            new ScopeUser("former@example.com", "Robin", "Example", Active: false, ServiceAccount: null, At),
            new ScopeUser("hound-runner@example.com", null, null, Active: true, ServiceAccount: "publish", At),
            new ScopeUser("hound-admin@example.com", null, null, Active: true, ServiceAccount: "apply", At),
            new ScopeUser("new@example.com", "Kim", "Example", Active: true, ServiceAccount: null, At),
        ],
        [],
        []);

    [Fact]
    public void Active_people_without_mfa_are_listed_and_inactive_users_service_accounts_and_users_not_yet_created_are_not()
    {
        var notes = AccessReview.Notes(Declared,
        [
            new UserAccess("it@example.com", MfaEnabled: true, []),
            new UserAccess("ceo@example.com", MfaEnabled: false, []),
            new UserAccess("former@example.com", MfaEnabled: false, []),
            new UserAccess("hound-runner@example.com", MfaEnabled: false, ["BI-RL-ANA on Example Pty Ltd"]),
            new UserAccess("hound-admin@example.com", MfaEnabled: false, ["BI-RL-ADM on Global"]),
        ]);

        notes.ShouldBe([new AccessNote("ceo@example.com", AccessIssue.MfaOff, "has not turned on multi-factor authentication")]);
    }

    [Fact]
    public void A_publishing_service_account_with_more_than_analyst_is_reported_whatever_gave_it_the_role()
    {
        var notes = AccessReview.Notes(Declared,
        [
            new UserAccess("hound-runner@example.com", MfaEnabled: false, ["BI-RL-ADM on Global", "BI-RL-ANA on Example Pty Ltd"]),
            new UserAccess("hound-admin@example.com", MfaEnabled: false, ["BI-RL-ADM on Global"]),
        ]);

        var note = notes.ShouldHaveSingleItem();
        (note.Email, note.Issue).ShouldBe(("hound-runner@example.com", AccessIssue.ServiceAccountExcessRole));
        note.Detail.ShouldBe("holds BI-RL-ADM on Global; a publishing service account needs only analyst on the domains it publishes to");
    }

    [Fact]
    public void Service_account_true_means_a_publishing_account()
    {
        var access = Declared with { Users = [new ScopeUser("hound-runner@example.com", null, null, Active: true, ServiceAccount: "true", At)] };

        AccessReview.Notes(access, [new UserAccess("hound-runner@example.com", MfaEnabled: false, ["BI-RL-DMA on Example Pty Ltd"])])
            .Select(n => n.Issue).ShouldBe([AccessIssue.ServiceAccountExcessRole]);
    }

    [Fact]
    public void The_state_query_reads_access_only_when_the_scope_declares_it()
    {
        var setup = new ScopeSetup("Global", [], []);
        var without = new ScopeDocument(setup, [], []);

        ScopeStateQuery.For(without, []).Access.ShouldBeFalse();
        ScopeStateQuery.For(without with { Access = Declared }, []).Access.ShouldBeTrue();
    }

}
