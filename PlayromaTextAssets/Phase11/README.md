# Playroma Text Studio Phase 11 assets

Phase 11 introduces `.playstorylet.json`, a declarative procedural-narrative asset.

A storylet contains eligibility prerequisites, optional character/location/item/faction bindings, weight and salience, once-only/cooldown behavior, state effects, and a realization recipe. Storylets do not contain arbitrary executable scripts. Predicate expressions reuse Playroma's safe Phase 6 semantic predicate language.

Project storylets live under `data/PlayromaText/storylets/`. Built-in storylets are read-only; clone them in Storylet Lab to create editable project variants.
