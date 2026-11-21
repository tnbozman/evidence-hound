using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.M365;

/// <summary>What one check found, citing the Graph collections (by name) it read.</summary>
internal sealed record Finding(string CheckId, CheckOutcome Outcome, string Observation, IReadOnlyList<string> Sources);
