using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;

namespace Playroma.JRPG.DatabaseEditing;

public interface IDatabaseMutationService
{
    ValueTask SetFieldAsync(
        IReadOnlyList<DatabaseRecordHandle> records,
        DatabaseFieldDescriptor field,
        object? value,
        bool allowHistoryMerge = false,
        CancellationToken cancellationToken = default);

    ValueTask ExecuteAsync(
        IEditorCommand command,
        string? transactionDescription = null,
        CancellationToken cancellationToken = default);

    ValueTask CreateRecordAsync(
        IDatabaseEditorDefinition definition,
        string relativePath,
        int recordId,
        CancellationToken cancellationToken = default);

    ValueTask DuplicateRecordAsync(
        IDatabaseEditorDefinition definition,
        MutableJsonProjectDocument document,
        int sourceId,
        int destinationId,
        CancellationToken cancellationToken = default);

    ValueTask DeleteRecordsAsync(
        string relativePath,
        IReadOnlyList<int> recordIds,
        CancellationToken cancellationToken = default);
}

public sealed class DatabaseMutationService : IDatabaseMutationService
{
    private readonly MvEditingSession _session;

    public DatabaseMutationService(MvEditingSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public async ValueTask SetFieldAsync(
        IReadOnlyList<DatabaseRecordHandle> records,
        DatabaseFieldDescriptor field,
        object? value,
        bool allowHistoryMerge = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(field);
        if (records.Count == 0) return;
        if (records.Count > 1 && !field.SupportsBatchEditing)
        {
            throw new InvalidOperationException($"Field '{field.DisplayName}' does not support batch editing.");
        }
        ProjectDiagnostic? validation = field.ValidateObject(value);
        if (validation is not null && validation.Severity >= DiagnosticSeverity.Error)
        {
            throw new ArgumentException(validation.Message, nameof(value));
        }
        string rawJson = field.EncodeObject(value);
        string description = records.Count == 1
            ? $"Set {field.DisplayName} on record {records[0].Id}"
            : $"Set {field.DisplayName} on {records.Count} records";
        await using ProjectTransaction transaction = _session.BeginTransaction(description, allowHistoryMerge);
        foreach (DatabaseRecordHandle record in records)
        {
            JsonPath target = DatabaseValueAccessor.Combine(record.Path, field.RelativePath);
            transaction.Add(new SetJsonValueCommand(record.RelativePath, target, rawJson, description));
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ExecuteAsync(
        IEditorCommand command,
        string? transactionDescription = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using ProjectTransaction transaction = _session.BeginTransaction(transactionDescription ?? command.Description);
        transaction.Add(command);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask CreateRecordAsync(
        IDatabaseEditorDefinition definition,
        string relativePath,
        int recordId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            new InsertDatabaseRecordCommand(relativePath, recordId, definition.CreateDefaultRecordJson(recordId), $"Create {definition.SingularName} {recordId}"),
            cancellationToken: cancellationToken);

    public ValueTask DuplicateRecordAsync(
        IDatabaseEditorDefinition definition,
        MutableJsonProjectDocument document,
        int sourceId,
        int destinationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        string raw = document.GetRawJson(JsonPath.Root.Append(sourceId))
            ?? throw new InvalidOperationException($"Record {sourceId} does not exist.");
        string duplicate = definition.CreateDuplicateRecordJson(raw, destinationId);
        return ExecuteAsync(
            new InsertDatabaseRecordCommand(document.RelativePath, destinationId, duplicate, $"Duplicate {definition.SingularName} {sourceId} as {destinationId}"),
            cancellationToken: cancellationToken);
    }

    public async ValueTask DeleteRecordsAsync(
        string relativePath,
        IReadOnlyList<int> recordIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        int[] ids = recordIds.Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0) return;
        string description = ids.Length == 1 ? $"Delete database record {ids[0]}" : $"Delete {ids.Length} database records";
        await using ProjectTransaction transaction = _session.BeginTransaction(description);
        foreach (int id in ids) transaction.Add(new RemoveDatabaseRecordCommand(relativePath, id));
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
