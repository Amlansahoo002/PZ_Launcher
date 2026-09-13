using PZLauncher.Profiles;

namespace PZLauncher.Services
{
    internal sealed class ProfileService
    {
        private readonly string profilesRoot;

        public ProfileService(string profilesRoot)
        {
            this.profilesRoot = profilesRoot;
        }

        public IReadOnlyList<GameProfile> GetOrCreateDefaultProfiles()
        {
            Directory.CreateDirectory(profilesRoot);

            var profiles = new[]
            {
                CreateProfile("vanilla", "Vanilla", GameProfileKind.Vanilla, "Jeu propre, sans mods actifs."),
                CreateProfile("solo", "Solo", GameProfileKind.Solo, "Profil de jeu perso avec sa propre liste de mods."),
                CreateProfile("mp", "Multiplayer", GameProfileKind.Multiplayer, "Profil MP separe pour eviter les collisions de mods."),
                CreateProfile("debug", "Debug / Tests", GameProfileKind.Debug, "Profil isole pour logs, patchs et essais.")
            };

            foreach (var profile in profiles)
            {
                EnsureCacheSkeleton(profile);
            }

            return profiles;
        }

        public int CountEnabledMods(GameProfile profile)
        {
            if (!File.Exists(profile.DefaultModsFile))
            {
                return 0;
            }

            return File.ReadLines(profile.DefaultModsFile)
                .Count(line => line.TrimStart().StartsWith("mod =", StringComparison.OrdinalIgnoreCase));
        }

        private GameProfile CreateProfile(string id, string name, GameProfileKind kind, string description)
        {
            return new GameProfile
            {
                Id = id,
                Name = name,
                Kind = kind,
                Description = description,
                CacheDir = Path.Combine(profilesRoot, id, "cache")
            };
        }

        private static void EnsureCacheSkeleton(GameProfile profile)
        {
            Directory.CreateDirectory(profile.CacheDir);
            Directory.CreateDirectory(profile.ModsDirectory);
            Directory.CreateDirectory(Path.Combine(profile.CacheDir, "Saves"));
            Directory.CreateDirectory(Path.Combine(profile.CacheDir, "Screenshots"));

            if (!File.Exists(profile.DefaultModsFile))
            {
                File.WriteAllText(profile.DefaultModsFile, DefaultActiveModsFile());
            }
        }

        private static string DefaultActiveModsFile()
        {
            string eol = Environment.NewLine;

            return string.Join(eol, new[]
            {
                "VERSION = 1,",
                "",
                "mods",
                "{",
                "}",
                "",
                "maps",
                "{",
                "}",
                ""
            });
        }
    }
}
