using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>Reads Sophos Central lists from the tenant's data region, following every page.</summary>
internal sealed class SophosClient(HttpClient http, SophosSession session)
{
    /// <summary>Most pages read from one list, so an API that never ends its paging cannot loop forever.</summary>
    public const int MaxPages = 1000;

    public async Task<Result<SophosCollection>> GetAllAsync(SophosSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var items = new JsonArray();
        var pages = 0;
        var next = (string?)FirstQuery(source);
        while (next is not null)
        {
            if (pages == MaxPages)
            {
                return Result.Fail<SophosCollection>(new Problem("sophos.too_many_pages", $"Sophos Central gave more than {MaxPages} pages of {source.Name}."));
            }

            using var request = SophosHttp.Get(new Uri(session.DataRegion, $"{source.Path}?{next}"), session.Token);
            request.Headers.Add("X-Tenant-ID", session.TenantId);
            var page = (await SophosHttp.SendAsync(http, request, source.Name, cancellationToken)).Bind(response => SophosHttp.Body(response, source.Name));
            if (page is Result<JsonObject>.Fail failed)
            {
                return Result.Fail<SophosCollection>(failed.Errors);
            }

            var body = ((Result<JsonObject>.Ok)page).Value;
            pages++;
            foreach (var item in body["items"] as JsonArray ?? [])
            {
                items.Add(item?.DeepClone());
            }

            next = NextQuery(source, body["pages"] as JsonObject);
        }

        return Result.Ok(new SophosCollection(source, items, pages));
    }

    private static string FirstQuery(SophosSource source) => source.Paging == SophosPaging.Keyed
        ? "pageSize=500&view=full"
        : "pageSize=100&pageTotal=true";

    /// <summary>The query for the page after this one, or null after the last page.</summary>
    internal static string? NextQuery(SophosSource source, JsonObject? pages)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Paging == SophosPaging.Keyed)
        {
            return pages?["nextKey"]?.GetValue<string>() is { Length: > 0 } key ? $"{FirstQuery(source)}&pageFromKey={Uri.EscapeDataString(key)}" : null;
        }

        var current = pages?["current"]?.GetValue<int>() ?? 1;
        var total = pages?["total"]?.GetValue<int>() ?? 1;
        return current < total ? string.Create(CultureInfo.InvariantCulture, $"{FirstQuery(source)}&page={current + 1}") : null;
    }
}
