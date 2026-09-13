using PZLauncher.Profiles;

namespace PZLauncher.Services
{
    internal sealed class OptionsIniService
    {
        public string EnsureProfileOptionsFile(GameProfile profile)
        {
            Directory.CreateDirectory(profile.CacheDir);

            string profileOptions = Path.Combine(profile.CacheDir, "options.ini");
            if (File.Exists(profileOptions))
            {
                return profileOptions;
            }

            string globalOptions = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Zomboid",
                "options.ini");

            if (File.Exists(globalOptions))
            {
                File.Copy(globalOptions, profileOptions, overwrite: false);
            }
            else
            {
                File.WriteAllText(profileOptions, string.Empty);
            }

            return profileOptions;
        }

        public IReadOnlyList<OptionEntry> Load(string path)
        {
            if (!File.Exists(path))
            {
                return Array.Empty<OptionEntry>();
            }

            var entries = new List<OptionEntry>();

            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith(";"))
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }

                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();

                if (!string.IsNullOrWhiteSpace(key))
                {
                    entries.Add(new OptionEntry { Key = key, Value = value });
                }
            }

            return entries;
        }

        public void Save(string path, IEnumerable<OptionEntry> entries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            var lines = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                .Select(entry => $"{entry.Key.Trim()}={entry.Value?.Trim() ?? string.Empty}")
                .ToArray();

            File.WriteAllLines(path, lines);
        }

        public void ImportGlobalOptions(GameProfile profile)
        {
            string globalOptions = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Zomboid",
                "options.ini");

            if (!File.Exists(globalOptions))
            {
                throw new FileNotFoundException("options.ini global introuvable.", globalOptions);
            }

            Directory.CreateDirectory(profile.CacheDir);
            File.Copy(globalOptions, Path.Combine(profile.CacheDir, "options.ini"), overwrite: true);
        }
    }
}
