using EvidenceHound.Collectors;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceHound.Runtime.Tests;

public class CollectorRegistryTests
{
    [Fact]
    public void Registered_collector_is_resolved_by_type()
    {
        using var services = new ServiceCollection().AddCollector<FakeCollector>().AddCollector<WidgetCollector>().BuildServiceProvider();

        var registry = CollectorRegistry.FromServices(services).Value();

        registry.ByType.Keys.Order(StringComparer.Ordinal).ShouldBe(["fake", "widget"]);
        registry.ByType["fake"].ShouldBeOfType<FakeCollector>();
        registry.ByType["widget"].ShouldBeOfType<WidgetCollector>();
        registry.Descriptions["widget"].Checks.ShouldBe(["widget.patched"]);
    }

    [Fact]
    public void Duplicate_type_fails_at_startup()
    {
        using var services = new ServiceCollection().AddCollector<FakeCollector>().AddCollector<SecondFakeCollector>().BuildServiceProvider();

        var problem = CollectorRegistry.FromServices(services).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("collector.type_duplicate");
        problem.Message.ShouldContain("'fake'");
        problem.Message.ShouldContain(nameof(FakeCollector));
        problem.Message.ShouldContain(nameof(SecondFakeCollector));
    }

    private sealed class WidgetCollector : ICollector
    {
        public string Type => "widget";

        public string Version => "0.1.0";

        public CollectorDescription Describe() => new(["widget.patched"], [], []);

        public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new CollectionResult([], [], [])));
    }

    private sealed class SecondFakeCollector : ICollector
    {
        public string Type => "fake";

        public string Version => "0.1.0";

        public CollectorDescription Describe() => new([], [], []);

        public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new CollectionResult([], [], [])));
    }
}
