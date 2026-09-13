# PZLauncher Community — User guide

Version 0.21.0 · Windows x64

PZLauncher manages Project Zomboid installations, game profiles, mods, saves and dedicated servers. You need an installed copy of the game. The launcher uses that installation's Java runtime; the included executable does not require a separate .NET installation.

This release focuses on Build 42. A particular mod, Java loader or runtime pack may require a specific game build. The launcher is a community project and is not affiliated with The Indie Stone.

## Contents

- [Start playing](#guide-start-playing)
- [Installations and profiles](#guide-installations-and-profiles)
- [Game mods and SteamCMD](#guide-game-mods-and-steamcmd)
- [Java mods](#guide-java-mods)
- [Online play](#guide-online-play)
- [Dedicated servers](#guide-dedicated-servers)
- [Shared runtime packs](#guide-shared-runtime-packs)
- [Saves and world tools](#guide-saves-and-world-tools)
- [Performance settings](#guide-performance-settings)
- [Troubleshooting](#guide-troubleshooting)
- [Updates and your files](#guide-updates-and-your-files)

<a id="guide-start-playing"></a>
## Start playing

1. Keep the release files together in a writable folder and open **PZLauncher.exe**.
2. Check the game installation shown in **Settings**. If it is missing or incorrect, browse to your Project Zomboid folder.
3. Choose your **Game profile** at the top. Use **+** to create another profile when you want a separate setup.
4. In **Settings**, check whether Steam should be enabled. Leave **Automatic JVM** enabled for a starting configuration adapted to your PC.
5. Select any game mods, then click **Save selection**. Java mods have a separate setup described below.
6. Click **Play**. PZ opens normally; choose or create your world in the game.

The language selector in the sidebar changes the launcher's language. English, French, Spanish, Russian, Brazilian Portuguese, German and Simplified Chinese are available. Settings → Community language packs opens the folder for additional JSON translations and creates a template. Add a pack, restart, then select its language. Missing text uses English. See LANGUAGE-PACKS.md beside the executable. Game language and graphics settings are separate settings. Drag the window's edges or corners to resize it; its size and maximized state are remembered when it closes.

<a id="guide-installations-and-profiles"></a>
## Installations and profiles

**Settings** can detect Steam libraries on different drives, recognize GOG installations and use a folder you choose manually. Select the game folder, not a mod or Workshop folder. If a drive is disconnected, reconnect it or select an available installation.

GOG installations run without Steam. The Steam public-server browser requires a Steam installation and a running Steam client. A direct connection must use the same Steam mode as its server.

A profile keeps its own game settings, mod selection and cache location. The **Profile folder** button below the profile selector opens that cache. It contains files such as `options.ini`, `mods` and `Saves`. Check the selected profile before installing mods or editing a world.

Your initial profile may use your existing `%UserProfile%\Zomboid` folder. New profiles use separate folders. Creating a profile does not move your existing worlds into it. Configuration sharing transfers settings and mod identifiers, not the actual mod files, Java runtimes or saved worlds.

<a id="guide-game-mods-and-steamcmd"></a>
## Game mods and SteamCMD

### Select game mods

Open **Mods → Game mods**, search by name or ID, and use the source filters to show local or Workshop content. Tick the mods you want, resolve any reported dependencies, check their order, and click **Save selection**.

Double-click a mod to inspect its detected copies and the active source. A local copy can exist even when another copy has priority. Use **Scan details** when a mod is missing or unavailable; it shows scan problems and the current profile's mod sources. A valid mod folder needs its own `mod.info` in a layout supported by the installed game build.

The profile's selection applies when starting a new game setup. Existing worlds can retain their own mod list; use the world editor or the game's own world controls when changing one.

### Download or update Workshop items

1. Open **Mods → Workshop · SteamCMD** in the intended profile.
2. Enter the Workshop item IDs or supported Workshop links. Workshop IDs are numeric download identifiers; they are different from the internal mod IDs used in a server's `Mods=` list.
3. Install SteamCMD from the tab if needed. Downloads target Project Zomboid, application **108600**.
4. Start the download or update and follow its progress. If anonymous access is rejected, use **SteamCMD login** and complete the Steam sign-in/Steam Guard prompts.
5. Refresh the game-mod list, review the installed content and save your selection.

Downloads are installed in the selected profile's cache. Replaced managed copies are backed up. Finding an update does not apply it automatically. A Workshop collection is not automatically expanded into all of its items; supply the individual item IDs.

<a id="guide-java-mods"></a>
## Java mods

Game mods and Java mods are configured separately. For a player, open **Mods → Java mods**. For a dedicated server, select that server and open **Servers → Java mods**.

1. Enable **Apply Java setup**.
2. Select the loader required by the mod's author.
3. Add the JAR, or enable the game mod that contains it. Review its role and any dependency or availability messages.
4. For Leaf or ZombieBuddy, use **Download / install** to choose a GitHub release, or browse to an existing runtime. The built-in PZLauncher loader needs no separate download.
5. Click **Verify Java mods**, then start the game or server from its normal launch page.

| Choice | Use it for |
|---|---|
| PZLauncher · API 1 | Mods written for the launcher's Java 25 API. Their declared game versions and client/server role must match. |
| Leaf | Mods made for Leaf. Install/select Leaf's libraries and review the mod's declared environment. |
| ZombieBuddy | Mods made for ZombieBuddy. Enable its companion game mod when required, and complete any approval requested by ZombieBuddy. |
| no agent | Direct classpath patch JARs that do not use an agent. **Apply Java setup must still be enabled** to load the selected JARs. |

Java choices are saved separately for each profile or server. JARs bundled inside active game mods follow their owner's activation. **Role undeclared** means the launcher cannot determine whether that JAR supports a client, server or both; consult its author. An explicit role mismatch is rejected.

The server launches as a dedicated server with the selected Java setup. Selecting Leaf or ZombieBuddy does not make every mod for that loader server-compatible. Verification checks the supported metadata/runtime requirements; only the built-in API runs the separate transformation preflight. Compatibility between real mods still depends on those mods and the game build.

<a id="guide-online-play"></a>
## Online play

### Find a server

Open **Online play → Public servers** and refresh. Results appear progressively, with loading status and cancellation. Filter by name/address, version, player count, mod status or ping; click a column header to sort. Double-click a row or use **Details** to inspect it.

Public mod lists may be incomplete. An unknown mod status does not mean the server is vanilla. **Statistics** summarizes the discovered servers and shows the most visible mod IDs; the ranking is based on the available lists, not an exhaustive popularity survey.

### Save a server with its own profile

Use **Save with separate profile**, or create a favorite in **Connection / favorites**. Enter its address, game port, query port and Steam mode. Each favorite keeps a separate client profile so its mod copies can stay independent from other servers.

In **Server mods**, review the internal mod IDs and Workshop IDs. You can import the relevant lists from a server INI, copy already-installed mods, or open SteamCMD for the favorite. Check for updates before applying them. A copy that is current on Workshop can still differ from the version used by the server.

Click **Join server** to start PZ with that favorite's profile and connection address. The game handles the player account and character. Connection passwords entered in the launcher are session-only.

### Request the complete mod information

In a server's details, **Grab all mods infos** attempts a game connection using the account information you supply. It disconnects once the mod metadata is received, before loading a character or downloading mods. The server can log this attempt and may create the supplied account if registration is open. The game version must match the server; the current capture helper targets 42.20.x.

The captured list is a dated snapshot. Refresh it after server changes. It does not prove that your Java patches match the server's Java setup.

<a id="guide-dedicated-servers"></a>
## Dedicated servers

1. Open **Servers → New server**, enter a name and select the new server. It receives its own cache folder.
2. In **Configuration files**, set the server name shown to players, access/password settings, ports, player limit and sandbox options. Use the forms where available. Source-text editing remains available for options the forms cannot represent.
3. Configure the game mods and Workshop identifiers, then use **Java mods** if this server needs a Java loader or direct patches.
4. In **Launch**, choose Steam mode and memory settings. On first startup, supply the administrator password requested for the server database.
5. Start the server and wait for `SERVER STARTED` in **Server console**. Use that console for commands, saving and a clean stop. Keep the launcher open while it manages the server.

For a local session, the player and server still use two processes and two caches. In **Client profiles**, create/select a client and use `127.0.0.1` as the address; the game port is read from the server configuration. Start the server first, then click **Join**. A plain client profile created without a pack still needs the server's required mods installed and configured.

The launcher can also start a client and dedicated server that already share a cache, matching the game's standard layout. While either process is using that cache, the launcher still blocks changes to its mods, server configuration, worlds, and backups.

**Launch → Export .bat / .sh** creates a folder with Windows, Linux or both formats. Keep each script together with its `.assets` folder. BAT uses the selected Windows installation and cache. For Linux, specify the target cache and copy SH plus `.assets` into a Linux installation of the same game version. Java dependencies are included; transfer configuration, saves and traditional mods separately. Admin passwords are excluded unless explicitly selected. Follow the exported README to start the server.

For players on another machine, use the address through which they can reach the server and configure the server's network access accordingly. The launcher does not configure your router. Existing detected servers keep their original cache paths; check them before making changes.

<a id="guide-shared-runtime-packs"></a>
## Shared runtime packs

A runtime pack is a prepared set of game mods and direct Java patches supplied with `pz-runtime-pack.json`. Keep the manifest together with its supplied folders and files.

1. Choose **Servers → Create from pack** and select its manifest.
2. Open **Client profiles → Create client profile**. The launcher installs the same pack in a separate client cache and chooses the client-side patches. Server-only patches stay out of the client command, and client-only patches stay out of the server command.
3. Use **Verify compatibility** to check the local copies. **Associate profile** can link an existing profile only when its pack and settings match; it does not replace that profile's files for you.
4. Start the server, select the client and click **Join**.

A pack can require the exact game JAR, even when two installations display the same version number. Keep its files intact. A mismatch, missing file or overlapping direct patch must be resolved before launch. External loaders and Java mods added outside the pack remain separate configuration choices.

### Replace a pack or return to the previous version

Stop the server and every linked client. Choose **Manage pack → Replace** and select the replacement manifest. It must be a changed version of the same pack, compatible with the currently selected game installation.

The launcher backs up the group, prepares new server/client caches, verifies them and switches the group together. A failed preparation keeps the existing group selected. The old caches and backups remain available and consume disk space.

**Return to previous version** restores the earlier pack **and the world state saved before the replacement**. Progress made since that snapshot is not part of the restored world; the launcher first saves the current state separately. Changing the linked-client list after an update can prevent a grouped return until the group is consistent again.

These checks compare local files. This release does not negotiate the pack's Java fingerprint with a remote server during login. Successful verification is not a guarantee of multiplayer gameplay compatibility.

<a id="guide-saves-and-world-tools"></a>
## Saves and world tools

Select the correct profile, open **Saves → Worlds**, and select a world. Stop any game or server using it before editing.

- **Back up / restore:** create a world backup and restore it as a separate copy when needed.
- **Edit world:** review its mod list and the supported fields in `mods.txt`, `map_ver.bin`, `map_t.bin` and `map_sand.bin`. The editor does not expose every possible binary field.
- **Delete world:** review the selected world and confirm removal to the Windows Recycle Bin.
- **Chunk Wipe:** choose the area and data types, preview the affected files, then apply only the intended reset. A reset can remove player-built structures in the affected chunks. Use the available backup/rollback controls.
- **Database browser:** inspect supported tables and decoded fields. It is a viewer, not a general database editor.

The map viewer accepts a background image, including GIF. If included in your release, `world.gif` can be selected manually as a background. Check its scale/alignment before using it to select a wipe area; the image alone does not establish world coordinates. The map can be detached into its own window.

### Remote worlds over FTP or FTPS

1. Open **Saves → FTP / FTPS**, enter host, port, username, password and protocol. **Connect / list folder** lists files; double-click a directory to open it. Select the world directory containing `map_sand.bin` or `map`.
2. Stop the server cleanly and confirm it is stopped. FTP cannot inspect the remote process. Keep it stopped until replacements finish.
3. Clear **Include chunk files** to download only the editor files and SQLite databases. For ChunkWiper, leave it checked to include supported chunk directories; this may be a large transfer.
4. Use **Edit world** or preview/apply **Chunk Wipe** on the downloaded working copy.
5. Select **Review / send changes** and review replacements/removals. Only downloaded files that changed or were removed are affected. Newer remote changes block the upload. Remote originals remain with a `.pzlauncher-*.bak` suffix, and the local transaction report lists recovery paths.

Working copies are under `%LocalAppData%\PZLauncher\ftp-workspaces`. **Resume a copy** opens its `manifest.json`, restores the connection without the password and resumes unsent edits. Enter the password again. Local or remote active SQLite journals must be checkpointed before transfer; close database editors after editing. This FTP/FTPS workflow transfers files; it does not use SSH commands or SFTP.

<a id="guide-performance-settings"></a>
## Performance settings

**Automatic JVM** adapts the starting memory and collector settings to the PC and available RAM before launch. It leaves room for Windows, graphics/native allocations and other programs. More assigned RAM is not automatically faster.

If you need manual settings, disable automatic mode, change memory and collector settings, and save. G1 pause/deduplication settings are not applied when ZGC is selected. Dedicated servers have their own memory settings; player count, map size and mods affect their requirements.

The normal launch mode supplies its own launch arguments instead of requiring changes to the game's BAT or JSON files. The advanced argument editor and command preview are intended for users who need to reproduce a particular setup. Keep custom arguments only when you understand their purpose; memory, collector and Steam settings already have dedicated controls. There is no guaranteed FPS improvement from a preset.

<a id="guide-troubleshooting"></a>
## Troubleshooting

| Problem | What to check |
|---|---|
| Game installation unavailable | Select the actual game folder in Settings, reconnect its drive, and check that its bundled Java and game files exist. |
| A local mod is missing | Check the selected profile, source filter and `mod.info` layout; open Scan details. Double-click a detected mod to inspect duplicate copies. |
| A mod cannot be enabled | Read its availability/dependency message. Confirm its target game build and required mods. |
| Java verification fails | Check the selected loader, runtime path, declared role and game build. Keep direct patch JARs in no agent mode unless the author specifies another loader. |
| Pack mismatch | Check the selected game installation and the original pack files. Obtain a matching complete pack instead of editing its fingerprint. |
| No public servers | Start Steam, use a Steam installation, refresh, and inspect any network error. A manually entered favorite is a separate connection option. |
| Cannot join a server | Recheck its address, game port, Steam mode, game version, account/password and required mod versions. Start a local dedicated server before joining it. |
| File changes are blocked | Stop the game/server using that cache. Refresh after external edits so the launcher does not overwrite newer content. |
| Game exits or behaves unexpectedly | Open Diagnostics, select the relevant log and click a problem marker to read the original lines. The dedicated server also has its own console. |

When reporting a problem, include the launcher version, game version, selected loader, the operation you performed and the relevant diagnostic lines. Review logs before sharing them: they can contain local paths, server addresses or account information. A mod mentioned in a trace is not by itself proof that it caused the failure.

<a id="guide-updates-and-your-files"></a>
## Updates and your files

Close the launcher before replacing it with a newer release. The launcher does not currently update its own executable automatically. Installing a launcher update is separate from updating the game, a loader or a mod pack.

Launcher settings are normally stored under `%LocalAppData%\PZLauncher`; game settings, mods and saves live in each profile/server cache. Use the folder buttons in the interface to locate them. Copying a new executable over the old one does not copy or relocate those caches. Include both launcher settings and the required caches when backing up or moving your complete setup.

For credits, project links and the official Project Zomboid Discord link, open **Community / About**. The launcher repository link is reserved until a public project URL is configured.
