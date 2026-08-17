# Playroma Text Studio Phase 3 — Tracery compatibility provenance

Phase 3 is an original C# implementation of the public Tracery grammar model and behavior. It was designed from the public Tracery repository/documentation and does not embed or execute tracery.js.

Reference project: https://github.com/galaxykate/tracery
Reference license: Apache License 2.0.

Compatibility targets implemented here include symbol-to-rule grammars, `#symbol#` expansion, modifier chains, bracket actions that push temporary symbol rules, scoped tag pre-actions, `POP`, classic universal modifiers, arbitrary raw-rule flattening, and traceable recursive expansion.

Playroma additions (`.playtracery.json`) are separate extensions: weighted rules, rule metadata, safe declarative conditions, typed parameters, deterministic Playroma RNG integration, resource limits, and structured diagnostics/traces.
