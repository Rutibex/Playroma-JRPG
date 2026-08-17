# Lossless JSON Model

## Why a syntax model is required

A normal object serializer preserves values it understands, but it is free to
change property order, spacing, newline style, escaping, number spelling, and
unknown extension fields. RPG Maker MV projects frequently contain plugin-owned
properties and JavaScript text inside JSON strings. Playroma therefore treats
the original UTF-8 file as the authoritative source.

## Syntax nodes

Every value has an original byte `TextSpan`. Objects retain an ordered list of
`JsonPropertySyntax` nodes and arrays retain an ordered list of element nodes.
A property name is decoded for lookup, but its original quoted token remains in
the source. Number nodes retain raw text such as `1.0` or `1e3`.

Duplicate properties are preserved. Typed lookup selects the final occurrence,
matching common JSON consumer behavior, while the source model still retains
all occurrences.

## Edit overlay

`LosslessJsonDocument` does not mutate its source bytes. It records
`JsonTextEdit` replacements over original spans. Output generation streams the
original bytes and substitutes only the planned edits.

Overlapping edits are rejected. A caller that performs a structural edit and
then needs to edit inside the generated structure must generate and reopen the
document, preventing ambiguous span ownership.

## Formatting profile

The parser detects BOM, newline convention, indentation style, indentation
width, trailing newline, and colon spacing. Newly inserted properties and array
elements follow the containing document's profile.

## Limitations

JSON comments and trailing commas are not valid MV JSON and remain invalid
read-only documents. `plugins.js` is handled separately because it contains a
JavaScript assignment around a JSON-compatible array.
