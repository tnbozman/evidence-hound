using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>
/// s15-07: an incident and its timeline. It was discovered at its first detection entry, or when it was reported if it has none; its
/// systems are its assets by name.
/// </summary>
internal sealed class IncidentReader(HttpClient http) : IIncidentSource
{
    public async Task<Result<IncidentFacts>> ReadAsync(string refId, CancellationToken cancellationToken)
    {
        var found = await http.ListAllAsync($"incidents/?ref_id={Uri.EscapeDataString(refId)}", cancellationToken);
        if (found is not Result<List<JsonNode>>.Ok { Value: var incidents })
        {
            return found.Map(_ => (IncidentFacts)null!);
        }

        if (incidents.FirstOrDefault(i => (string?)i["ref_id"] == refId) is not { } incident)
        {
            return Result.Fail<IncidentFacts>(new Problem(
                "incident.not_found", $"CISO Assistant holds no incident with the ref_id {refId}. Record it under Operations, Incidents, and give it that reference ID."));
        }

        var entries = await http.ListAllAsync($"timeline-entries/?incident={Uri.EscapeDataString((string)incident["id"]!)}", cancellationToken);
        return entries.Map(all =>
        {
            var timeline = all.Select(e => (Type: (string?)e["entry_type"], Entry: new IncidentEntry(Time(e["timestamp"]) ?? DateTimeOffset.MinValue, (string?)e["entry"] ?? "")))
                .OrderBy(e => e.Entry.At).ToList();
            var detected = timeline.Where(e => e.Type == "detection").Select(e => (DateTimeOffset?)e.Entry.At).FirstOrDefault();
            var discovered = detected ?? Time(incident["reported_at"]) ?? Time(incident["created_at"]) ?? DateTimeOffset.UtcNow;
            return new IncidentFacts(
                refId,
                (string?)incident["name"] ?? refId,
                discovered,
                [.. (incident["assets"] as JsonArray ?? []).Select(a => (string?)a?["str"] ?? (string?)a?["name"]).OfType<string>()],
                [.. timeline.Select(e => e.Entry)]);
        });
    }

    private static DateTimeOffset? Time(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time : null;
}
