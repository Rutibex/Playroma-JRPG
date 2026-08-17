# Validation Engine

## Diagnostic contract

A diagnostic has:

- stable code;
- severity;
- message and technical detail;
- file and JSON path;
- optional map, record, event, page, and command coordinates;
- optional documentation key;
- optional safe fix command.

Validators return data only. They never write files or directly mutate the
workspace.

## Default validators

### Loader diagnostics

Carries structural, encoding, runtime, plugin, document, and asset
findings into the unified validation result.

### Missing references

Checks exact reference targets against the current target catalogue. Missing
database records and maps are errors. Missing assets are warnings. Safe fixes
currently include clearing an absent asset reference and setting a missing
animation to None.

### Project structure

Checks:

- missing plugin script files;
- preserved duplicate plugin entries;
- plugin commands that appear to reference disabled plugins;
- player and vehicle starting map coordinates;
- map data length (`width × height × 6`).

### Event structure

Checks map events, common events, and troop events for:

- list values that are not arrays;
- negative or implausibly large indentation;
- first-command indentation other than zero;
- indentation jumps larger than one level;
- conservative autorun termination risks.

## Applying a fix

`MvProjectSession.PreviewFixAsync` returns a transaction preview.
`ApplyFixAsync` verifies that the fix is marked safe, commits it as a normal
transaction, and refreshes affected reference-index documents. The fix is then
fully undoable.

## Determinism

The engine runs validators in a stable order and sorts results by severity,
file, record, JSON path, and code. This makes CLI output and automated reports
suitable for regression testing.
