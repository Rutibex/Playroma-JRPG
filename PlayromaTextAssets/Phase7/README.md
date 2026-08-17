# Playroma Text Studio Phase 7 assets

Phase 7 is primarily a host-integration release rather than a generator-content release.

`example-project-context.playtext.json` demonstrates the read-only project values Playroma jRPG now injects into every generation context. Copy the example into a project's `data/PlayromaText/recipes/` folder to inspect the resulting semantic features in **Why this result?**.

The exact database/map/plugin context is supplied by the currently open RPG Maker MV project. Text Studio does not write to MV database files through this bridge; writable target adapters arrive in Phase 8.
