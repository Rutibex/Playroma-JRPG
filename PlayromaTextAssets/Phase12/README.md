# Phase 12 quest grammar assets

`.playquest.json` is Playroma Text Studio's declarative quest-planning format. A grammar may use an authored `rootTask` plus HTN methods, or omit `rootTask` and allow bounded forward search to choose a causal action sequence that satisfies `goalPredicates`.

Quest definitions never execute C#, JavaScript, or shell code. Preconditions and goals use the same safe Phase 6 semantic-predicate language used elsewhere in Text Studio.
