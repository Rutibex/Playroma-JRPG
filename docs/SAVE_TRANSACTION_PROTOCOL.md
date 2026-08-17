# Save Transaction Protocol

## Preflight

1. Resolve every target through `ProjectPathPolicy`.
2. Calculate the current strong disk fingerprint.
3. Compare it with the opening fingerprint.
4. Generate output in memory.
5. Parse generated JSON where applicable.
6. Compare semantic differences with authorized changes.
7. Produce an immutable `ProjectSavePlan`.

No file is written during preflight.

## Commit

1. Acquire `.playroma/save.lock` using exclusive create semantics.
2. Create `.playroma/transactions/<id>/transaction.json`.
3. Write each temporary file beside its destination.
4. Flush temporary output to disk.
5. Verify every temporary SHA-256 hash.
6. Copy original targets into the transaction backup tree.
7. Mark the journal as replacing files.
8. Replace each target and update the per-file completion flag.
9. Re-read each target and verify its planned hash.
10. Mark the transaction committed.
11. Establish the generated files as the new document baselines.
12. Apply backup retention limits.

## Failure

A failure marks the journal `RecoveryRequired` and leaves temporary and backup
files in place. The source files are never silently guessed into a coherent
state.

## Recovery

`SaveRecoveryService` inspects journal state and recommends rollback, completion,
cleanup, or manual review. Rollback copies verified backups over replaced files.
Completion uses retained generated files for replacements that had not finished.
Ambiguous mixed states require explicit review.
