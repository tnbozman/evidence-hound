namespace EvidenceHound.Core.Scope;

/// <summary>
/// A supplier or other third party, from <c>scope/third-parties/&lt;entity&gt;.yaml</c>: a CISO Assistant entity with the solutions it provides,
/// the contracts covering them, and optionally its representatives. Levels follow CISO Assistant's stakeholder scales: dependency and
/// penetration 0 to 4, maturity and trust 1 to 4.
/// </summary>
public sealed record ScopeEntity(string RefId, string Name, SourceLine At)
{
    public string? Description { get; init; }

    public string? Mission { get; init; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? Country { get; init; }

    /// <summary>ISO 4217.</summary>
    public string? Currency { get; init; }

    public ScopeRef? Parent { get; init; }

    public ScopeRef? Domain { get; init; }

    public int? Dependency { get; init; }

    public int? Penetration { get; init; }

    public int? Maturity { get; init; }

    public int? Trust { get; init; }

    /// <summary>Legal identifiers by kind, such as LEI, VAT or ABN.</summary>
    public IReadOnlyDictionary<string, string> Identifiers { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<ScopeSolution> Solutions { get; init; } = [];

    public IReadOnlyList<ScopeContract> Contracts { get; init; } = [];

    public IReadOnlyList<ScopeRepresentative> Representatives { get; init; } = [];

    /// <summary>The tier the organisation holds this supplier at whatever its assessment says, one of the tier scale's keys; needs a reason.</summary>
    public string? ForcedTier { get; init; }

    public string? ForcedTierReason { get; init; }

    public SourceLine? ForcedTierAt { get; init; }

    /// <summary>The supplier's assessments, each with its questionnaire, set up from the file; answers and conclusions stay CISO Assistant's.</summary>
    public IReadOnlyList<ScopeEntityAssessment> Assessments { get; init; } = [];

    /// <summary>Where each level was written, for problems about its range.</summary>
    public IReadOnlyDictionary<string, SourceLine> LevelsAt { get; init; } = new Dictionary<string, SourceLine>();

    /// <summary>Where the country and currency were written.</summary>
    public IReadOnlyDictionary<string, SourceLine> CodesAt { get; init; } = new Dictionary<string, SourceLine>();
}

/// <summary>A product or service the entity provides. Criticality 1 to 4.</summary>
public sealed record ScopeSolution(string RefId, string Name, string? Description, int? Criticality, SourceLine At);

/// <summary>A contract with the entity: <c>draft</c>, <c>active</c>, <c>expired</c> or <c>terminated</c>, with dates as yyyy-MM-dd.</summary>
public sealed record ScopeContract(string RefId, string Name, string Status, SourceLine At)
{
    public string? Description { get; init; }

    public string? Start { get; init; }

    public string? End { get; init; }

    public SourceLine? EndAt { get; init; }

    /// <summary>The annual expense as written, a plain number.</summary>
    public string? AnnualExpense { get; init; }

    public string? Currency { get; init; }

    public SourceLine? CurrencyAt { get; init; }

    public IReadOnlyList<ScopeRef> Solutions { get; init; } = [];
}

/// <summary>A person at the entity, matched by email. <c>hound</c> never gives a representative a login.</summary>
public sealed record ScopeRepresentative(string Email, SourceLine At)
{
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Role { get; init; }

    public string? Phone { get; init; }
}

/// <summary>
/// An assessment of the entity: which of its solutions, the perimeter it sits in, when it is due and how critical it is, and the questionnaire
/// framework (by URN) whose audit CISO Assistant creates with it. With <see cref="ReviewMonths"/>, the next review opens once one falls due.
/// </summary>
public sealed record ScopeEntityAssessment(string RefId, string Name, ScopeRef Perimeter, SourceLine At)
{
    public IReadOnlyList<ScopeRef> Solutions { get; init; } = [];

    /// <summary>The perimeter's domain; the entity's own domain when left out.</summary>
    public ScopeRef? Domain { get; init; }

    public string? DueDate { get; init; }

    public int? Criticality { get; init; }

    public ScopeRef? Questionnaire { get; init; }

    public IReadOnlyList<string> Groups { get; init; } = [];

    /// <summary>Months between reviews, from <c>review_every</c>.</summary>
    public int? ReviewMonths { get; init; }
}
