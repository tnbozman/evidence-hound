namespace EvidenceHound.Core.Secrets;

/// <summary>A resolved secret. <see cref="ToString"/> is redacted, so it never reaches a log or a message by accident.</summary>
public sealed class Secret
{
    public Secret(string value) => throw new NotImplementedException();

    /// <summary>The secret itself. Call this only where the value is handed to the system that needs it.</summary>
    public string Reveal() => throw new NotImplementedException();

    public override string ToString() => throw new NotImplementedException();
}
