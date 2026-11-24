namespace EvidenceHound.Core.Scope;

/// <summary>
/// The kinds of sensitive data a small defence supplier is asked about, framework-neutral: controlled unclassified information, personal
/// information, export-controlled data (ITAR, EAR, DSGL) and security-classified information (such as OFFICIAL: Sensitive or PROTECTED).
/// </summary>
public enum SensitiveDataKind
{
    Cui,
    Pii,
    ExportControlled,
    Classified,
}

/// <summary>What a system or a supplier's solution does with the data.</summary>
public enum DataRole
{
    Stores,
    Processes,
    Transmits,
    Protects,
}

/// <summary>What approves a solution to handle a kind of data, such as FedRAMP Moderate or an IRAP assessment, with its evidence and expiry.</summary>
public sealed record DataAuthority(string Name, SourceLine At)
{
    /// <summary>A document in the compliance repo that shows it.</summary>
    public string? Evidence { get; init; }

    public DateOnly? Expires { get; init; }
}

/// <summary>One kind of sensitive data a system or solution handles. <see cref="Detail"/> says which, such as <c>ITAR</c> or <c>PROTECTED</c>.</summary>
public sealed record SensitiveData(SensitiveDataKind Kind, DataRole Role, SourceLine At)
{
    public string? Detail { get; init; }

    /// <summary>On a solution only: what authorises it.</summary>
    public DataAuthority? Authority { get; init; }

    /// <summary>The kind as the scope files write it: <c>cui</c>, <c>pii</c>, <c>export_controlled</c> or <c>classified</c>.</summary>
    public static string Code(SensitiveDataKind kind) => kind switch
    {
        SensitiveDataKind.ExportControlled => "export_controlled",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
