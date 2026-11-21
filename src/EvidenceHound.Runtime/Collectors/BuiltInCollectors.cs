using EvidenceHound.Core.Collecting;
using EvidenceHound.Runtime.Configuration;

namespace EvidenceHound.Runtime.Collectors;

/// <summary>The collectors this build ships, by type name.</summary>
public static class BuiltInCollectors
{
    public static IReadOnlyDictionary<string, ICollector> Create() =>
        new Dictionary<string, ICollector>(StringComparer.Ordinal) { [CollectorTypes.Fake] = new FakeCollector() };
}
