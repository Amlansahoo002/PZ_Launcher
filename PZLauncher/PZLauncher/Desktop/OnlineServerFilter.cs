namespace PZLauncher.Desktop;

internal sealed class OnlineServerFilter
{
    public string Search { get; set; } = "";
    public string Version { get; set; } = "";
    public int MinimumPlayers { get; set; }
    public int MaximumPing { get; set; }
    public int Mods { get; set; }
    internal bool Matches(OnlineServerInfo server) =>
        (server.Name + " " + server.Host + " " + server.Version).Contains(Search, StringComparison.OrdinalIgnoreCase)
        && (Version.Length == 0 || server.Version.Equals(Version, StringComparison.OrdinalIgnoreCase))
        && server.Players >= MinimumPlayers
        && (MaximumPing == 0 || server.Ping >= 0 && server.Ping <= MaximumPing)
        && (Mods == 0 || Mods == 1 && server.HasMods == true || Mods == 2 && server.HasMods == false || Mods == 3 && server.HasMods == null);
}
