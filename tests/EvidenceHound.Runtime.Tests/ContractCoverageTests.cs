using EvidenceHound.Collectors;
using EvidenceHound.Collectors.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceHound.Runtime.Tests;

public class ContractCoverageTests
{
    [Fact]
    public void Every_built_in_collector_has_contract_tests()
    {
        var builtIn = new ServiceCollection().AddBuiltInCollectors()
            .Where(d => d.ServiceType == typeof(ICollector))
            .Select(d => d.ImplementationType.ShouldNotBeNull());

        var covered = typeof(ContractCoverageTests).Assembly.GetTypes()
            .Where(t => t is { IsPublic: true, IsAbstract: false, BaseType.IsGenericType: true } && t.BaseType.GetGenericTypeDefinition() == typeof(CollectorContractTests<>))
            .Select(t => t.BaseType!.GetGenericArguments()[0]);

        builtIn.Except(covered).ShouldBeEmpty("Each built-in collector needs a public CollectorContractTests<T> class in this project.");
    }
}
