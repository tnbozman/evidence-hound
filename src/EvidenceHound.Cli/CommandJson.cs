using System.Text.Encodings.Web;
using System.Text.Json;

namespace EvidenceHound.Cli;

/// <summary>How commands write JSON, for an agent to read and for the files they keep.</summary>
internal static class CommandJson
{
    /// <summary>Indented, with text such as <c>'</c>, <c>&lt;</c> and <c>+</c> left as it is: the agent-facing answers of the newer commands.</summary>
    public static readonly JsonSerializerOptions Readable = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Indented, with HTML-sensitive characters escaped: the default of System.Text.Json, kept by the older reports and the files hound keeps
    /// so their bytes do not change. One setting for all is discovered work (s19-D5).
    /// </summary>
    public static readonly JsonSerializerOptions Escaped = new() { WriteIndented = true };
}
