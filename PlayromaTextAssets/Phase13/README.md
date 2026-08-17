# Phase 13 — Batch Content Builder assets

Phase 13 is primarily an editor/orchestration phase, so it does not introduce a new recipe file format. Existing `.playtext.json` recipes are used in bulk.

The Batch Content Builder can:

- fill blank text fields on existing RPG Maker MV records or maps;
- update existing records in bulk;
- append new RPG Maker MV records using safe null slots / consecutive IDs;
- freeze individual cells or whole rows;
- regenerate selected cells or rows;
- detect exact and near duplicate names against the batch and open project;
- import/export the working grid as CSV;
- preview all planned changes before committing them as one undoable Playroma transaction.

`example-enemy-batch.csv` demonstrates the CSV interchange shape. Generate/prepare a matching Enemy batch before importing the example.
