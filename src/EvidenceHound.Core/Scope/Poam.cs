namespace EvidenceHound.Core.Scope;

/// <summary>One line of the plan of action and milestones: a requirement not yet met and a control that answers it.</summary>
public sealed record PoamItem(SspValue Requirement, SspValue Status, SspValue Control, SspValue Owner, SspValue Eta, SspValue ControlStatus);

/// <summary>The POA&amp;M, from the same model as the plan (s11-03). Pure.</summary>
public static class Poam
{
    public static IReadOnlyList<PoamItem> Of(SspDocument document) => throw new NotImplementedException();

    public static string Markdown(IReadOnlyList<PoamItem> items) => throw new NotImplementedException();
}
