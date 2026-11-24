using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Reporting;

/// <summary>s12-04: a posture snapshot as JSON, snake_case, the form kept in the evidence store and uploaded to CISO Assistant.</summary>
public static class PostureSnapshotJson
{
    public static string Write(PostureSnapshot snapshot) => throw new NotImplementedException();

    public static Result<PostureSnapshot> Read(string json, string source) => throw new NotImplementedException();
}
