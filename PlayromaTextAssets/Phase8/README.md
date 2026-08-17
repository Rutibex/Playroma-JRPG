# Playroma Text Studio Phase 8 — RPG Maker MV Database Integration

Phase 8 connects generated text to the live Playroma RPG Maker MV database while preserving Playroma's transaction/undo architecture.

## Launch points

- Database toolbar: **✨ Generate Text…**
- Compatible generic text fields: **✨** button beside the field
- Notes: **✨ Generate Note…**
- System → Types arrays: **✨ Generate…**
- System → Terms: **✨ Generate…**

The selected record/field becomes a read-only Text Studio generation target. No RPG Maker MV JSON is changed until the user chooses **✨ Import…**, reviews the field-by-field diff, and accepts it.

## Supported standard database areas

Actors, Classes, Skills, Items, Weapons, Armors, Enemies, Troops, States, Animations, Tilesets, Common Events, plus System Types and System Terms.

## Multi-field bundles

Phase 8 ships complete text recipes whose generated field names match RPG Maker MV text semantics. For example `phase8.mv.skill-text` emits `name`, `description`, `message1`, and `message2`; the import preview can apply any subset in one undoable transaction.

## Safety

- Existing plugin-style Note tags block whole-field Replace by default.
- Append/Prepend/Fill-if-blank remain available for protected Notes.
- System Term imports cannot silently remove required `%1`, `%2`, etc. placeholders.
- Create New Record only accepts an existing null slot or the next append ID; populated records are never overwritten by that mode.
- Database records with unapplied local editor changes must be applied before Text Studio is launched, so generation context and import diffs cannot use stale JSON.
- All accepted changes are committed through Playroma `ProjectTransaction` / `SetJsonValueCommand` / `InsertDatabaseRecordCommand`, so Ctrl+Z undoes the generated import normally.

See `docs/TEXT_STUDIO_PHASE8_RPG_MAKER_MV_DATABASE_INTEGRATION.md` for architecture and workflow details.
