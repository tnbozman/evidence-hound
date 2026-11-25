namespace EvidenceHound.Cli.Tests;

/// <summary>s14-03 rehearsal: a user who cannot write the default evidence root got "Unexpected failure" and a stack-trace hint.</summary>
public class AccessDeniedTests
{
    [Fact]
    public void A_denied_path_is_explained_with_the_ways_to_fix_it()
    {
        var message = AccessDenied.Explain(new UnauthorizedAccessException("Access to the path '/var/lib/evidence-hound' is denied."));

        message.ShouldNotBeNull();
        message.ShouldContain("Access to the path '/var/lib/evidence-hound' is denied.");
        message.ShouldContain("evidence.root");
        message.ShouldContain("chown");
    }

    [Fact]
    public void Other_failures_are_left_to_the_unexpected_failure_message()
    {
        AccessDenied.Explain(new InvalidOperationException("boom")).ShouldBeNull();
    }
}
