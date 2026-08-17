# Yanfly Phase 2 Catalog Compiler Report

Generated from the canonicalized `YEP Plugins Complete.zip` inventory established in Phase 0.

## Coverage summary

| Measurement | Count |
|---|---:|
| Canonical plugins | 218 |
| Plugins with English help metadata | 218 |
| Indexed help sections | 976 |
| Indexed plugin parameter declarations | 8,979 |
| Total classified source discoveries | 22,525 |
| Unique discoveries | 12,292 |
| Duplicate help examples | 10,233 |
| Generated authoring features | 2,903 |
| Typed-feature discoveries | 1,459 |
| Advanced/raw block discoveries | 366 |
| Context-assistant discoveries | 1,290 |
| Documentation-only candidates | 198 |
| Unclassified/review discoveries | 0 |

Plugin parameter declarations are numerous because several YEP plugins expose large repeated configuration tables, including menu slots, TP modes, picture/common-event bindings, macros, and credits entries.

## Runtime parser evidence

| Evidence tier | Count |
|---|---:|
| Runtime parser-expression match | 1,990 |
| Runtime keyword-sequence match | 857 |
| Help-only conservative match | 268 |

The verifier stores only significant tokens, source line numbers, and an evidence hash. It does not copy JavaScript source lines into the generated catalog.

## Discovery classes

| Discovery class | Count |
|---|---:|
| Notetags | 1,430 |
| Block openings | 216 |
| Block closings | 233 |
| Comment tags | 52 |
| Plugin commands | 736 |
| Action sequence entries | 259 |
| Text codes | 240 |
| Script calls | 92 |
| Move-route helpers | 5 |
| Plugin parameters | 8,979 |
| Documentation markup retained for audit | 10,167 |
| Changelog references | 109 |
| Code comparisons | 7 |

## Generated feature surfaces

| Authoring surface | Features |
|---|---:|
| Database notes | 1,420 |
| Map notes | 77 |
| Tileset notes | 9 |
| Event notes | 68 |
| Event-page comments | 54 |
| Plugin commands | 724 |
| Action sequences | 254 |
| Message text codes | 211 |
| Script calls | 81 |
| Move-route scripts | 5 |

## Semantic overlay

The compiler generates safe generic definitions for discovered syntax and then applies a reviewed semantic overlay. Phase 2 includes typed overrides and specialized-editor assignments for the highest-value systems:

- Battle AI Priority
- Battle Engine action sequences and Action Sequence Packs
- Skill Core
- Passive States and Passive Auras
- Buffs & States lifecycle effects
- Item Core, durability, augments, and upgrade slots
- Enemy levels and extra drops
- Region events and restrictions
- Message Core text-code composition
- Move Route Core

The overlay is versioned separately from extraction so future corrections do not require changing the source scanner.

## Licensing boundary

No YEP JavaScript is included in the repository or generated artifacts. Long help prose and plugin-parameter descriptions are omitted; only independently structured metadata, short syntax samples, parameter names/types/defaults, source fingerprints, line numbers, and hashes are retained.
