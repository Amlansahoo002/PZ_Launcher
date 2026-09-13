namespace PZLauncher.Profiles
{
    internal enum GameProfileKind
    {
        Vanilla,
        Solo,
        Multiplayer,
        Debug
    }

    internal sealed class GameProfile
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public GameProfileKind Kind { get; init; }
        public string CacheDir { get; init; } = string.Empty;
        public LaunchSettings Launch { get; init; } = new();

        public string ModsDirectory => Path.Combine(CacheDir, "mods");
        public string DefaultModsFile => Path.Combine(ModsDirectory, "default.txt");
        public string ConsoleLogPath => Path.Combine(CacheDir, "console.txt");

        public override string ToString()
        {
            return Name;
        }
    }
}
