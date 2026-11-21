namespace EvidenceHound.Functional.Tests;

/// <summary>Every functional test class in this collection shares one seeded CISO Assistant container.</summary>
[CollectionDefinition(Name)]
public sealed class SharedCisoAssistant : ICollectionFixture<CisoAssistantFixture>
{
    public const string Name = "CISO Assistant";
}
