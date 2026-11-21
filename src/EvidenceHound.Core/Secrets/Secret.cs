namespace EvidenceHound.Core.Secrets;

/// <summary>A resolved secret. <see cref="ToString()"/> is redacted, so it never reaches a log or a message by accident.</summary>
public sealed class Secret
{
    private readonly string _value;

    public Secret(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>The secret itself. Call this only where the value is handed to the system that needs it.</summary>
    public string Reveal() => _value;

    public override string ToString() => "***";
}
