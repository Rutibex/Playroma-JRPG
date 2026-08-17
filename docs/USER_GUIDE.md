# Playroma jRPG User Guide

## Project Hub

The Project Hub is the entry point for opening projects, recent-project access, recovery workflows, and application preferences. Playroma is single-instance by default to reduce conflicting edits to the same project.

## Map Editor

The Map Editor is the primary workspace for an open project. It combines map hierarchy/navigation, tileset tools, a map canvas, editing commands, project-level tools, and shortcuts to the rest of the editor.

Map editing is backed by Playroma's project transaction model rather than direct ad-hoc writes. Use the normal save command to persist shared project changes.

## Event Editor

Map events, common events, and troop event pages use the shared event-authoring infrastructure. Supported workflows include:

- event page properties and conditions;
- event command insertion, editing, deletion, copy/paste, and reordering;
- branches, loops, choices, battle processing, messages, comments, scripts, shops, and movement routes;
- searchable command selection;
- typed editing where Playroma understands the command shape;
- raw/unknown command preservation when a command or plugin extension is not fully modeled.

Unknown command data should be preserved rather than discarded merely because Playroma cannot interpret it.

## Database

The Database window provides category-specific editors over RPG Maker MV database files. The codebase includes editors and supporting models for actors, classes, skills, items, weapons, armor, states, enemies, troops, animations, tilesets, common events, System, Types, and Terms.

Important behavior:

- database IDs remain stable;
- deleting a numbered MV record uses a null slot instead of shifting later IDs;
- reference-aware operations can report affected records;
- notes and formulas remain editable text and are not executed simply by browsing the database;
- unknown fields are retained by the raw-backed document model.

## Plugin Tags

Plugin Tags adds structured authoring on top of plugin note-tag conventions. The Yanfly compatibility layer is clean-room metadata tooling for plugins supplied by the user; the repository does not redistribute Yanfly plugin JavaScript.

Specialized editors exist for areas such as battle AI/action sequences, skill progression, item/equipment/commerce behavior, enemy/state/formula features, and map/event-related plugin authoring.

Always test plugin-driven behavior in the actual game runtime because plugin load order and third-party code remain authoritative.

## Resource Manager

Use Resource Manager for project assets and asset-related operations instead of manually copying files while an edit session is active. Asset preview and cataloguing support covers common RPG Maker MV image/audio categories and is designed to avoid executing project JavaScript.

## Validation and References

The Validation window aggregates diagnostics for supported project structures. Reference tools can inspect exact and heuristic links between database records and other project content.

Validation is advisory unless an operation explicitly blocks unsafe output. A clean validation report does not guarantee that arbitrary third-party plugins will behave correctly at runtime.

## Raw Data

Raw JSON views exist for inspection and controlled advanced editing. Prefer purpose-built editors when available; raw editing bypasses some higher-level affordances and therefore deserves extra review before saving.

## Integrated Studios

Playroma includes Text, Sprite, Tile, Sound, and Music tools. They can be used as standalone authoring surfaces and, where implemented, can target the active RPG Maker MV project. See [STUDIOS.md](STUDIOS.md).

## Saving and recovery

Playroma's save system is designed around:

- one shared project session;
- transaction journals and recovery information;
- external file modification checks;
- backups/rollback support for operations that require it;
- post-save verification in supported paths.

Do not edit the same project concurrently in multiple tools unless you understand how those tools write files. Version control remains strongly recommended.

## Settings, layout, and safe mode

Use `--reset-layout` if saved window placements become unusable. Use `--safe-mode` when you want default layout behavior and do not want recovered work automatically applied during startup.

## Keyboard shortcuts

See [KEYBOARD_SHORTCUTS.md](KEYBOARD_SHORTCUTS.md). The Map Editor also exposes its current map-tool shortcut summary from its Help menu.
