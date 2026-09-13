# Dedicated runtime packs

The Servers page now offers **Create from a pack**. Select `pz-runtime-pack.json`, then name the server. The launcher creates an independent server cache, installs the declared traditional mods, enables their IDs and pins the manifest hash in the server preset. It does not launch the server until the existing Start action is used.

This integration targets `zombie.network.GameServer`, including a dedicated process on the player's own machine. It does not use CoopMaster or the in-game Host workflow. Player/client preparation and automatic loopback connection are separate follow-up work.

## Format 1

```json
{
  "format": 1,
  "id": "Example",
  "version": "1.0",
  "gameVersion": "42.20",
  "gameJarSha256": "<64 hex digits>",
  "mods": [{ "id": "ExampleMod", "folder": "ExampleMod" }],
  "files": [
    { "path": "java/gameplay.jar", "sha256": "<64 hex digits>", "kind": "java", "side": "both" },
    { "path": "java/render.jar", "sha256": "<64 hex digits>", "kind": "java", "side": "client" },
    { "path": "mods/ExampleMod/42/mod.info", "sha256": "<64 hex digits>", "kind": "mod", "side": "both" }
  ]
}
```

List every file, including assets and scripts. Paths are relative to the manifest folder, use `/`, and stay under `java/` or a declared `mods/<folder>/`. Traditional mod files use `both`; Java JARs can use `server`, `client` or `both`. The file-list order determines Java overlay order. Build the overlays against the pinned original game JAR.

The server classpath uses applicable overlays, `projectzomboid.jar`, then `.`. Inherited loose game classes cannot shadow the pinned game classes. JAR manifest Class-Path dependencies, multi-release classes and overlapping classes between applicable overlays are rejected; compose overlapping transformations at build time. This mechanism supports locally built class overlays and is distinct from the existing client community agent API.

Import validates the source, copies it into `<cache>/runtime-pack`, copies traditional mods into `<cache>/mods`, rechecks the copies, then creates server configuration. A failed import may leave an isolated incomplete cache but is not registered as a server. Source folders are no longer needed after successful import.

At launch, the preset pin, game hash, pack contents and installed mods are checked again. A required mod cannot silently be disabled or shadowed by another installed copy. Other mods remain configurable through the existing mod editor. The pack is included in server backups and uses paths relative to the restored cache.

Checks detect local mismatches, not remote-client cheating or all possible runtime incompatibilities. A pack is executable local code. Network version negotiation remains the mod's responsibility. No pack scripts are run during import; there are no arbitrary launch arguments in this format.

## Development verification

```powershell
PZLauncher.exe --verify-runtime-pack=E:\path\to\results
PZLauncher.exe --smoke-runtime-pack=E:\path\to\pz-runtime-pack.json --output=E:\path\to\smoke-results
```

The first command exercises creation, role filtering, corruption/overlap rejection and backup restoration using fixture files. The second starts the EpicLootZ pack on an isolated no-Steam loopback dedicated server and sends `quit` after startup. It does not test combat, rendering or a connected client. Only the process created by that smoke command can be terminated on its timeout.

The existing `--verify` suite also includes pack regression checks. `--render-ui=<directory>` renders the server controls using synthetic data.
