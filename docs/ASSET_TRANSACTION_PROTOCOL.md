# Asset Transaction Protocol

## Principle

A UI control never directly copies, moves, replaces, or deletes a project asset. Physical operations enter `ProjectFileOverlay` and are committed by the verified save service together with JSON changes.

## Staging

Create and replace commands copy external content into a Playroma staging directory. Move and delete commands record source fingerprints. Pending operations appear through `IAssetOverlayContentSource`, allowing previews before save.

## Preview

`ProjectTransaction.PreviewAsync`:

1. captures JSON baselines;
2. executes commands into memory and the file overlay;
3. reports semantic JSON and physical file changes;
4. reverses temporary command effects;
5. releases preview-only staged resources.

## Commit to editing history

`CommitAsync` executes the same commands, records one undo entry, and leaves physical changes staged. The project remains unchanged until Save.

## Save plan

The plan contains `DocumentSavePlan` and `FileMutationSavePlan` entries. Source and destination fingerprints are checked before writing. New/replacement bytes are re-read and hashed before final application.

## Journaled application

1. validate the plan;
2. acquire the project save lock;
3. create transaction journal and backups;
4. write verified temporary output;
5. atomically replace/create files;
6. apply moves;
7. apply deletions only after backup;
8. verify final paths and hashes;
9. mark the journal committed;
10. remove completed overlay entries and refresh indexes.

## Recovery

An incomplete journal contains sufficient document and file information to recommend completion or rollback. Backups are retained until the transaction is verified. Ambiguous states are never silently repaired.

## Reference-aware rename

A logical rename composes one move command per variant and one JSON command per distinct exact reference. Heuristic references are included in the preview report only.

## Path safety

All relative paths are normalized and resolved through `ProjectPathPolicy`. Paths escaping the project root, conflicting pending operations, stale fingerprints, missing staged content, and destination collisions fail before destructive steps.
