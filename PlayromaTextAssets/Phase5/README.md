# Playroma Text Studio — Phase 5 Authoring Assets

Phase 5 adds project corpora (`*.playcorpus.json`) and the `phase5.markov` recipe stage.

- `example-tavern.playcorpus.json` demonstrates corpus provenance, normalization defaults, and persisted Markov training defaults.
- `example-project-markov.playtext.json` demonstrates referencing a Corpus Lab corpus through `corpusId`.
- `playtext.corpus.schema.json` documents the project corpus file format.

Project corpora belong in `data/PlayromaText/corpora/`. Derived Markov caches belong under `.playroma/cache/text/markov/` and can be deleted at any time; they are rebuilt from corpus source data and training settings.
