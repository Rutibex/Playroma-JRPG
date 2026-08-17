# Playroma Text Studio Phase 2 — Built-in Name Corpus Provenance

The Phase 2 built-in language profiles and their seed corpora (`common`, `valestrian`, `northreach`, `sylvan`, `kharic`, and `umbral`) were authored for this Playroma implementation. They are not copied from RPG Maker, Tracery, commercial games, novels, fan wikis, online name lists, or other third-party naming datasets.

The Markov engine is data-agnostic. User-imported corpora remain user-supplied project assets and should retain their own source/license metadata when the later Corpus Lab phase adds corpus-management UI.

Phase 2 includes an originality guard that rejects exact training-corpus reproduction and can reject near matches by composite similarity threshold.
