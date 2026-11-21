namespace EvidenceHound.Collectors.M365.Tests;

public class OptionsTests
{
    [Fact]
    public void Defaults_apply()
    {
        var options = Tenants.Options(s => s.Remove("break_glass_accounts"));

        options.GraphUrl.ShouldBe(new Uri("https://graph.microsoft.com/"));
        options.IncludeIdentities.ShouldBeFalse();
        options.BreakGlass.ShouldBeEmpty();
        options.MfaPartialPercent.ShouldBe(90);
        options.PrivilegedRoles.Count.ShouldBe(14);
    }

    [Fact]
    public void Break_glass_accounts_are_a_comma_separated_list()
    {
        Tenants.Options().BreakGlass.ShouldBe(["00000000-0000-4000-8000-0000000000b1", "00000000-0000-4000-8000-0000000000b2"], ignoreOrder: true);
    }

    [Fact]
    public void Graph_url_gets_a_trailing_slash()
    {
        Tenants.Options(s => s["graph_url"] = "https://graph.example.test").GraphUrl.ShouldBe(new Uri("https://graph.example.test/"));
    }

    [Theory]
    [InlineData("mfa_partial_percent", "ninety")]
    [InlineData("mfa_partial_percent", "101")]
    [InlineData("include_identities", "maybe")]
    [InlineData("graph_url", "not a url")]
    [InlineData("graph_url", "ftp://graph.example.test/")]
    public void Invalid_values_are_problems(string setting, string value)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["tenant_id"] = RecordedTenant.TenantId, ["client_id"] = RecordedTenant.ClientId, [setting] = value };

        var problem = M365Options.FromSettings(new CollectorSettings(settings)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("m365.setting_invalid");
        problem.Message.ShouldContain(setting);
    }

    [Fact]
    public void Missing_tenant_is_a_problem()
    {
        var problems = M365Options.FromSettings(new CollectorSettings(new Dictionary<string, string>())).Problems();

        problems.Select(p => p.Code).ShouldBe(["m365.setting_missing", "m365.setting_missing"]);
    }
}
