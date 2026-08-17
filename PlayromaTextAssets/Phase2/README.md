# Playroma Text Studio Phase 2 Assets

Phase 2 introduces `.playlang.json` culture/language profiles and cultural naming recipes.

Built-in profiles: `common`, `valestrian`, `northreach`, `sylvan`, `kharic`, and `umbral`.
Recipes may use a fixed `profile`, while `GenerationContext["culture"]` overrides that profile at runtime. This is how one recipe can generate a coherent set of people, places, monsters, artifacts, and languages for a particular RPG region.

Use `BuiltInLanguageProfileExporter.ExportPhase2Async()` and `BuiltInRecipeExporter.ExportPhase2Async()` to emit the complete editable built-in catalogs.
