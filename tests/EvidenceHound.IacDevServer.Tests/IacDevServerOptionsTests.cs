namespace EvidenceHound.IacDevServer.Tests;

/// <summary>The dev server's command line: <c>make iac-dev ARGS="--deviation public-ip --controls-report controls.xml"</c>.</summary>
public sealed class IacDevServerOptionsTests
{
    [Fact]
    public void Defaults_to_the_compliant_pipeline_on_port_5380_at_localhost()
    {
        var options = DevServerOptions.Parse([]).ShouldBeOfType<DevServerOptions>();

        options.Port.ShouldBe(5380);
        options.PublicUrl.ShouldBe(new Uri("http://localhost:5380/"));
        options.Deviations.ShouldBeEmpty();
        options.ControlsReport.ShouldBeNull();
    }

    [Fact]
    public void Gaps_variant_at_the_address_the_runner_reaches()
    {
        var options = DevServerOptions.Parse(["--variant", "gaps", "--port", "5381", "--public-url", "http://host.docker.internal:5381"]).ShouldBeOfType<DevServerOptions>();

        options.Port.ShouldBe(5381);
        options.PublicUrl.ShouldBe(new Uri("http://host.docker.internal:5381/"));
        options.Deviations.ShouldBe(IacDevPipeline.Deviations.Select(d => d.Name));
    }

    [Fact]
    public void One_named_deviation_and_a_report_file()
    {
        var options = DevServerOptions.Parse(["--deviation", "drift", "--controls-report", "/trial/controls.xml"]).ShouldBeOfType<DevServerOptions>();

        options.Deviations.ShouldBe(["drift"]);
        options.ControlsReport.ShouldBe("/trial/controls.xml");
    }

    [Theory]
    [InlineData("--variant", "partly")]
    [InlineData("--deviation", "no-such-thing")]
    [InlineData("--port", "http")]
    [InlineData("--public-url", "not a url")]
    [InlineData("--controls-report", "")]
    [InlineData("--colour", "blue")]
    public void Unknown_values_are_refused(string option, string value)
    {
        DevServerOptions.Parse([option, value]).ShouldBeOfType<string>().ShouldContain(option);
    }
}
