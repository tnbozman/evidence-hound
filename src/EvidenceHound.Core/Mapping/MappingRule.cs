namespace EvidenceHound.Core.Mapping;

/// <summary>How the outcomes of a requirement's applicable checks roll up into one result.</summary>
public abstract record MappingRule
{
    private MappingRule()
    {
    }

    /// <summary>Every mapped check must pass. Any failure makes the requirement non-compliant.</summary>
    public static MappingRule AllPass { get; } = new AllPassRule();

    /// <summary>Compliant when every check passes, partially compliant when at least one passes or partly passes.</summary>
    public static MappingRule AnyPass { get; } = new AnyPassRule();

    /// <summary>At least <paramref name="count"/> mapped checks must pass (capped at the number of applicable checks).</summary>
    public static MappingRule Threshold(int count) => new ThresholdRule(count);

    /// <summary>The rule's name in mapping files and observations.</summary>
    public abstract string Name { get; }

    /// <summary>The result for a tally of applicable outcomes (at least one).</summary>
    public abstract ComplianceResult Assess(OutcomeTally tally);

    private static ComplianceResult PassesNeeded(OutcomeTally tally, int needed) =>
        tally.Passes >= needed ? ComplianceResult.Compliant
        : tally.Passes + tally.Partials > 0 ? ComplianceResult.PartiallyCompliant
        : ComplianceResult.NonCompliant;

    public sealed record AllPassRule : MappingRule
    {
        public override string Name => "all_pass";

        public override ComplianceResult Assess(OutcomeTally tally) =>
            tally.Fails > 0 ? ComplianceResult.NonCompliant : PassesNeeded(tally, tally.Total);
    }

    public sealed record AnyPassRule : MappingRule
    {
        public override string Name => "any_pass";

        public override ComplianceResult Assess(OutcomeTally tally) => PassesNeeded(tally, tally.Total);
    }

    public sealed record ThresholdRule(int Count) : MappingRule
    {
        public override string Name => $"threshold({Count})";

        public override ComplianceResult Assess(OutcomeTally tally) => PassesNeeded(tally, Math.Min(Count, tally.Total));
    }
}

/// <summary>How many applicable checks passed, partly passed or failed.</summary>
public readonly record struct OutcomeTally(int Passes, int Partials, int Fails)
{
    public int Total => Passes + Partials + Fails;
}
