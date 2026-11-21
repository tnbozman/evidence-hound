using System.Globalization;
using EvidenceHound.Core.Results;

namespace EvidenceHound.CisoAssistant;

/// <summary>Turns an unsuccessful CISO Assistant response into a problem. Pure.</summary>
internal static class ApiProblems
{
    private static readonly int MaxDetail = 500;

    public static Problem FromStatus(int status, string body, string action)
    {
        var http = status.ToString(CultureInfo.InvariantCulture);
        return status switch
        {
            401 or 403 => new Problem("ciso_assistant.auth", $"CISO Assistant refused the request {action} (HTTP {http}). Check the token and its permissions."),
            404 => new Problem("ciso_assistant.not_found", $"CISO Assistant has nothing at the address {action} (HTTP 404)."),
            >= 400 and < 500 => new Problem("ciso_assistant.rejected", $"CISO Assistant rejected the request {action} (HTTP {http}): {Detail(body)}"),
            _ => Unavailable($"CISO Assistant failed {action} (HTTP {http})."),
        };
    }

    public static Problem Unavailable(string message) => new("ciso_assistant.unavailable", message);

    public static Problem NotFound(string what, object selector) =>
        new("ciso_assistant.not_found", $"No {what} in CISO Assistant matches {selector}.");

    public static Problem Ambiguous(string what, object selector, int count) =>
        new("ciso_assistant.ambiguous", $"{count.ToString(CultureInfo.InvariantCulture)} {what}s in CISO Assistant match {selector}; expected one.");

    private static string Detail(string body) => body.Length <= MaxDetail ? body : string.Concat(body.AsSpan(0, MaxDetail), "...");
}
