# Playroma Sprite synthetic fixtures

All fixtures in this directory are independently authored and deliberately contain no Aseprite source, Aseprite resources, RPG Maker RTP artwork, commercial graphics, or private project content. Binary golden images must be generated from Playroma code and registered in `provenance/ASSET_PROVENANCE.json` before they are committed.

## Canonical Phase 0 fixtures

The `images/` tree contains 15 independently generated PNGs that exercise the strict MV profile rules. `documents/synthetic-empty.playromasprite` is a real ZIP-based native package containing `document.json`, `manifest.json`, and a synthetic RGBA pixel payload. Every binary fixture has a SHA-256 entry in `manifest.json`; none contains RPG Maker RTP or Aseprite artwork.
