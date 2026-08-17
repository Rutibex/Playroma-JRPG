# Playroma Sprite Studio API Reference

## Preferences

`SpriteEditorPreferences` is the versioned `playroma-sprite-preferences/1` model. Load and save it through `SpritePreferencesStore`; the store repairs ranges and writes atomically. `SpriteShortcutRegistry` normalizes gestures and rejects command or scope conflicts.

## Workspaces

`SpriteWorkspaceProfile` stores window bounds, inspector side and width, timeline visibility and height, selected inspector tab, zoom, and panel metadata. `SpriteWorkspaceProfileStore` validates identifiers, clamps unsafe dimensions, and persists `*.playroma-workspace.json` files atomically.

## Recovery

`SpriteRecoveryService.CaptureAsync` writes a complete deep-cloned `.playromasprite` package and a `playroma-sprite-recovery/1` manifest. `LoadAsync` verifies SHA-256 before deserializing. `ListSnapshots`, `Prune`, `Delete`, and `DeleteAll` implement the recovery browser and retention policy.

`SpriteEditorSessionLease` records an unclean active session. The editor marks the lease clean only after background work has shut down and the window has closed normally.

## Background jobs

`ResilientBackgroundJobQueue.Enqueue` accepts an asynchronous operation with progress and cancellation. The queue supplies bounded concurrency, retry, cancellation, status, error text, timestamps, and graceful disposal. `CancelAll` supports explicit close-time cancellation.

## Performance and accessibility

`SpritePerformanceMonitor.Measure` records elapsed time and managed-memory observations. `SpriteLargeDocumentPolicy.Assess` returns threshold status and authoring recommendations. `SpriteAccessibilityAuditor` validates preferences and calculates WCAG-style contrast ratios.

## Host integration

`SpriteEditorIntegrationOptions.SettingsRoot` allows tests and alternate hosts to isolate preferences, recovery data, workspaces, and session state. Omit it to use the normal `%LocalAppData%/Playroma/jRPG/SpriteStudio` location.

## Compatibility

The public APIs are additive where practical, and the `.playromasprite` project, MV, tilemap, animation, color, scripting, extension, and CLI contracts are treated as compatibility-sensitive.
