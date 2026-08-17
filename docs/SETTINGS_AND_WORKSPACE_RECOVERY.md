# Settings and Workspace Recovery

## Settings

Application settings use the current schema and retain per-window placements, editor-specific layout values, accessibility/UI preferences, and other application options.

`JsonSettingsService` reads existing settings through `EditorSettingsMigrator`. Older supported schemas are upgraded conservatively, modern values win over legacy equivalents, invalid JSON is moved aside, and rewritten settings use atomic replacement.

## Workspace recovery

Recovery manifests store the authoritative project workspace snapshot used to restore supported windows, navigation state, map view, and active editing context after an interrupted session.

Older recovery manifests remain readable where migration support exists. Unsupported legacy requests are skipped and logged rather than preventing the project from opening.

## Separation of concerns

- **Project mutation recovery** preserves modified documents and staged files.
- **Workspace state** preserves windows, navigation, map view, and active editor state.
- **Private editing-session recovery** preserves supported unapplied Event, Plugin, and Raw Data sessions.

Use `--safe-mode` when you want to start without automatically applying recovered work, and `--reset-layout` when saved window placement is unusable.
