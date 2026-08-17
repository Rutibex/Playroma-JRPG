# Compatibility Charter

This charter defines the non-negotiable behavior of Playroma jRPG.

## 1. Source-project compatibility

Playroma targets editable RPG Maker MV source projects and deployed-style
folders that still contain complete editable source data. It does not promise
to decrypt, decompile, or reconstruct protected commercial deployments.

## 2. Existing runtimes are preserved

Opening a project must not replace its CoreScript, JavaScript libraries, NW.js
wrapper, `index.html`, `package.json`, or plugin files. Runtime upgrades are
explicit migration operations, never incidental editor behavior.

## 3. Unknown data is valid data

Unknown JSON properties, custom data files, note tags, script strings, plugin
parameters, event codes, duplicate plugin entries, null slots, and unusual but
valid values are preserved unless the user explicitly changes or removes them.

## 4. Untouched files remain untouched

An unmodified file is copied byte-for-byte. A modified document changes only
the nodes required by the user's operation, subject to unavoidable JSON
serialization boundaries documented by the save transaction.

## 5. No silent repair

Validation reports problems. It does not silently normalize, renumber,
upgrade, reorder, delete, or rewrite project content. A proposed repair must be
previewable, explicit, transactional, and undoable.

## 6. Strict MV Mode

Strict MV Mode writes only ordinary MV-compatible project data. Playroma-only
metadata is stored under `.playroma/` and may not be injected into standard MV
JSON files.

## 7. Enhanced features compile down

Playroma Enhanced Mode may author prefabs, graphs, generators, localization,
and other higher-level constructs, but export must compile them into standard
MV data, assets, plugins, or note tags without requiring the proprietary RPG
Maker editor.

## 8. Project code is untrusted

Loading and inspecting a project must not execute JavaScript. Playtesting is an
explicit action with clear security boundaries.

## 9. Proprietary content is not distributed

The editor repository and official starter projects may contain only
Playroma-owned or clearly redistributable material with recorded provenance.

## 10. Compatibility claims require tests

Every compatibility claim must have a synthetic fixture, a deterministic test,
or a documented external verification procedure. Anecdotal success is not a
substitute for a regression test.
