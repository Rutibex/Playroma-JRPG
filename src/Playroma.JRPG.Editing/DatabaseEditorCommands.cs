using Playroma.JRPG.Documents.Json;

namespace Playroma.JRPG.Editing;

public sealed class SetDatabaseFieldCommand : SetJsonValueCommand
{
    public SetDatabaseFieldCommand(
        string relativePath,
        int recordId,
        string propertyName,
        string rawJson,
        string? description = null)
        : base(
            relativePath,
            JsonPath.Root.Append(ValidateRecordId(recordId)).Append(propertyName),
            rawJson,
            description ?? $"Set {propertyName} on record {recordId}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
    }

    private static int ValidateRecordId(int recordId) => recordId > 0
        ? recordId
        : throw new ArgumentOutOfRangeException(nameof(recordId), "RPG Maker database record IDs must be positive.");
}

/// <summary>
/// Creates or replaces a record at its numeric ID without shifting any other IDs.
/// The target slot may be an existing null slot or the next append position.
/// </summary>
public sealed class InsertDatabaseRecordCommand : SetJsonValueCommand
{
    public InsertDatabaseRecordCommand(
        string relativePath,
        int recordId,
        string rawRecordJson,
        string? description = null)
        : base(
            relativePath,
            JsonPath.Root.Append(ValidateRecordId(recordId)),
            rawRecordJson,
            description ?? $"Set database record {recordId}")
    {
    }

    private static int ValidateRecordId(int recordId) => recordId > 0
        ? recordId
        : throw new ArgumentOutOfRangeException(nameof(recordId), "RPG Maker database record IDs must be positive.");
}

/// <summary>
/// Deletes a record by replacing its array slot with null. This preserves every
/// subsequent numeric ID, which is the safe RPG Maker MV database behavior.
/// </summary>
public sealed class RemoveDatabaseRecordCommand : SetJsonValueCommand
{
    public RemoveDatabaseRecordCommand(
        string relativePath,
        int recordId,
        string? description = null)
        : base(
            relativePath,
            JsonPath.Root.Append(ValidateRecordId(recordId)),
            "null",
            description ?? $"Delete database record {recordId}")
    {
    }

    private static int ValidateRecordId(int recordId) => recordId > 0
        ? recordId
        : throw new ArgumentOutOfRangeException(nameof(recordId), "RPG Maker database record IDs must be positive.");
}

public sealed class ClearDatabaseRecordCommand : SetJsonValueCommand
{
    public ClearDatabaseRecordCommand(
        string relativePath,
        int recordId,
        string? description = null)
        : base(
            relativePath,
            JsonPath.Root.Append(ValidateRecordId(recordId)),
            "null",
            description ?? $"Clear database record {recordId}")
    {
    }

    private static int ValidateRecordId(int recordId) => recordId > 0
        ? recordId
        : throw new ArgumentOutOfRangeException(nameof(recordId), "RPG Maker database record IDs must be positive.");
}

/// <summary>
/// Explicit low-level ID-renumbering primitive. Unlike InsertDatabaseRecordCommand,
/// this shifts later array slots and therefore must only be used by a dependency-aware
/// refactoring transaction that updates every exact reference.
/// </summary>
public sealed class InsertDatabaseArraySlotCommand : InsertJsonArrayElementCommand
{
    public InsertDatabaseArraySlotCommand(
        string relativePath,
        int index,
        string rawRecordJson,
        string? description = null)
        : base(
            relativePath,
            JsonPath.Root,
            index,
            rawRecordJson,
            description ?? $"Insert database array slot {index}")
    {
    }
}

/// <summary>
/// Explicit low-level ID-renumbering primitive. It shifts every later array slot.
/// </summary>
public sealed class RemoveDatabaseArraySlotCommand : RemoveJsonArrayElementCommand
{
    public RemoveDatabaseArraySlotCommand(
        string relativePath,
        int index,
        string? description = null)
        : base(
            relativePath,
            JsonPath.Root,
            index,
            description ?? $"Remove database array slot {index}")
    {
    }
}
