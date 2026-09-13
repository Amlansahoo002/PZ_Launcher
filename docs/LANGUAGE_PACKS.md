# Community language packs

PZLauncher 0.21 includes English, French, Spanish, Russian, Brazilian Portuguese,
German and Simplified Chinese. Additional languages can be installed as JSON files
without changing the program or rebuilding the executable.

## Install a language

1. Open **Settings → Community language packs → Open language folder**.
2. Put the pack's UTF-8 `.json` file in this folder, normally
   `%LocalAppData%\PZLauncher\languages`.
3. Restart the launcher and select the language in the sidebar.

Missing translations use English. Remove the JSON file and restart to uninstall
a language. If it was selected, the launcher switches to English. Profiles, saves
and the game's language preference are independent of this choice. Install packs
in the user folder, not beside the executable: release updates then leave them intact.
When `PZLAUNCHER_DATA` is set, the folder is `<PZLAUNCHER_DATA>\languages` instead.

## Create and share a pack

The folder button also creates `template.json.example`, a versioned English
reference and a short README, without replacing existing files. Copy the template
to `it.json`, for example, then translate keys from the reference:

```json
{
  "schemaVersion": 1,
  "code": "it",
  "name": "Italiano",
  "culture": "it-IT",
  "gameLanguage": "IT",
  "translations": {
    "nav.play": "Gioca",
    "nav.settings": "Impostazioni",
    "home.modsCount": "{0} mod installate"
  }
}
```

- `code`: unique BCP 47 style tag; comparisons ignore case. Built-in codes
  `en`, `fr`, `es`, `ru`, `pt-BR`, `de`, `zh-CN` are reserved. A variant may use a
  distinct tag, such as `fr-CA`. If two files use one code, the first by filename wins.
- `name`: language name in its own language, shown in the selector.
- `culture`: .NET culture for displayed dates and numbers, such as `it-IT`.
- `gameLanguage`: optional Project Zomboid translation folder used for option names
  and help; defaults to `EN`. Examples: `IT`, `DE`, `PTBR`, `CN` (Simplified Chinese),
  `CH` (Traditional Chinese). Missing game translations use the available English
  text or technical key. This does not change the game's language setting.
- `translations`: any nonempty subset of the English keys. Unknown keys are
  ignored, allowing a pack made for a newer release to load on an older one.

Keep keys, `{0}` / `{1}` placeholders and their format specifiers, `\n` line breaks,
file extensions and command switches intact. Placeholder order may change for
natural phrasing. Use concise labels and consistent terms across pages. A partial
translation is allowed; do not fill untranslated entries with empty strings.

Only `.json` files directly in the language folder are read. `.example` files are
references. Packs must use format version 1 and stay under 2 MB. Invalid JSON,
metadata, placeholders or duplicate keys cause that pack to be skipped. Errors
appear in **Settings → Community language packs** and in `launcher.log`; other
languages remain available. No scripts, executable content, downloaded fonts or
network resources are loaded from a pack.

Review each page, tab and dialog at the minimum window size before sharing the
JSON. The launcher uses installed Windows fonts, with CJK font families for Chinese,
Japanese and Korean. Full right-to-left layout is not implemented. Community
translations are supplied by their authors; bundled language coverage checks do
not certify the linguistic quality or layout of an external pack.

## Maintain bundled translations

The complete catalogs live in `PZLauncher/PZLauncher/Resources/Languages/`.
Add new UI keys to every bundled catalog. The embedded verification command checks
key coverage, empty strings and numbered placeholders. `--render-ui=ABSOLUTE_PATH`
exports pages and dialogs in all discovered languages and reports layout issues;
inspect the images too. To share a new language, distribute its JSON first. It can
later be included as a bundled catalog without changing the pack format.
