# Playroma Sprite Resilience Formats

## Preferences

Schema identifier: `playroma-sprite-preferences/1`

Preferences are user-local and must not be placed in the RPG Maker MV project. Unknown fields are retained by compatible JSON tooling but may be ignored by the editor. Numeric values are repaired to documented safe ranges on load.

## Workspace

Schema identifier: `playroma-sprite-workspace/1`

Workspace IDs are lowercase filename-safe identifiers. Panel records express intent rather than a WinForms implementation detail. Hosts may ignore unsupported dock modes while retaining the records.

## Recovery manifest

Schema identifier: `playroma-sprite-recovery/1`

A recovery manifest refers only to a package file in the same document-specific directory. `packageFileName` must not contain separators. `sha256` is lowercase hexadecimal SHA-256 calculated after the atomic package write has completed.

A host must verify the checksum before loading. A host must reject paths escaping the document directory. A host may delete invalid or orphan manifests after presenting diagnostics.

## Session lease

Schema identifier: `playroma-sprite-session/1`

The active-session record is ephemeral and contains process identity, start time, and clean-exit state. Its existence with `cleanExit: false` indicates that recovery should be considered. It is not a lock that prevents another instance from opening.

## Compatibility policy

New required properties require a new schema identifier. Additive optional properties may be introduced within version 1. Runtime PNG and `.playromasprite` formats are independent of these sidecars.
