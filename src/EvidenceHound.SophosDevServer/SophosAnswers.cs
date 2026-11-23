using System.Text.Json.Nodes;

namespace EvidenceHound.SophosDevServer;

public static class SophosAnswers
{
    public static JsonObject Whoami(SophosDevTenant tenant, Uri url) => throw new NotImplementedException();

    public static Dictionary<string, JsonArray> Items(SophosDevTenant tenant) => throw new NotImplementedException();
}
