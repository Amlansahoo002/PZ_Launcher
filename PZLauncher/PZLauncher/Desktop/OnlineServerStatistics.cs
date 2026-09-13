namespace PZLauncher.Desktop;

internal sealed record ServerVersionCount(string Version, int Servers, int Occupied, long Players);
internal sealed record VisibleModCount(string Id, int Servers);
internal sealed record OnlineServerStatistics(int Total, int Vanilla, int Modded, int UnknownMods,
    int Occupied, int Empty, int UnknownPlayers, long Players, int PublicLists, int CompleteLists,
    IReadOnlyList<ServerVersionCount> Versions, IReadOnlyList<VisibleModCount> TopMods)
{
    internal static List<OnlineServerInfo> Unique(IEnumerable<OnlineServerInfo> source) => source
        .Where(s => s.Responded).GroupBy(s => (s.Host.Trim().ToLowerInvariant(), s.Port))
        .Select(g => g.OrderByDescending(s => s.CheckedAt).First()).ToList();

    
    internal static List<string>? PublicMods(OnlineServerInfo server)
    {
        if (!server.Rules.TryGetValue("mods", out string? text)) return null;
        try { return OnlineProfiles.ParseModIds(text); }
        catch (InvalidDataException) { return null; }
    }
    internal static int? PublicModCount(OnlineServerInfo server) =>
        int.TryParse(server.Rules.GetValueOrDefault("modCount"), out int count) && count >= 0 ? count : null;
    internal static string PublicVersion(OnlineServerInfo server) => server.Rules.TryGetValue("version", out string? version)
        && !string.IsNullOrWhiteSpace(version) ? version.Trim() : server.Version.Trim();
    internal static bool? PublicHasMods(OnlineServerInfo server)
    {
        if (PublicMods(server) is { Count: > 0 }) return true;
        if (PublicModCount(server) is { } count) return count > 0;
        var tags = server.Tags.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        bool modded = tags.Contains("modded"), vanilla = tags.Contains("vanilla");
        return modded == vanilla ? null : modded;
    }
    internal static OnlineServerStatistics Build(IEnumerable<OnlineServerInfo> source)
    {
        var servers = Unique(source);
        int vanilla = 0, modded = 0, occupied = 0, empty = 0, lists = 0, complete = 0;
        var mods = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var server in servers)
        {
            switch (PublicHasMods(server)) { case true: modded++; break; case false: vanilla++; break; }
            if (server.Players > 0) occupied++; else if (server.Players == 0) empty++;
            if (PublicMods(server) is not { } ids) continue;
            lists++;
            if (PublicModCount(server) == ids.Count) complete++;
            foreach (string id in ids) mods[id] = mods.GetValueOrDefault(id) + 1;
        }
        var versions = servers.GroupBy(PublicVersion, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ServerVersionCount(g.Key, g.Count(), g.Count(s => s.Players > 0), g.Sum(s => (long)Math.Max(0, s.Players))))
            .OrderByDescending(v => v.Servers).ThenBy(v => v.Version, StringComparer.Ordinal).ToList();
        var ranking = mods.Select(m => new VisibleModCount(m.Key, m.Value)).OrderByDescending(m => m.Servers)
            .ThenBy(m => m.Id, StringComparer.Ordinal).Take(50).ToList();
        return new(servers.Count, vanilla, modded, servers.Count - vanilla - modded,
            occupied, empty, servers.Count - occupied - empty, servers.Sum(s => (long)Math.Max(0, s.Players)),
            lists, complete, versions, ranking);
    }
}
