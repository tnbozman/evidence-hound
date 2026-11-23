namespace EvidenceHound.SophosDevServer.Tests;

/// <summary>The dev server's command line: <c>make sophos-dev ARGS="--variant gaps --port 5371 --public-url http://host.docker.internal:5371/"</c>.</summary>
public sealed class SophosDevServerOptionsTests
{
    [Fact]
    public void Defaults_to_the_compliant_tenant_on_port_5370_at_localhost()
    {
        var options = DevServerOptions.Parse([]).ShouldBeOfType<DevServerOptions>();

        options.Port.ShouldBe(5370);
        options.PublicUrl.ShouldBe(new Uri("http://localhost:5370/"));
        options.Tenant.ShouldBe(SophosDevTenant.Compliant);
    }

    [Fact]
    public void Gaps_variant_at_the_address_the_runner_reaches()
    {
        var options = DevServerOptions.Parse(["--variant", "gaps", "--port", "5371", "--public-url", "http://host.docker.internal:5371"]).ShouldBeOfType<DevServerOptions>();

        options.Port.ShouldBe(5371);
        options.PublicUrl.ShouldBe(new Uri("http://host.docker.internal:5371/"));
        options.Deviations.ShouldBe(SophosDevTenant.Deviations.Select(d => d.Name));
    }

    [Fact]
    public void One_named_deviation()
    {
        var options = DevServerOptions.Parse(["--deviation", SophosDevTenant.Deviations[0].Name]).ShouldBeOfType<DevServerOptions>();

        options.Deviations.ShouldBe([SophosDevTenant.Deviations[0].Name]);
    }

    [Theory]
    [InlineData("--variant", "partly")]
    [InlineData("--deviation", "no-such-thing")]
    [InlineData("--port", "http")]
    [InlineData("--public-url", "not a url")]
    [InlineData("--colour", "blue")]
    public void Unknown_values_are_refused(string option, string value)
    {
        DevServerOptions.Parse([option, value]).ShouldBeOfType<string>().ShouldContain(option);
    }
}
