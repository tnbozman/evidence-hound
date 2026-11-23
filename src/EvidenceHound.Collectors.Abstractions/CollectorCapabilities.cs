namespace EvidenceHound.Collectors;

/// <summary>
/// The capabilities, by mitigation code such as <c>ac</c>, that <c>scope/capabilities.yaml</c> says another product provides, each with that
/// product's name. A collector's checks for one of them are not applicable here. <see cref="All"/>, with none, keeps every check.
/// </summary>
public sealed record CollectorCapabilities(IReadOnlyDictionary<string, string> ProvidedElsewhere)
{
    public static CollectorCapabilities All { get; } = new(new Dictionary<string, string>());
}
