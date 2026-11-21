namespace EvidenceHound.Core.Planning;

/// <summary>The status of an applied control in CISO Assistant.</summary>
public enum ControlState
{
    ToDo,
    InProgress,
    OnHold,
    Active,
    Deprecated,
}

/// <summary>A control's observed status, named by its configuration key (not a CISO Assistant ID).</summary>
public sealed record ControlStatus(string ControlKey, ControlState State);
