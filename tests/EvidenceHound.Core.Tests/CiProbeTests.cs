namespace EvidenceHound.Core.Tests;

public class CiProbeTests
{
    [Fact]
    public void Deliberately_failing_to_prove_ci_goes_red() => false.ShouldBeTrue();
}
