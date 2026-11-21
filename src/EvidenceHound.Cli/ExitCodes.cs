namespace EvidenceHound.Cli;

/// <summary>Process exit codes shared by every command.</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Unexpected = 1;
    public const int Usage = 2;
    public const int Configuration = 3;
    public const int PublishFailed = 4;
    public const int Cancelled = 130;
}
