namespace PZLauncher.Desktop;

internal sealed record ModResolution(List<string> Ordered, List<string> Added, List<string> Issues)
{
    public bool Success => Issues.Count == 0;
}
internal static class ModResolver
{
    internal static ModResolution Resolve(IEnumerable<InstalledMod> catalog, IEnumerable<string> requested)
    {
        var lookup = catalog.GroupBy(m => m.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var original = requested.Distinct(StringComparer.Ordinal).ToList();
        var order = original.ToList();
        var included = new HashSet<string>(original, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var issues = new List<string>();
        void Include(string id, string? parent)
        {
            if (!visited.Add(id)) return;
            if (!lookup.TryGetValue(id, out var mod))
            {
                issues.Add(T("resolver.missing", id, parent ?? id)); return;
            }
            if (!mod.Available) issues.Add(T("resolver.unavailable", mod.Name, id));
            foreach (string dependency in mod.Requires)
            {
                if (included.Add(dependency)) order.Add(dependency);
                Include(dependency, mod.Name);
            }
        }
        foreach (string id in original) Include(id, null);
        var edges = order.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var incoming = order.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        void Before(string first, string second)
        {
            if (included.Contains(first) && included.Contains(second) && edges[first].Add(second)) incoming[second]++;
        }
        foreach (string id in order)
        {
            if (!lookup.TryGetValue(id, out var mod)) continue;
            foreach (string dependency in mod.Requires) Before(dependency, id);
            foreach (string after in mod.LoadAfter) Before(after, id);
            foreach (string before in mod.LoadBefore) Before(id, before);
            foreach (string conflict in mod.Incompatible.Where(included.Contains))
                issues.Add(T("resolver.conflict", mod.Name, lookup.GetValueOrDefault(conflict)?.Name ?? conflict));
        }
        var sorted = new List<string>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        while (sorted.Count < order.Count)
        {
            string? next = order.FirstOrDefault(id => incoming[id] == 0 && !emitted.Contains(id));
            if (next == null) break;
            emitted.Add(next); sorted.Add(next);
            foreach (string child in edges[next]) incoming[child]--;
        }
        if (sorted.Count != order.Count) issues.Add(T("resolver.cycle", string.Join(" → ", order.Except(sorted))));
        return new(sorted, order.Except(original).ToList(), issues.Distinct().ToList());
    }
}
