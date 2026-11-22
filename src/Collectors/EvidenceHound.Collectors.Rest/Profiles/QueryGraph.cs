namespace EvidenceHound.Collectors.Rest.Profiles;

/// <summary>Which queries each query waits for: the one it runs <c>for_each</c> item of, and those whose variables it uses.</summary>
public static class QueryGraph
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var queries = profile.AllQueries.ToArray();
        var setBy = queries.SelectMany(q => q.Vars.Keys.Select(v => (Var: v, Query: q.Name)))
            .GroupBy(p => p.Var, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Query, StringComparer.Ordinal);
        return queries.GroupBy(q => q.Name, StringComparer.Ordinal).Select(g => g.First()).ToDictionary(
            q => q.Name,
            q => (IReadOnlyList<string>)[.. (q.ForEach is { } forEach ? [forEach] : Array.Empty<string>())
                .Concat(q.Texts.SelectMany(Template.References).Where(r => r.Kind == ReferenceKind.Value && setBy.ContainsKey(r.Name)).Select(r => setBy[r.Name]))
                .Distinct(StringComparer.Ordinal)],
            StringComparer.Ordinal);
    }

    /// <summary>The queries in an order where each comes after everything it waits for (bootstrap first); null when there is a cycle.</summary>
    public static IReadOnlyList<Query>? Order(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var graph = Dependencies(profile);
        var byName = profile.AllQueries.GroupBy(q => q.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var ordered = new List<Query>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(string name)
        {
            if (done.Contains(name) || !byName.TryGetValue(name, out var query))
            {
                return true;
            }

            if (!visiting.Add(name))
            {
                return false;
            }

            if (!graph[name].All(Visit))
            {
                return false;
            }

            visiting.Remove(name);
            done.Add(name);
            ordered.Add(query);
            return true;
        }

        return profile.Bootstrap.Concat(profile.Queries).All(q => Visit(q.Name)) ? ordered : null;
    }
}
