# Project Classification

Layout and editability are deliberately separate.

## Layouts

- `SourceRoot` — data and optional runtime are directly beneath the project root.
- `WwwSubdirectory` — an editable MV marker and data are beneath `www`.
- `BrowserProject` — readable data and browser runtime without an MV project marker.
- `ReadableDeployment` — readable `www` data inside a deployment wrapper.
- `IncompleteProject` — partial MV evidence that remains useful for inspection.
- `UnsupportedMzProject` — an RPG Maker MZ project recognized but not loaded as MV.

## Editability

- `FullyEditableSource` — strong source evidence including an MV marker.
- `ProbablyEditable` — complete readable MV data without an authoritative source marker.
- `ReadOnlyInspection` — incomplete or uncertain source material.
- `UnsupportedEncryptedDeployment` — readable deployment data accompanied by encrypted assets and no source marker.
- `Invalid` — recognized but unsupported or not a valid MV candidate.

Classification does not grant legal permission to edit or redistribute a project. It reports technical evidence only.
