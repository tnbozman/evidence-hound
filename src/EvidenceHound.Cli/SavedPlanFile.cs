using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Cli;

/// <summary>A saved plan as JSON.</summary>
internal static class SavedPlanFile
{
    public static string Write(SavedPlan plan) => throw new NotImplementedException(plan.ToString());

    public static Result<SavedPlan> Read(string json, string source) => Result.Fail<SavedPlan>(new Problem("not_implemented", json + source));
}
