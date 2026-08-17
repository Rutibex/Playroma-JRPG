# `.playromasprite` Native Package Specification — Version 1

A `.playromasprite` file is a ZIP package. Version 1 contains:

```text
document.json
manifest.json
pixels/<pixel-content-id>.rgba
layers/        reserved
cels/          reserved
palettes/      reserved
thumbnails/    reserved
references/    reserved
recovery/      reserved
```

The package serializer writes `document.json`, `manifest.json`, and one raw RGBA32 payload per pixel-content descriptor. The descriptor records dimensions and the deterministic package entry name. Layer, frame, cel, animation-tag, slice, palette, and tileset identities retain their stable meanings.

## Compatibility rules

- `schemaVersion` is mandatory.
- Readers must reject newer unsupported schema versions rather than overwrite them.
- UUIDs identify documents, layers, frames, cels, tags, and slices.
- Linked cels share one pixel-content UUID.
- Runtime PNG exports are derivatives; they are not the editable master.
- Writers use temporary files and atomic replacement.
- Schema migrations must preserve a backup and have deterministic tests.
