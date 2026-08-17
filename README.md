# Playroma jRPG

Playroma jRPG is an open-source Windows authoring environment for **RPG Maker MV-compatible projects**. It edits MV project data directly instead of importing projects into a proprietary format, with an emphasis on lossless round-tripping, safe project writes, and integrated content-creation tools.

> **Status:** public-source release candidate. The editor is actively developed; keep backups of important game projects and review generated changes before committing them.

## Highlights

- Multi-window RPG Maker MV project editor for maps, events, database records, system data, resources, plugins, validation, references, and raw JSON.
- Lossless JSON/document model designed to preserve unknown fields, plugin data, notes, formulas, IDs, ordering, and untouched source bytes where possible.
- Complete event-authoring foundation for map events, common events, troop events, movement routes, branches, choices, battles, scripts, shops, and unknown command preservation.
- Visual database editors for actors, classes, skills, items, equipment, states, enemies, troops, animations, tilesets, System, Types, and Terms.
- **Text Studio** for names, templates, Tracery-compatible grammars, Markov generation, voices, dialogue, storylets, quest planning, batch generation, semantic ranking, and optional external AI-provider bridges.
- **Sprite Studio** for pixel-art drawing, animation, layers, selections, RPG Maker MV asset profiles, system assets, tilemaps, slices, automation, and export pipelines.
- **Tile Studio** for MV tiles, autotiles, procedural materials, tileset construction, metadata, previews, and safe project patching.
- **Sound Studio** for procedural sound design, effects, authoring graphs, analysis, playback, and MV-oriented export.
- **Music Studio** for procedural composition, style packs, MIDI/SoundFont playback, rendering, and MV audio export.
- Plugin Tags authoring support, including clean-room metadata tooling for user-supplied Yanfly Engine Plugins.
- Command-line tools for inspection, round-trip checks, references, dependencies, validation, recovery, asset auditing, and controlled edits.

## Requirements

For the main editor:

- Windows 10 or later
- .NET 10 SDK (the repository pins `10.0.100` and permits later .NET 10 feature bands)
- An RPG Maker MV source project or readable deployed `www` layout for supported read-only operations

Visual Studio is optional. The command-line and many core libraries are platform-neutral, but the primary editor is WinForms and therefore Windows-only.

## Build

```powershell
./build.ps1
```

Or directly:

```powershell
dotnet restore Playroma.JRPG.sln
dotnet build Playroma.JRPG.sln --configuration Release
```

## Run

```powershell
dotnet run --project src/Playroma.JRPG.Editor.WinForms --configuration Release
```

Open a project directly:

```powershell
dotnet run --project src/Playroma.JRPG.Editor.WinForms --configuration Release -- "C:\Games\MyMvProject"
```

Useful startup switches:

- `--reset-layout` — discard saved window/splitter placement and use defaults.
- `--safe-mode` — start with default layout and do not automatically apply recovered work.

## Documentation

Start with the [documentation index](docs/README.md):

- [Getting Started](docs/GETTING_STARTED.md)
- [User Guide](docs/USER_GUIDE.md)
- [Integrated Studios](docs/STUDIOS.md)
- [Command-Line Interface](docs/CLI.md)
- [Building and Development](docs/BUILDING.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Keyboard Shortcuts](docs/KEYBOARD_SHORTCUTS.md)
- [Compatibility Charter](docs/COMPATIBILITY_CHARTER.md)
- [Legal and Provenance](docs/LEGAL_AND_PROVENANCE.md)

## Project-safety principles

Playroma treats the RPG Maker MV project as authoritative:

- unknown JSON and plugin-owned data should survive edits;
- database IDs are stable and list sorting does not renumber records;
- deletes use MV-compatible null slots rather than shifting later IDs;
- notes and formulas are data and are not executed merely by opening a project;
- project writes use transaction/recovery infrastructure and external-change checks;
- Playroma does not bundle RPG Maker MV RTP artwork, commercial plugins, proprietary game projects, or RPG Maker runtime binaries.

See [Compatibility Charter](docs/COMPATIBILITY_CHARTER.md) and [Security Policy](SECURITY.md) for the complete expectations.

## Repository layout

```text
src/                 Product source code
tests/               Automated and smoke-test projects
tools/               Repository, fixture, comparison, and validation utilities
docs/                Public documentation
specifications/      Format and compatibility specifications
PlayromaTextAssets/  Text Studio schemas and examples
PlayromaSpriteAssets/Sprite Studio schemas/assets
PlayromaTileAssets/  Tile Studio sample/support assets
PlayromaSoundAssets/ Sound Studio schemas/templates
MusicAssets/         Music Studio supporting assets and licences
StylePacks/           Procedural music style definitions
provenance/           Third-party/source provenance records
testdata/             Synthetic public fixtures and ignored private-corpus area
```

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting changes, especially the rules around synthetic fixtures, lossless compatibility, clean-room work, and third-party provenance.

## Licence and trademarks

Playroma-owned source code is released under the **MIT License**. See [LICENSE](LICENSE), [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), [TRADEMARKS.md](TRADEMARKS.md), and [docs/LEGAL_AND_PROVENANCE.md](docs/LEGAL_AND_PROVENANCE.md).

RPG Maker and referenced third-party products remain the property of their respective owners. Compatibility references do not imply affiliation or endorsement.
