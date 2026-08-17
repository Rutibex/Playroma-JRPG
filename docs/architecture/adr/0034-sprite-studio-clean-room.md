# ADR-0014: Sprite Studio clean-room implementation

- Status: Accepted
- Date: 2026-08-05

## Context

Playroma needs mature pixel-art and animation workflows, but Aseprite redistribution and source reuse are incompatible with the intended independently distributable Playroma implementation.

## Decision

Implement Playroma Sprite Studio from original C# code using public user-facing documentation and official RPG Maker MV format documentation. Do not embed, translate, vendor, or redistribute Aseprite code, binaries, icons, themes, templates, or resources. Enforce the boundary with documentation, review records, synthetic fixtures, and `CleanRoomAudit`.

## Consequences

Development requires more original engineering and behavioural tests. Playroma retains a clear MIT-owned codebase, can design an MV-native workflow, and avoids a runtime dependency on Aseprite.
