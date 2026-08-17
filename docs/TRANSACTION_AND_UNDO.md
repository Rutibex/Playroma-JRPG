# Transaction and Undo Protocol

## Creating a transaction

```csharp
await using ProjectTransaction transaction = session.BeginTransaction("Rename actor and class");
transaction.Add(new SetJsonStringCommand("data/Actors.json", "/1/name", "Alicia"));
transaction.Add(new SetJsonStringCommand("data/Classes.json", "/1/name", "Vanguard"));

TransactionPreview preview = await transaction.PreviewAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

The two document edits become one undo entry.

## Built-in commands

- `SetJsonValueCommand`
- `SetJsonStringCommand`
- `SetJsonInt32Command`
- `AddJsonPropertyCommand`
- `RemoveJsonPropertyCommand`
- `InsertJsonArrayElementCommand`
- `RemoveJsonArrayElementCommand`
- `SetDatabaseFieldCommand`
- `InsertDatabaseRecordCommand`
- `RemoveDatabaseRecordCommand`
- `InsertDatabaseArraySlotCommand`
- `RemoveDatabaseArraySlotCommand`
- `MapTileStrokeCommand`
- `CompositeEditorCommand`

## Command merging

Text-entry transactions may request history merging. Consecutive
`SetJsonValueCommand` operations merge only when both target the same document
and JSON path. The first command's original value and the newest command's final
value are retained. A merge invalidates a saved-state marker when necessary.

## Sparse map strokes

`MapTileStrokeCommand` stores one `TileDelta` per changed flat-map-data index:

```csharp
public readonly record struct TileDelta(int Index, int Before, int After);
```

Repeated deltas for one index are collapsed to the final requested value. The
original value is captured only on first execution, allowing efficient redo.

## Undo state

`UndoManager` tracks a cursor through immutable history entries. It exposes:

- `CanUndo` and `CanRedo`;
- descriptions for the next undo and redo;
- undo and redo counts;
- `IsAtSavedState`;
- `MarkSaved()` after a successful verified save.

Creating a new edit after undo truncates redo history. If the truncated region
contained the saved marker, the saved marker becomes unknown.

## Atomic failure recovery

Before commit, preview, undo, or redo, every distinct targeted JSON document is
captured. A partial command failure therefore does not depend on the failing
command's own inverse logic. The exact snapshots are authoritative.

Commands are still required to implement `UndoAsync`, because committed history
uses command inverses during ordinary undo/redo. Snapshot restoration protects
that process from partial failures.
## Command notifications

One `CommandApplied` notification is published after a complete commit, undo,
redo, or explicit rollback. Observers are invoked independently. An observer
exception is reported through `CommandNotificationFailed` and is not allowed to
turn an already-completed edit into a failed transaction.

