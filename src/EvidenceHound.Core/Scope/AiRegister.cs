namespace EvidenceHound.Core.Scope;

/// <summary>The result of screening an AI system (AI6 practice 3). A prohibited system is not used.</summary>
public enum AiRiskTier
{
    Low,
    Medium,
    High,
    Prohibited,
}

/// <summary>Whether the organisation built the AI system, customised one, or uses a supplier's product (AI6's develop and deploy split).</summary>
public enum AiUse
{
    Built,
    Customised,
    ThirdParty,
}

/// <summary>How people oversee the AI system (AI6 practice 6).</summary>
public enum AiOversight
{
    HumanInTheLoop,
    HumanOnTheLoop,
    HumanOutOfLoop,
}

/// <summary>Where the AI system's impact assessment is (AI6 practice 2).</summary>
public enum AiImpactAssessment
{
    NotStarted,
    InProgress,
    Approved,
    NotRequired,
}

/// <summary>A system's entry in the AI register (s11-12): the <c>ai</c> block of a system in <c>scope/setup.yaml</c>, reviewed by pull request.</summary>
public sealed record AiRegistration(AiRiskTier Tier, DateOnly ScreenedOn, AiUse Use, AiOversight Oversight, string OversightNote, SourceLine At)
{
    public AiImpactAssessment? ImpactAssessment { get; init; }

    public DateOnly? ImpactAssessedOn { get; init; }

    /// <summary>The go-live date; null while the system is being considered.</summary>
    public DateOnly? InUseFrom { get; init; }

    public DateOnly? LastTestedOn { get; init; }

    public DateOnly? RetiredOn { get; init; }
}
