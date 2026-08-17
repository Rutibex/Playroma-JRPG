# Style packs

Playroma Music loads built-in and user-authored `.style.json` documents from:

1. Embedded resources in `PlayromaMusic.Generators`.
2. `StylePacks` beside the application.
3. `StylePacks` in the current working directory.
4. `Documents/PlayromaMusic/StylePacks`.
5. Style documents supplied by trusted plugins.

Built-in files demonstrate inheritance, mixins, role definitions, form libraries, harmonic vocabularies, rhythms, instrument selectors, constraints, and scorers. User files can extend a built-in ID and are validated before becoming playable.

Use the Style-Pack Workbench (`F4`) to inspect, apply, hybridize, and reload packs. Use `validate-style-packs` from the CLI for automated checks. The Reference MIDI workspace can also export a corpus-derived style document into the user StylePacks folder.

See [style-pack authoring](../docs/style-pack-authoring.md) and the [complete style library](../docs/style-library.md).
