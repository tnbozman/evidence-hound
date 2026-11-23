using System.Text.Json;
using System.Text.Json.Serialization;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Suppliers;

namespace EvidenceHound.Onboarding.Sessions;

/// <summary>Where an onboarding session is.</summary>
public enum OnboardStage
{
    /// <summary>Asking who provides the system.</summary>
    Supplier,

    /// <summary>Stopped until the supplier is approved.</summary>
    AwaitingSupplier,

    /// <summary>Deciding how each requirement is met.</summary>
    Applicability,

    /// <summary>Every requirement decided.</summary>
    Decided,
}

/// <summary>
/// An onboarding session, saved after every answer so it resumes where it stopped. Proposals are kept, so resuming never asks the model
/// again for what it already answered.
/// </summary>
public sealed record OnboardSession(string System, OnboardStage Stage)
{
    public int Format { get; init; } = 1;

    public string? Reviewer { get; init; }

    public string? Owner { get; init; }

    public string? Product { get; init; }

    public string? Framework { get; init; }

    public IReadOnlyList<string> Groups { get; init; } = [];

    public SupplierChoice? Supplier { get; init; }

    /// <summary>What the supplier provides for the system, in the person's words: kept for whoever decides the supplier's levels.</summary>
    public string? SupplierProvides { get; init; }

    public IReadOnlyList<LevelProposal> SupplierLevels { get; init; } = [];

    /// <summary>The supplier summary: a draft for the onboarding record, never evidence.</summary>
    public IReadOnlyList<SummaryPoint> SupplierSummary { get; init; } = [];

    public IReadOnlyList<KeptDocument> Documents { get; init; } = [];

    public IReadOnlyList<string> Specs { get; init; } = [];

    public IReadOnlyList<Proposal> Proposals { get; init; } = [];

    public IReadOnlyList<Decision> Decisions { get; init; } = [];

    /// <summary>The files written from the decided session, relative to the repo. Once written, a re-run writes nothing.</summary>
    public IReadOnlyList<string> Written { get; init; } = [];

    /// <summary>Proposals not yet decided, in framework order.</summary>
    [JsonIgnore]
    public IReadOnlyList<Proposal> Pending => [.. Proposals.Where(p => Decisions.All(d => d.Key != p.Key))];

    /// <summary>Decisions an agent drafted that no person has accepted yet. While there are any, only <c>hound approve</c> writes the system's files.</summary>
    [JsonIgnore]
    public IReadOnlyList<Decision> Drafts => [.. Decisions.Where(d => d.AwaitsApproval)];

    /// <summary>The supplier gate's answer moves the session past the supplier, or holds it there.</summary>
    public OnboardSession Advance(SupplierStatus status) => Stage switch
    {
        OnboardStage.Supplier or OnboardStage.AwaitingSupplier => this with { Stage = status == SupplierStatus.Approved ? OnboardStage.Applicability : OnboardStage.AwaitingSupplier },
        _ => this,
    };

    /// <summary>Records a decision, replacing any earlier one for the same requirement; the last one decides the session.</summary>
    public OnboardSession Decide(Decision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var decided = this with { Decisions = [.. Decisions.Where(d => d.Key != decision.Key), decision] };
        decided = decided with { Decisions = [.. Proposals.Select(p => decided.Decisions.FirstOrDefault(d => d.Key == p.Key)).OfType<Decision>(), .. decided.Decisions.Where(d => Proposals.All(p => p.Key != d.Key))] };
        return decided.Pending.Count == 0 && Proposals.Count > 0 ? decided with { Stage = OnboardStage.Decided } : decided;
    }
}

/// <summary>Reads and writes <c>onboarding/&lt;system&gt;/session.json</c>.</summary>
public static class OnboardSessionJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static string Write(OnboardSession session) => JsonSerializer.Serialize(session, Options) + "\n";

    public static Result<OnboardSession> Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<OnboardSession>(json, Options) is { } session
                ? Result.Ok(session)
                : Result.Fail<OnboardSession>(Unreadable("it is empty"));
        }
        catch (JsonException e)
        {
            return Result.Fail<OnboardSession>(Unreadable(e.Message));
        }
    }

    private static Problem Unreadable(string why) =>
        new("onboard.session_unreadable", $"The saved onboarding session cannot be read ({why}). Move it aside to start again.");
}
