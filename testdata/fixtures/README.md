# Synthetic MV Fixture Corpus

Every file in this directory is independently authored for Playroma jRPG.
No records, maps, text, media, or plugin code were copied from a real game.

Regenerate with `eng/generate-fixtures.ps1` or `eng/generate-fixtures.sh`.
CI compares semantic JSON hashes and raw hashes for non-JSON files against
`fixture-manifest.json`.

The `malformed` directory intentionally contains invalid or incomplete data.
Each case declares expected diagnostics and must not be treated as a normal
loadable project.

## Phase 1 corpus

`phase1/` contains independently authored source, `www`, browser, markerless, incomplete-runtime, encrypted-sentinel, Unicode-path, multiple-candidate, plugin-heavy, large-map, unsupported-MZ, and unsafe-plugin-path layouts. No proprietary RPG Maker content is present.

## Phase 2 fixtures

`phase2/` contains independently authored source-preservation fixtures:

- lexical number, escape, BOM, CRLF, and duplicate-property cases;
- plugin-owned unknown object data;
- a wrapped and duplicated `plugins.js` manifest;
- four-space pretty JSON;
- compact six-layer map data.
