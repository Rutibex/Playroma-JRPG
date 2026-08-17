# 0004 — Store enhanced metadata in a sidecar directory

- Status: Accepted
- Date: 2026-07-19

## Context

Playroma needs prefabs, workspace state, generator recipes, localization, bookmarks, and history that do not belong in standard MV files.

## Decision

Store Playroma-only information under `.playroma/`. Strict MV Mode never injects private fields into standard data files.

## Consequences

Enhanced projects can still be opened by MV-compatible tools. Sidecar loss must not make the compiled MV project unreadable.
