using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;


internal static class LanguagePacks
{
    internal sealed record Pack(L.Language Language, Dictionary<string, string> Translations);
    internal sealed record Discovery(List<Pack> Packs, List<string> Issues);
    internal static Discovery Discover(string directory, IReadOnlyDictionary<string, string> english, IEnumerable<string> reservedCodes)
    {
        var result = new Discovery([], []);
        var codes = new HashSet<string>(reservedCodes, StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(directory)) return result;
            foreach (string path in Directory.GetFiles(directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Pack exceeds 2 MB.");
                    var pack = Parse(File.ReadAllText(path, new UTF8Encoding(false, true)), english);
                    if (!codes.Add(pack.Language.Code)) throw new InvalidDataException("Duplicate or built-in language code: " + pack.Language.Code);
                    result.Packs.Add(pack);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException)
                { result.Issues.Add(Path.GetFileName(path) + ": " + ex.Message); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { result.Issues.Add("Cannot read language folder: " + ex.Message); }
        return result;
    }

    internal static Pack Parse(string json, IReadOnlyDictionary<string, string> english)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected a language pack object.");
        if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int schema) || schema != 1)
            throw new InvalidDataException("Expected schemaVersion 1.");
        string Read(string key, string? fallback = null)
        {
            if (!root.TryGetProperty(key, out var value) && fallback != null) return fallback;
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidDataException("Missing or invalid " + key + ".");
            return value.GetString()!.Trim();
        }
        string code = Read("code"), name = Read("name"), cultureName = Read("culture"), gameLanguage = Read("gameLanguage", "EN");
        if (code.Length > 40 || !Regex.IsMatch(code, @"\A[a-zA-Z]{2,3}(?:-[a-zA-Z0-9]{2,8})*\z"))
            throw new InvalidDataException("Invalid language code; use a BCP 47 tag such as it or pt-BR.");
        if (name.Length > 80 || name.Any(char.IsControl)) throw new InvalidDataException("Invalid display name.");
        var culture = CultureInfo.GetCultureInfo(cultureName);
        if (culture.Name.Length == 0) throw new InvalidDataException("A named culture is required.");
        if (!Regex.IsMatch(gameLanguage, @"\A[A-Z]{2,8}(?:_[A-Z]{2,8})?\z")) throw new InvalidDataException("Invalid gameLanguage folder code.");
        if (!root.TryGetProperty("translations", out var entries) || entries.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Missing translations object.");
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateObject())
        {
            if (!english.TryGetValue(entry.Name, out string? source)) continue; 
            if (entry.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.Value.GetString()))
                throw new InvalidDataException("Empty or non-text translation: " + entry.Name);
            string value = entry.Value.GetString()!;
            try
            {
                if (!Arguments(source).SetEquals(Arguments(value))) throw new FormatException("Keep the same numbered placeholders.");
                var sourceFormats = Regex.Matches(source, @"\{\d+(?:[^{}]*)\}").Select(m => m.Value).ToHashSet();
                var translatedFormats = Regex.Matches(value, @"\{\d+(?:[^{}]*)\}").Select(m => m.Value).ToHashSet();
                if (!sourceFormats.SetEquals(translatedFormats)) throw new FormatException("Keep the original placeholder format specifiers.");
                
                _ = string.Format(culture, value, Enumerable.Repeat<object>(1234, CompositeFormat.Parse(source).MinimumArgumentCount).ToArray());
            }
            catch (FormatException ex) { throw new InvalidDataException(entry.Name + ": " + ex.Message, ex); }
            if (!translations.TryAdd(entry.Name, value)) throw new InvalidDataException("Duplicate translation key: " + entry.Name);
        }
        if (translations.Count == 0) throw new InvalidDataException("Provide at least one known translation key.");
        return new Pack(new(code, name, culture.Name, gameLanguage, Community: true), translations);
    }

    private static HashSet<int> Arguments(string text)
    {
        _ = CompositeFormat.Parse(text);
        var result = new HashSet<int>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;
            if (i + 1 < text.Length && text[i + 1] == '{') { i++; continue; }
            int start = ++i;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            result.Add(int.Parse(text[start..i], CultureInfo.InvariantCulture));
        }
        return result;
    }

    internal static void WriteContributorFiles(string directory, IReadOnlyDictionary<string, string> english)
    {
        Directory.CreateDirectory(directory);
        void WriteNew(string name, string text)
        {
            string path = Path.Combine(directory, name);
            if (!File.Exists(path)) File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        WriteNew("template.json.example", JsonSerializer.Serialize(new
        {
            schemaVersion = 1, code = "it", name = "Italiano", culture = "it-IT", gameLanguage = "IT",
            translations = new Dictionary<string, string> { ["nav.play"] = "Gioca", ["nav.settings"] = "Impostazioni" }
        }, options));
        
        string version = typeof(LanguagePacks).Assembly.GetName().Version?.ToString(3) ?? "current";
        WriteNew("english-reference-" + version + ".json.example", JsonSerializer.Serialize(english, options));
        WriteNew("README.md", """
            # Community language packs

            Copy template.json.example to a new file such as it.json in this folder.
            Set code (BCP 47), name (native display name), culture (e.g. it-IT), and gameLanguage
            (optional Project Zomboid translation folder, default EN). Add translated entries
            from english-reference-*.json.example under translations. Save as UTF-8, then restart
            the launcher and select the language in the sidebar. No rebuild is needed.

            Missing keys use English. Preserve {0}, {1}, etc., their format specifiers and \n
            line breaks. Keys and technical identifiers must remain unchanged. Unknown keys
            are ignored to allow packs from newer versions. Built-in language codes cannot
            be replaced; use a distinct variant code for a community variation.

            Packs use schemaVersion 1 and must be JSON objects under 2 MB. Invalid packs
            are skipped and reported in Settings > Community language packs and launcher.log.
            Only *.json files in this folder are loaded; *.example files are never loaded.
            Remove a pack and restart to uninstall it; an unavailable selected language uses English.

            Check every page at the minimum window size, dialogs, long labels, and non-Latin
            fonts before sharing your JSON. Right-to-left layout is not implemented yet.
            The launcher uses installed Windows fonts and never runs code from a language pack.
            More details are in LANGUAGE-PACKS.md beside the launcher executable.
            """);
    }
}
