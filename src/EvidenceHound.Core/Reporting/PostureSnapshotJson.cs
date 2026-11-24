using System.Text.Json;
using System.Text.Json.Serialization;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Reporting;

/// <summary>s12-04: a posture snapshot as JSON, snake_case, the form kept in the evidence store and uploaded to CISO Assistant.</summary>
public static class PostureSnapshotJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower), new PeriodConverter() },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static string Write(PostureSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options) + "\n";

    public static Result<PostureSnapshot> Read(string json, string source)
    {
        try
        {
            return JsonSerializer.Deserialize<PostureSnapshot>(json, Options) is { } snapshot
                ? Result.Ok(snapshot)
                : Result.Fail<PostureSnapshot>(new Problem("report.snapshot_invalid", $"{source}: the snapshot is empty."));
        }
        catch (JsonException ex)
        {
            return Result.Fail<PostureSnapshot>(new Problem("report.snapshot_invalid", $"{source}: not a posture snapshot: {ex.Message}"));
        }
    }

    private sealed class PeriodConverter : JsonConverter<ReportingPeriod>
    {
        public override ReportingPeriod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReportingPeriod.Parse(reader.GetString() ?? "").Match(p => p, ps => throw new JsonException(ps[0].Message));

        public override void Write(Utf8JsonWriter writer, ReportingPeriod value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
