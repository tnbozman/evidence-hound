using EvidenceHound.Collectors.Testing;
using EvidenceHound.Runtime.Collectors;

namespace EvidenceHound.Runtime.Tests;

/// <summary>The built-in fake collector passes the collector contract.</summary>
public class FakeCollectorContractTests : CollectorContractTests<FakeCollector>
{
    protected override FakeCollector CreateCollector() => new();
}
