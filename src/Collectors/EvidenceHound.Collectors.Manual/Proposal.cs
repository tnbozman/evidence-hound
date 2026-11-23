using System.Globalization;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace EvidenceHound.Collectors.Manual;

/// <summary>
/// What an agent proposes for one dropped file, kept next to it as <c>&lt;file&gt;.proposal.yaml</c> until a person attests or rejects it:
/// the outcome for each check, an observation, and what in the file supports it. A proposal is not an attestation, so it is not evidence
/// and no run publishes it.
/// </summary>
public sealed record Proposal(
    string ProposedBy, DateTimeOffset ProposedAt, string FileSha256, IReadOnlyDictionary<string, CheckOutcome> Outcomes, string Observation, IReadOnlyList<string> Supports)
{
    public const string Suffix = ".proposal.yaml";

    private static readonly string Stamp = "yyyy-MM-ddTHH:mm:ssZ";

    /// <summary>Who rejected the proposal; null while it waits.</summary>
    public string? RejectedBy { get; init; }

    public DateTimeOffset? RejectedAt { get; init; }

    public string? RejectionReason { get; init; }

    /// <summary>Whether a person has still to attest or reject it.</summary>
    public bool Pending => RejectedBy is null;

    /// <summary>The attestation a person makes from this proposal, with the outcomes and note they settled on.</summary>
    public Attestation Attest(string who, DateTimeOffset at, IReadOnlyDictionary<string, CheckOutcome> outcomes, string note) =>
        new(who, at, outcomes, note) { ProposedBy = ProposedBy, FileSha256 = FileSha256 };

    public Proposal Reject(string who, DateTimeOffset at, string reason) => this with { RejectedBy = who, RejectedAt = at, RejectionReason = reason };

    public static Result<Proposal> Parse(string yaml, string source)
    {
        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root = stream.Documents is [var document, ..] ? document.RootNode as YamlMappingNode : null;
        }
        catch (YamlException e)
        {
            return Fail(source, $"is not valid YAML: {e.Message}");
        }

        if (root is null || Attestation.Text(root, "file_sha256") is not { Length: > 0 } hash)
        {
            return Fail(source, "must be a map with proposed_by, file_sha256, outcomes, observation and supports.");
        }

        var supports = root.Children.TryGetValue(new YamlScalarNode("supports"), out var node) && node is YamlSequenceNode list
            ? list.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "").ToArray()
            : [];
        return Attestation.ParseOutcomes(root, source).Map(outcomes => new Proposal(
            Attestation.Text(root, "proposed_by") ?? "agent", Date(root, "proposed") ?? default, hash, outcomes, Attestation.Text(root, "observation") ?? "", supports)
        {
            RejectedBy = Attestation.Text(root, "rejected_by"),
            RejectedAt = Date(root, "rejected"),
            RejectionReason = Attestation.Text(root, "rejection_reason"),
        });
    }

    public string ToYaml()
    {
        var document = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["proposed_by"] = ProposedBy,
            ["proposed"] = ProposedAt.UtcDateTime.ToString(Stamp, CultureInfo.InvariantCulture),
            ["file_sha256"] = FileSha256,
            ["outcomes"] = Outcomes.ToDictionary(pair => pair.Key, pair => Attestation.NameOf(pair.Value), StringComparer.Ordinal),
            ["observation"] = Observation,
            ["supports"] = Supports,
        };
        if (RejectedBy is not null)
        {
            document["rejected_by"] = RejectedBy;
            document["rejected"] = (RejectedAt ?? default).UtcDateTime.ToString(Stamp, CultureInfo.InvariantCulture);
            document["rejection_reason"] = RejectionReason ?? "";
        }

        return new SerializerBuilder().Build().Serialize(document);
    }

    private static DateTimeOffset? Date(YamlMappingNode root, string key) =>
        DateTimeOffset.TryParse(Attestation.Text(root, key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when) ? when : null;

    private static Result<Proposal> Fail(string source, string message) => Result.Fail<Proposal>(new Problem("manual.proposal_invalid", $"{source}: {message}"));
}
