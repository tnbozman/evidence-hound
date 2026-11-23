using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Profiles;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>
/// Extract phase: every query's items (identities hashed unless the instance includes them) and every dataset built from them. A
/// dataset is built only from complete inputs: if any query it reads failed or was unavailable, so is the dataset, naming that query.
/// </summary>
public static class Extraction
{
    public static IReadOnlyDictionary<string, RowSource> Build(
        Profile profile, IReadOnlyDictionary<string, QueryResult> queries, bool includeIdentities, Renderer? renderer = null, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(queries);
        var sources = new Dictionary<string, RowSource>(StringComparer.Ordinal);
        foreach (var (name, result) in queries)
        {
            sources[name] = result switch
            {
                QueryResult.Collected collected => new RowSource.Rows(name, QueryEvidence.Items(collected, includeIdentities), [name], 0),
                QueryResult.Unavailable unavailable => new RowSource.Unavailable(name, unavailable.Reason),
                QueryResult.Failed failed => new RowSource.Failed(name, failed.Reason),
                _ => new RowSource.Failed(name, "It was not read."),
            };
        }

        var datasets = profile.Datasets.ToDictionary(d => d.Name, StringComparer.Ordinal);
        var builder = new DatasetBuilder(sources, datasets, renderer ?? Renderer.Empty, now ?? DateTimeOffset.UnixEpoch);
        foreach (var dataset in profile.Datasets)
        {
            builder.Get(dataset.Name);
        }

        return sources;
    }

    private sealed class DatasetBuilder(Dictionary<string, RowSource> sources, Dictionary<string, Dataset> datasets, Renderer renderer, DateTimeOffset now)
    {
        private readonly HashSet<string> _building = new(StringComparer.Ordinal);

        public RowSource Get(string name)
        {
            if (sources.TryGetValue(name, out var known))
            {
                return known;
            }

            if (!datasets.TryGetValue(name, out var dataset) || !_building.Add(name))
            {
                return new RowSource.Failed(name, "It names no query or dataset that could be built.");
            }

            var built = Build(dataset);
            _building.Remove(name);
            sources[name] = built;
            return built;
        }

        private RowSource Build(Dataset dataset)
        {
            var inputs = dataset.Joins.Select(j => j.With).Prepend(dataset.From).Select(Get).ToArray();
            switch (inputs.FirstOrDefault(i => i is not RowSource.Rows))
            {
                case RowSource.Failed failed:
                    return new RowSource.Failed(dataset.Name, $"It is built from '{failed.Name}', which could not be read: {failed.Reason}");
                case RowSource.Unavailable unavailable:
                    return new RowSource.Unavailable(dataset.Name, $"It is built from '{unavailable.Name}', which is not available: {unavailable.Reason}");
            }

            var rows = inputs.Cast<RowSource.Rows>().ToArray();
            var items = rows[0].Items.Select(i => i?.DeepClone()).ToList();
            var unmatched = rows.Sum(r => r.Unmatched);
            foreach (var (join, with) in dataset.Joins.Zip(rows.Skip(1)))
            {
                unmatched += Join(items, join, with.Items);
            }

            if (dataset.Flatten is { } flatten)
            {
                switch (Flatten(items, Path(flatten)))
                {
                    case { } flattened:
                        items = flattened;
                        break;
                    default:
                        return new RowSource.Failed(dataset.Name, $"A row has no list at {flatten} to flatten.");
                }
            }

            var kept = new List<JsonNode?>();
            foreach (var item in items)
            {
                var tests = dataset.Where.Select(t => FieldTester.Test(t, item, renderer, now)).ToArray();
                if (tests.OfType<TestOutcome.Unknown>().FirstOrDefault() is { } unknown)
                {
                    return new RowSource.Failed(dataset.Name, $"Its filter could not be applied: {unknown.Reason}.");
                }

                if (tests.All(t => t == TestOutcome.Pass))
                {
                    kept.Add(dataset.Fields.Count == 0 ? item : Pick(item, dataset.Fields));
                }
            }

            var queries = rows.SelectMany(r => r.Queries).Distinct(StringComparer.Ordinal).ToArray();
            return new RowSource.Rows(dataset.Name, new JsonArray([.. kept]), queries, unmatched);
        }

        /// <summary>Attaches matching rows to each item; returns how many items matched nothing in a required join.</summary>
        private static int Join(List<JsonNode?> items, Join join, JsonArray with)
        {
            var left = Path(join.Left);
            var right = Path(join.Right);
            var keyed = with.Select(w => (Row: w, Keys: Keys(right.Select(w)))).ToArray();
            var unmatched = 0;
            foreach (var item in items.OfType<JsonObject>())
            {
                var keys = Keys(left.Select(item));
                var matches = keyed.Where(w => w.Keys.Overlaps(keys)).Select(w => w.Row?.DeepClone()).ToArray();
                item[join.As] = new JsonArray(matches);
                if (matches.Length == 0 && !join.Optional)
                {
                    unmatched++;
                }
            }

            return unmatched;
        }

        private static HashSet<string> Keys(PathMatch match) =>
            match.Found ? match.Values.OfType<JsonValue>().Select(Renderer.Text).ToHashSet(StringComparer.Ordinal) : [];

        /// <summary>One row per element of the list at <paramref name="path"/>, with the original row under <c>parent</c>; null when a row has no list.</summary>
        private static List<JsonNode?>? Flatten(List<JsonNode?> items, JsonPath path)
        {
            var rows = new List<JsonNode?>();
            foreach (var item in items)
            {
                if (path.Select(item) is not { Found: true, Values: [JsonArray list] })
                {
                    return null;
                }

                rows.AddRange(list.Select(element =>
                {
                    var row = element is JsonObject obj ? (JsonObject)obj.DeepClone() : new JsonObject { ["value"] = element?.DeepClone() };
                    row["parent"] = item?.DeepClone();
                    return (JsonNode?)row;
                }));
            }

            return rows;
        }

        /// <summary>The named fields of a row. A path that finds nothing leaves its field out, so a check on it is unable to assess.</summary>
        private static JsonObject Pick(JsonNode? item, IReadOnlyDictionary<string, string> fields)
        {
            var row = new JsonObject();
            foreach (var (name, text) in fields)
            {
                var path = Path(text);
                if (path.Select(item) is { Found: true } match)
                {
                    row[name] = path.IsMulti ? new JsonArray([.. match.Values.Select(v => v?.DeepClone())]) : match.Values[0]?.DeepClone();
                }
            }

            return row;
        }

        private static JsonPath Path(string text) => JsonPath.Parse(text).Match(p => p, _ => JsonPath.Root);
    }
}
