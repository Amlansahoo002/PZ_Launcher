global using static PZLauncher.Desktop.L;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace PZLauncher.Desktop;

internal static class L
{
    internal sealed record Language(string Code, string Name, string CultureName, string GameLanguage, bool Community = false)
    {
        public override string ToString() => Name;
    }
    internal static readonly Language[] BuiltInLanguages =
    [
        new("en", "English", "en-GB", "EN"), new("fr", "Français", "fr-FR", "FR"),
        new("es", "Español", "es-ES", "ES"), new("ru", "Русский", "ru-RU", "RU"),
        new("pt-BR", "Português (Brasil)", "pt-BR", "PTBR"), new("de", "Deutsch", "de-DE", "DE"),
        new("zh-CN", "简体中文", "zh-CN", "CN")
    ];
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs =
        BuiltInLanguages.ToDictionary(l => l.Code, l => Read(l.Code), StringComparer.OrdinalIgnoreCase);
    internal static readonly Language[] Languages;
    internal static readonly IReadOnlyList<string> PackIssues;
    internal static string PacksDirectory => Path.Combine(LauncherStorage.Root, "languages");
    static L()
    {
        var packs = LanguagePacks.Discover(PacksDirectory, Catalogs["en"], BuiltInLanguages.Select(l => l.Code));
        Languages = [.. BuiltInLanguages, .. packs.Packs.Select(p => p.Language)];
        PackIssues = packs.Issues;
        foreach (var pack in packs.Packs) Catalogs.Add(pack.Language.Code, pack.Translations);
        foreach (string issue in PackIssues) LauncherStorage.Log("Language pack: " + issue);
    }
    internal static string Code { get; private set; } = "en";
    internal static Language Current => Languages.First(l => l.Code == Code);
    internal static CultureInfo Culture => CultureInfo.GetCultureInfo(Current.CultureName);
    internal static void SetLanguage(string? code)
    {
        Code = Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }
    internal static string T(string key, params object[] args)
    {
        string value = Catalogs[Code].GetValueOrDefault(key) ?? Catalogs["en"].GetValueOrDefault(key)
            ?? throw new KeyNotFoundException("Missing UI translation: " + key);
        return args.Length == 0 ? value : string.Format(Culture, value, args);
    }
    internal static IReadOnlyDictionary<string, string> Catalog(string code) => Catalogs[code];
    private static Dictionary<string, string> Read(string code)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PZLauncher.Resources.Languages." + code + ".json")
            ?? throw new InvalidDataException("Missing language resource: " + code);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}
