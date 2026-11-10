# Prospect Renamer

Prospect Renamer is an unofficial Windows desktop utility for renaming and duplicating **Icarus** prospect saves. Rename also updates matching local loadout references so they follow the renamed prospect.

The application is fully local. It has no accounts, network calls, telemetry, advertising, or updater.

## Features

- Native .NET 8 WPF interface for Windows 10/11 x64.
- Automatically discovers every `Prospects` directory below `%LOCALAPPDATA%\Icarus\Saved\PlayerData\*`, including an obvious `DedicatedServer` account.
- Supports multiple Steam/account folders at once and labels each prospect by account.
- Accepts a manually selected `Prospects` folder or a nearby parent folder, and remembers that choice.
- Reads the uncompressed prospect header for a fast list, isolates malformed entries, and always uses the proven `IcarusSaveLib`/`UeSaveGame` implementation before writing any rename.
- Matches the game's persistent prospect list by hiding mission/operation instance files whose `FactionMissionDTKey` identifies a specific mission; no save names or GUID patterns are hardcoded and no files are deleted.
- Validates Windows file names, reserved device names, Unicode names, duplicates, and trailing invalid forms.
- Creates timestamped backups below `%LOCALAPPDATA%\ProspectRenamer\Backups` by default.
- Offers explicit **Rename** (remove the old local file and migrate matching local loadout references) and **Duplicate** (keep both while leaving loadouts on the original) operations.
- Hash-verifies and archives the old prospect's Icarus `.json.backup[_N]` autosaves outside the live folder during Rename, preventing Icarus from recreating the removed old prospect from its newest local backup.
- Writes to a non-JSON transaction file, reloads it for verification, finalizes the new `.json`, and removes the original only after all earlier steps succeed.
- Shows the workshop/loadout warning on first rename and detects a running Icarus process without blocking intentional main-menu editing.
- Shows live red/green Icarus process status, the installed app version, and a responsive terminal-style progress overlay during save transactions.
- Uses a themed completion panel with a short generated orbital chime instead of the standard Windows notification box and sound.
- Keeps the entire prospect table read-only; all changes must go through the explicit Rename / Duplicate workflow and its safety checks.

## Build

Requirements:

- Windows 10 or Windows 11 x64
- .NET 8 SDK from the **8.0.4xx** feature band, as specified in global.json

From PowerShell in the repository root:

```powershell
dotnet restore ProspectRenamer.sln
dotnet build ProspectRenamer.sln --configuration Release
dotnet test ProspectRenamer.Tests\ProspectRenamer.Tests.csproj --configuration Release
```

Open `ProspectRenamer.sln` in Visual Studio if you prefer the IDE.

## Build the release executable

From the repository root, run the following command:

```text
.\publish.bat
```

The script creates a self-contained Windows x64 release at:

```text
publish\ProspectRenamer.exe
```

The self-contained build does not require the end user to install .NET. License and third-party notice files are copied alongside the executable. For release review, check out the tag matching the file's version before building. To package a fresh build, start with a clean checkout.

Equivalent command:

```powershell
dotnet publish .\ProspectRenamer.App\ProspectRenamer.App.csproj -c Release -r win-x64 --self-contained true -o .\publish -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
```

## How renaming is kept safe

The implementation follows the relevant upstream rename mechanism rather than editing JSON text:

1. Fully load and parse the original prospect.
2. Confirm the internal `ProspectInfo.ProspectID` is available.
3. Copy the untouched original to the dedicated backup tree.
4. Update both the JSON-facing `FProspectInfo.ProspectID` and the `StrProperty` inside the compressed Unreal property blob.
5. Serialize to a uniquely named `.tmp` file in the live folder.
6. Reopen that file through the same parser and verify its internal ID.
7. Move it into place as `<new name>.json`.
8. Delete the old file. If deletion fails, remove the new file again so the original remains authoritative.

For Rename only, the application migrates exact `AssociatedProspect.ProspectID` matches in this account's `Loadout\Loadouts.json`. It creates a separate untouched backup, writes and parses a temporary copy, verifies structurally that no other JSON values changed, and atomically replaces the live file. If the prospect transaction fails, the loadout file is rolled back from its verified backup. Duplicate never changes loadouts. Other players' local loadout files are not available to this application, so they must exit/rejoin or migrate their own references.

Steam Cloud can restore an older save at the next game launch. Follow the upstream workflow: either temporarily disable Steam Cloud for Icarus, or start Icarus first, wait until the main menu and Cloud synchronization are fully ready, then modify the prospect while it is not loaded. The next normal shutdown can then upload the local file state.

## Local data

- Preferences: `%LOCALAPPDATA%\ProspectRenamer\settings.json`
- Default backups: `%LOCALAPPDATA%\ProspectRenamer\Backups`

Prospect backups are stored below the account/prospect name. `Loadouts.json` backups are stored separately below `<account>\AccountFiles\Loadouts`.
Icarus autosave sidecars removed from the live folder by Rename are retained below `<account>\<prospect>\GameBackups`.

The backup folder can be changed in the small Settings dialog.

## Project layout

- `ProspectRenamer.App` — WPF views, dialogs, commands, and view models.
- `ProspectRenamer.Core` — discovery, validation, codec adapter, backup, settings, and rename transaction.
- `ProspectRenamer.Tests` — temporary-directory tests; never targets real Icarus saves.
- `third_party/IcarusSaveLib` — pinned Apache-2.0 Icarus prospect serializer source.
- `third_party/UeSaveGame` — pinned Apache-2.0 Unreal property serializer source.

The application icon is an original project asset generated for Prospect Renamer; it does not use official game art or branding.

## Attribution and disclaimer

The save format work is based on Crystal Ferrai's [EditIcarusProspect](https://github.com/CrystalFerrai/EditIcarusProspect), [IcarusSaveLib](https://github.com/CrystalFerrai/IcarusSaveLib), and [UeSaveGame](https://github.com/CrystalFerrai/UeSaveGame). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the vendored license files.

Prospect Renamer is unofficial and is not affiliated with or endorsed by RocketWerkz. Icarus is a trademark of its respective owner. No official logos, artwork, screenshots, or UI assets are included.

Always keep independent backups of important saves. Game updates may change the save format.

## License

Original Prospect Renamer code is available under the MIT License. Vendored components retain their Apache License 2.0 terms; see the third-party notices.
