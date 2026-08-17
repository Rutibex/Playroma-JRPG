using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playroma.JRPG.Documents.Saving;

public enum SaveTransactionState
{
    Created,
    PreflightComplete,
    TemporaryFilesWritten,
    TemporaryFilesVerified,
    BackupsCreated,
    ReplacingFiles,
    Committed,
    RolledBack,
    RecoveryRequired
}

public sealed record SaveTransactionDocumentEntry(
    string RelativePath,
    string OriginalSha256,
    string GeneratedSha256,
    string TemporaryPath,
    string BackupPath,
    bool ReplacementComplete);

public sealed record SaveTransactionFileEntry(
    Guid MutationId,
    FileMutationKind Kind,
    string? SourceRelativePath,
    string? DestinationRelativePath,
    string? OriginalSha256,
    string? GeneratedSha256,
    string? TemporaryPath,
    string? BackupPath,
    bool OriginalExisted,
    bool ReplacementComplete);

public sealed record SaveTransactionJournal(
    string Schema,
    Guid TransactionId,
    SaveTransactionState State,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<SaveTransactionDocumentEntry> Documents,
    IReadOnlyList<SaveTransactionFileEntry>? FileMutations = null)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public IReadOnlyList<SaveTransactionFileEntry> Files => FileMutations ?? [];

    public static SaveTransactionJournal Create(
        Guid id,
        IReadOnlyList<SaveTransactionDocumentEntry> documents,
        IReadOnlyList<SaveTransactionFileEntry>? files = null) =>
        new("playroma-jrpg-save-transaction/2", id, SaveTransactionState.Created, DateTimeOffset.UtcNow, documents, files ?? []);

    public async ValueTask WriteAsync(string path, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(this, Options);
        string temporary = path + ".tmp";
        await using (FileStream stream = new(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }

    public static async ValueTask<SaveTransactionJournal> ReadAsync(string path, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<SaveTransactionJournal>(bytes, Options)
            ?? throw new InvalidDataException("The save transaction journal is empty.");
    }
}
