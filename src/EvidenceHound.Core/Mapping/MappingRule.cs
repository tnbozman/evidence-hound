namespace EvidenceHound.Core.Mapping;

/// <summary>How the outcomes of a requirement's checks roll up into one result.</summary>
public abstract record MappingRule
{
    private MappingRule()
    {
    }

    /// <summary>Every mapped check must pass.</summary>
    public static MappingRule AllPass { get; } = new AllPassRule();

    /// <summary>At least one mapped check must pass.</summary>
    public static MappingRule AnyPass { get; } = new AnyPassRule();

    /// <summary>At least <paramref name="count"/> mapped checks must pass.</summary>
    public static MappingRule Threshold(int count) => new ThresholdRule(count);

    public sealed record AllPassRule : MappingRule;

    public sealed record AnyPassRule : MappingRule;

    public sealed record ThresholdRule(int Count) : MappingRule;
}
