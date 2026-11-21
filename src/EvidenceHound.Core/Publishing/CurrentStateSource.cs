using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Planning;

namespace EvidenceHound.Core.Publishing;

/// <summary>The audits, evidence ref_ids and applied control ref_ids whose current state a plan needs.</summary>
public sealed record StateQuery(IReadOnlyList<string> AuditRefIds, IReadOnlyList<string> EvidenceRefIds, IReadOnlyList<string> ControlRefIds)
{
    public static StateQuery For(IReadOnlyList<TargetResults> targets, IReadOnlyList<Evidence> evidence, IReadOnlyList<ControlStatus> controlStatuses) =>
        throw new NotImplementedException();
}

/// <summary>Reads the current state a plan is compared with.</summary>
public interface ICurrentStateSource
{
    Task<CurrentState> ReadAsync(StateQuery query, CancellationToken cancellationToken);
}

/// <summary>Reads the current state from CISO Assistant.</summary>
public sealed class ClientStateSource(ICisoAssistantClient client) : ICurrentStateSource
{
    public Task<CurrentState> ReadAsync(StateQuery query, CancellationToken cancellationToken) => throw new NotImplementedException(client.ToString());
}
