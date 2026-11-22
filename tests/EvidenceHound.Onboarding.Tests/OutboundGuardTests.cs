using EvidenceHound.Onboarding.Guard;
using EvidenceHound.Onboarding.Models;

namespace EvidenceHound.Onboarding.Tests;

public class OutboundGuardTests
{
    private static readonly GuardTerms Terms = new(
        new Dictionary<string, string> { ["client_secret"] = "example-client-secret-value" },
        ["Example Internal Project"]);

    [Fact]
    public void Secret_value_in_request_blocks_it()
    {
        var request = Request("The token request sends example-client-secret-value as the client secret.");

        var findings = OutboundGuard.Check(request, Terms);

        findings.ShouldHaveSingleItem().ShouldBe(new GuardFinding("api", "the value of the secret client_secret"));
        string.Concat(findings.Select(f => f.What)).ShouldNotContain("example-client-secret-value");
    }

    [Fact]
    public void Deny_term_and_email_are_reported()
    {
        var request = Request("Ask admin@corp.example.org about the example internal project.");

        OutboundGuard.Check(request, Terms).Select(f => f.What).ShouldBe(["the deny term 'Example Internal Project'", "an email address"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("The firewall at 10.20.30.40 answers.", "an IP address")]
    [InlineData("Its interface is 00:1A:2B:3C:4D:5E.", "a MAC address")]
    [InlineData("Its IPv6 address is fd12:3456:789a:1::1.", "an IP address")]
    public void Addresses_are_reported(string text, string what)
    {
        OutboundGuard.Check(Request(text), Terms).ShouldHaveSingleItem().What.ShouldBe(what);
    }

    [Theory]
    [InlineData("The documentation range 192.0.2.10 and admin@example.com are fictional.")]
    [InlineData("SFOS 21.0.1 and 21.5 are supported.")]
    [InlineData("Paging uses pageSize=100 and pageTotal=true.")]
    public void Documentation_addresses_and_versions_pass(string text)
    {
        OutboundGuard.Check(Request(text), Terms).ShouldBeEmpty();
    }

    [Fact]
    public void Instructions_and_schema_are_scanned_too()
    {
        var request = Request("Nothing here.") with { Instructions = "Write to admin@corp.example.org when done." };

        OutboundGuard.Check(request, Terms).ShouldHaveSingleItem().Where.ShouldBe("instructions");
    }

    private static DraftRequest Request(string api) =>
        new("applicability", "Decide how the requirement is assessed.", [new DraftInput("api", api)], DraftingModelContractTests<AnthropicDraftingModel>.Schema);
}
