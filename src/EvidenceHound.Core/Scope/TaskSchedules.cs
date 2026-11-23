namespace EvidenceHound.Core.Scope;

/// <summary>A CISO Assistant task schedule: its frequency and interval, such as every 3 months as <c>MONTHLY</c>, 3.</summary>
public sealed record TaskSchedule(string Frequency, int Interval);

/// <summary>How the evidence plan's intervals are written as CISO Assistant schedules. Pure.</summary>
public static class TaskSchedules
{
    /// <summary>3 or 6 months monthly with that interval; 12 months yearly, once.</summary>
    public static TaskSchedule Of(int months) => months == 12 ? new("YEARLY", 1) : new("MONTHLY", months);
}
