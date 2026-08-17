# Playroma Text Studio — Phase 6 assets

Phase 6 adds typed semantic features, safe semantic predicates, feature unification, grammatical agreement helpers, semantic scoring, and hidden-candidate generate/filter/rank pipelines.

Files:

- `example-semantic-dialogue.playtracery.json` — extended Tracery rules with string, number, boolean, and string-list features.
- `example-ranked-recipe.playtext.json` — a recipe with a hidden candidate pool, semantic filter, weighted scorers, and diversity ranking.
- `playtext.semantic-features.schema.json` — the primitive semantic feature-map shape.
- `playtext.candidate-pipeline.schema.json` — candidate-pipeline schema.

Project recipes belong under `data/PlayromaText/recipes/`; project Tracery grammars belong under `data/PlayromaText/grammars/`.

Semantic predicates are declarative. They do not execute C#, JavaScript, shell commands, reflection, or arbitrary scripts. Common examples:

- `feature.tone == context.tone`
- `feature.threat >= 0.6 && tag.boss`
- `has(feature.dialogue.act) && feature.mood != "hostile"`
- `score.originality >= 0.8`

Candidate pipelines run outside the ordinary recipe engine. This lets any pre-existing Phase 1–5 recipe participate in hidden candidate generation and ranking without changing its individual generator stages.
