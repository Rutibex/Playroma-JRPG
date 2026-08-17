# Playroma Music Style-Pack Authoring

Style packs are JSON documents with the `.style.json` suffix. They describe reusable musical constraints and generation preferences for Playroma Music.

## Locations

Playroma searches built-in resources and user-facing `StylePacks` folders. Repository examples live under `StylePacks/`.

## Core fields

A style document uses `schemaVersion`, `id`, `name`, `description`, `version`, `family`, and `abstract`. Most concrete styles also use `extends` to inherit from a shared foundation or another style.

Common optional sections include:

- `tempo` — minimum/maximum tempo bounds;
- `meter` — beats per bar and beat unit;
- `candidateCount` — number of generated candidates to consider;
- `scales` — allowed scale/mode identifiers;
- `forms` — form grammar, productions, and named section tokens;
- `harmony` — harmonic grammar and progression vocabularies;
- `rhythms` — rhythm definitions or preferences;
- `roles` — musical-role definitions and instrument-selection behavior;
- `constraints` — generation constraints;
- `scoring` — candidate scoring preferences;
- `mixins` — reusable style fragments.

## Inheritance

Use `extends` when a style changes only part of an existing definition. Keep base/foundation styles `abstract: true` when they should not be selected directly.

A concrete style should override only the data needed to establish its identity. This keeps the library maintainable and lets improvements to shared foundations flow into derived styles.

## IDs and versions

- Keep `id` stable once a style has been published.
- Use lowercase, descriptive IDs consistent with the existing library.
- Increment `version` when behavior changes in a way authors may need to track.
- Give each style a human-readable `name` and useful `description`.

## Validation workflow

Use the Style-Pack Workbench in Playroma Music to load, inspect, apply, hybridize, and reload style packs. Invalid documents should be corrected before use or distribution.

When contributing a style to the repository, validate the JSON, confirm inherited IDs exist, and test generation with several deterministic seeds rather than judging only one output.

## Minimal example

```json
{
  "schemaVersion": 1,
  "id": "example-adventure",
  "name": "Example Adventure",
  "description": "A compact adventurous style derived from the shared JRPG foundation.",
  "version": "1.0.0",
  "family": "jrpg",
  "abstract": false,
  "extends": "jrpg-foundation",
  "tempo": {
    "minimum": 96,
    "maximum": 132
  },
  "scales": ["major", "mixolydian"]
}
```

Use an existing pack with similar behavior as the best structural reference; the built-in library demonstrates more advanced forms, harmony, roles, constraints, scoring, and mixins.
