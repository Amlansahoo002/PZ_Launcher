# Contributing to PZLauncher Community

Thank you for helping improve PZLauncher Community.

## Before opening a change

- Keep each change focused and explain the user-facing result.
- Do not commit build output, local configuration, logs, credentials or Project Zomboid files.
- Preserve existing copyright, license, attribution and imported-code provenance notices.
- Document any newly introduced third-party code or asset in `THIRD_PARTY_NOTICES.md`.
- Add or update verification coverage when behavior changes.

## Build and verify

Requirements and the normal build workflow are documented in `README.md`.

```powershell
dotnet build PZLauncher/PZLauncher/PZLauncher.csproj -c Release
./build.ps1 -JavaHome 'C:\Path\To\jdk-25' -Verify
```

Network probes and real client/server smoke tests remain opt-in. Review their parameters before running them.

## Contribution license

By submitting a contribution, you agree that it may be distributed under the GNU General Public License v3.0 only (`GPL-3.0-only`). Contributions must be your own work or must be compatible with that license and accompanied by the required source and attribution information.
