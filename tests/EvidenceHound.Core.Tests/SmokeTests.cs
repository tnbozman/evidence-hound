namespace EvidenceHound.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_assembly_loads()
    {
        var assembly = typeof(CoreAssembly).Assembly;

        assembly.GetName().Name.ShouldBe("EvidenceHound.Core");
    }
}
