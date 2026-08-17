# Project Report Schema

The CLI JSON format is versioned independently of assembly versions.

## Probe

`playroma-jrpg probe <path> --format json` emits `playroma-jrpg-project-probe/1` with the selected path and candidate array.

## Inspection

`playroma-jrpg inspect <path> --format json` emits `playroma-jrpg-project-report/1` with:

- `generatedAtUtc`;
- immutable project identity and layout;
- editability classification;
- file inventory and fingerprints;
- database summaries;
- map descriptors;
- plugin entries in load order;
- runtime fingerprint and capability heuristics;
- asset inventory;
- diagnostics.

Consumers must inspect the `schema` value before deserializing. New optional properties may be added within a schema version. Breaking field changes require a new schema number.
