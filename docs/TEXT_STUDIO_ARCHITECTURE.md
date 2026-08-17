# Playroma Text Studio Architecture

## Dependency direction

```text
Playroma.Text.Core
   ↑        ↑
   │        └── Playroma.Text.Authoring
   ├─────────── Playroma.Text.Generators
   └─────────── Playroma.Text.RpgMakerMV

Playroma.Text.Editor.WinForms
   └── consumes the core/authoring/generator libraries

Playroma.JRPG.Editor.WinForms
   └── hosts Text Studio and bridges imports to the active project transaction model
```

Core Text Studio libraries do not depend on WinForms. Generator logic is separated from RPG Maker MV adapters so procedural techniques remain reusable and testable.

## Execution contract

A `TextRecipe` contains ordered generation stages. `TextGenerationEngine` validates the recipe, establishes a deterministic root random stream and generation budget, forks stable random streams for stages, executes registered stage implementations, records diagnostics/trace events, and attaches provenance to generated bundles.

This permits pipelines that combine templates, names, Tracery-compatible grammars, Markov models, candidate search/ranking, dialogue/voice realization, storylets, quest planning, and formatting without coupling the orchestration engine to one technique.

## Determinism

Text Studio uses a deterministic in-tree random generator with stable stage forking. A stage can evolve internally without unnecessarily perturbing unrelated sibling stages, which is important for reproducible batch generation and testing.

## Persistence

Authoritative project-side Text Studio assets live under:

```text
data/PlayromaText/
```

Derived caches live under:

```text
.playroma/cache/text/
```

Caches are disposable and may be regenerated.

## RPG Maker MV integration

Text targets identify a destination without mutating it. MV adapters convert generated bundles into declarative import changes, and the Playroma host maps those changes into the same transaction/undo system used by the rest of the editor.

## Safety budgets

Generation contexts enforce limits for stages, trace growth, output size, candidate attempts, and elapsed work. Grammar/search subsystems add their own recursion or exploration limits while consuming the shared budget.

## Optional AI providers

The optional AI boundary is provider-free by default. It can invoke a user-selected external process through a constrained stdin/stdout integration contract. Playroma does not bundle models, provider executables, API keys, or cloud SDKs. Project context is only supplied when a recipe explicitly opts into it.
