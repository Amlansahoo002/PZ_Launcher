using System.Text.Json;

namespace PZLauncher.Desktop;

internal static class LanguagePackVerification
{
    internal static void Run(string root, Action<string, Action> check)
    {
        var english = L.Catalog("en");
        string Json(Dictionary<string, string>? translations = null, string code = "it", string culture = "it-IT", string game = "IT", int schema = 1) =>
            JsonSerializer.Serialize(new { schemaVersion = schema, code, name = "Italiano", culture, gameLanguage = game,
                translations = translations ?? new() { ["nav.play"] = "Gioca" } });
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        check("Langues communautaires : découverte, traduction partielle et repli anglais", () =>
        {
            string folder = Path.Combine(root, "language-discovery"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "it.json"), Json(new() { ["nav.play"] = "Gioca", ["future.key"] = "Future translation" }));
            File.WriteAllText(Path.Combine(folder, "ignored.json.example"), "not JSON");
            var result = LanguagePacks.Discover(folder, english, L.BuiltInLanguages.Select(l => l.Code));
            Assert(result.Packs.Count == 1 && result.Issues.Count == 0, "Pack non découvert.");
            var pack = result.Packs.Single();
            Assert(pack.Language.Community && pack.Language.GameLanguage == "IT" && pack.Language.CultureName == "it-IT", "Métadonnées perdues.");
            Assert(pack.Translations.Count == 1 && pack.Translations["nav.play"] == "Gioca", "Filtrage de clés incorrect.");
            Assert((pack.Translations.GetValueOrDefault("nav.settings") ?? english["nav.settings"]) == "Settings", "Repli incorrect.");
        });
        check("Langues communautaires : fichiers invalides isolés, codes réservés et doublons", () =>
        {
            string folder = Path.Combine(root, "language-errors"); Directory.CreateDirectory(folder);
            var invalid = new[]
            {
                "{", "[]", Json(schema: 9), Json().Replace("\"schemaVersion\":1", "\"schemaVersion\":\"1\""),
                Json(code: "../bad"), Json(culture: "bad/culture"), Json(game: "../EN"), Json(code: "EN"),
                Json(new() { ["nav.play"] = "" }), Json(new() { ["home.modsCount"] = "Mods: {9}" }),
                Json(new() { ["home.modsCount"] = "Mods: {0:X}" }),
                Json(new() { ["nav.play"] = "Broken {" })
            };
            for (int i = 0; i < invalid.Length; i++) File.WriteAllText(Path.Combine(folder, "bad-" + i.ToString("D2") + ".json"), invalid[i]);
            File.WriteAllText(Path.Combine(folder, "a-valid.json"), Json());
            File.WriteAllText(Path.Combine(folder, "z-duplicate.json"), Json(code: "IT"));
            File.WriteAllBytes(Path.Combine(folder, "bad-encoding.json"), [0x7B, 0x22, 0xC3, 0x28, 0x22, 0x7D]);
            var result = LanguagePacks.Discover(folder, english, L.BuiltInLanguages.Select(l => l.Code));
            Assert(result.Packs.Count == 1 && result.Issues.Count == invalid.Length + 2,
                "Un pack invalide a bloqué le chargement ou a été accepté : " + JsonSerializer.Serialize(result));
        });
        check("Langues communautaires : modèles non chargés et fichiers contributeur préservés", () =>
        {
            string folder = Path.Combine(root, "language-templates");
            LanguagePacks.WriteContributorFiles(folder, english);
            string template = Path.Combine(folder, "template.json.example");
            Assert(LanguagePacks.Parse(File.ReadAllText(template), english).Translations["nav.play"] == "Gioca", "Modèle invalide.");
            File.WriteAllText(template, "community edits");
            LanguagePacks.WriteContributorFiles(folder, english);
            Assert(File.ReadAllText(template) == "community edits", "Modèle existant écrasé.");
            var result = LanguagePacks.Discover(folder, english, []);
            Assert(result.Packs.Count == 0 && result.Issues.Count == 0, "Référence ou modèle chargé comme une langue.");
        });
        check("Langues : variantes brésilienne et chinoise, cultures et correspondances PZ", () =>
        {
            foreach (var (code, culture, game) in new[] { ("PT-br", "pt-BR", "PTBR"), ("DE", "de-DE", "DE"), ("ZH-cn", "zh-CN", "CN") })
            {
                L.SetLanguage(code);
                Assert(L.Culture.Name == culture && L.Current.GameLanguage == game, "Mauvaise variante : " + code);
                if (game == "CN")
                {
                    using var font = Theme.Heading();
                    Assert(font.FontFamily.Name == "Microsoft YaHei UI", "Police chinoise absente ou incorrecte.");
                }
            }
            L.SetLanguage("en");
        });
    }
}
