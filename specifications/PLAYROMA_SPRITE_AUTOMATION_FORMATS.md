# Playroma Sprite Automation Formats

## Versioning rule

Each automation artifact has an independent schema identifier. Unknown major versions must be rejected rather than interpreted approximately.

## `playroma-sprite-pipeline/1`

A pipeline contains a name, required capability flags, continue-on-error policy, and ordered steps. A step has a stable ID, command, optional display name, enabled state, local continue-on-error state, and free-form JSON arguments.

## `playroma-sprite-script/1`

A script adds named parameters to the pipeline step model. Parameter values are substituted into JSON string values before pipeline execution. The format is declarative and does not include loops, reflection, shell commands, or arbitrary native code.

## `playroma-sprite-export-preset/1`

An export preset stores output format, path template, frame selection, scale, trim, padding, extrusion, power-of-two preference, maximum dimensions, ordering, metadata format, and user data.

## `playroma-sprite-extension/1`

An extension manifest identifies an extension, entry assembly and type, requested capabilities, advertised commands, format providers, and metadata. Paths are relative to the manifest directory.

## `playroma-sprite-atlas/1`

Atlas metadata records the output image, source document, packing settings, and every packed frame. Each frame contains its source frame number, duration, destination rectangle, original source rectangle, and trim state.

## Atomic writes

Editable packages and preset/settings files use temporary files followed by replacement. PNG, GIF, and atlas files use their established codecs. Batch callers should treat each pipeline result independently and should not infer success from file existence alone.

## Path variables

The standard variable namespace is:

- `document`
- `width`
- `height`
- `frames`
- `workspace`
- `output`
- `date`
- `time`

Hosts may add variables. Undefined variables are errors.

## Extension compatibility

The extension SDK contract is source-compatible within major version 1. Extensions must target a framework compatible with the host and reference the same Playroma Sprite Automation contracts. The host may refuse capabilities even when a manifest requests them.
