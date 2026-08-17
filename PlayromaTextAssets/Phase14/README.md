# Playroma Text Studio Phase 14 assets

Phase 14 completes the original Text Studio roadmap with deterministic advanced candidate search, optional provider-based AI, migrations, release auditing, and hardening.

- `example-advanced-search.playtext.json` demonstrates seed-space quality/diversity optimization.
- `example-optional-ai.playtext.json` demonstrates the `phase14.neural-text` stage. It intentionally references a provider that is not configured by default.

## Optional AI provider protocol

Playroma does not ship a cloud dependency or API key. An enabled `process` provider is a user-selected local executable. Playroma writes one JSON `NeuralTextRequest` to stdin and reads either `{ "text": "..." }` JSON or plain text from stdout. The process is launched directly with `UseShellExecute=false`; no shell command line is constructed. Project context is excluded unless a recipe explicitly sets `includeProjectContext=true`.

## Release migration

Project Doctor can preview or apply canonical Phase 14 recipe migrations. Applying a changed recipe creates a sibling `.pre-phase14` backup before rewriting the `.playtext.json` file.
