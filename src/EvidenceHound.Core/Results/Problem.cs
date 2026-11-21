namespace EvidenceHound.Core.Results;

/// <summary>An expected failure: a stable machine-readable code and a message for people.</summary>
public sealed record Problem
{
    public Problem(string code, string message)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);
        Code = code;
        Message = message;
    }

    public string Code { get; }

    public string Message { get; }
}
