using EvidenceHound.M365DevServer;

namespace EvidenceHound.Collectors.M365.Tests;

/// <summary>The dev server's command line: <c>make m365-dev ARGS="--variant gaps --port 5366"</c>.</summary>
public sealed class M365DevServerOptionsTests
{
    [Fact]
    public void Defaults_to_the_compliant_tenant_on_port_5365()
    {
        var options = DevServerOptions.Parse([]).ShouldBeOfType<DevServerOptions>();

        options.Port.ShouldBe(5365);
        options.Tenant.ShouldBe(DevTenant.Compliant);
    }

    [Fact]
    public void Gaps_variant_on_another_port()
    {
        var options = DevServerOptions.Parse(["--variant", "gaps", "--port", "5366"]).ShouldBeOfType<DevServerOptions>();

        options.Port.ShouldBe(5366);
        options.Variant.ShouldBe("gaps");
        options.Deviations.ShouldBe(DevTenant.Deviations.Select(d => d.Name));
    }

    [Fact]
    public void One_named_deviation_on_the_compliant_tenant()
    {
        var options = DevServerOptions.Parse(["--deviation", "synced-admin"]).ShouldBeOfType<DevServerOptions>();

        options.Deviations.ShouldBe(["synced-admin"]);
        options.Tenant.People.Count(p => p.Synced).ShouldBe(1);
    }

    [Theory]
    [InlineData("--variant", "partly")]
    [InlineData("--deviation", "no-such-thing")]
    [InlineData("--port", "http")]
    [InlineData("--colour", "blue")]
    public void Unknown_values_are_refused_with_the_choices(string option, string value)
    {
        var error = DevServerOptions.Parse([option, value]).ShouldBeOfType<string>();

        error.ShouldContain(option);
    }
}
