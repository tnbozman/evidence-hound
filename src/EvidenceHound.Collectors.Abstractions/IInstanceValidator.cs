using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors;

/// <summary>
/// A collector that can check an instance's settings and secret names before it runs, beyond the settings it describes (for example
/// against a profile the instance names). <c>hound plan</c> and <c>hound config validate</c> report what it finds as warnings: an
/// invalid instance stops only itself.
/// </summary>
public interface IInstanceValidator
{
    IReadOnlyList<Problem> ValidateInstance(CollectorSettings settings, IReadOnlyCollection<string> secrets);
}
