# RPG Maker MV Image Profile Specification

`PlayromaSpriteAssets/profiles/RpgMakerMvImageProfiles.json` is the versioned source of truth for Sprite Studio MV image layouts.

A profile declares an ID, runtime folder, category, PNG format, fixed or recommended dimensions, optional grid geometry, filename markers, template key, named motions, and metadata. Validation modes are:

- `strict`: dimensions and grid rules are enforced.
- `template`: a specialized editor supplies detailed reserved-region validation by the specialized editor.
- `bridge`: advanced work is delegated to another Playroma studio, presently Tile Studio.

Project-local plugin profiles can be layered over the built-in catalog. Built-in profile IDs are stable public API.
