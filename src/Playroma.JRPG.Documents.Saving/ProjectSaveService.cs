using System.Security.Cryptography;
using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV.Documents;

namespace Playroma.JRPG.Documents.Saving;

public sealed class ProjectSaveService
{
    private readonly string _projectRoot;
    private readonly ProjectPathPolicy _paths;
    private readonly IAtomicFileReplacer _replacer;

    public ProjectSaveService(string projectRoot, IAtomicFileReplacer? replacer = null)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        _paths = new ProjectPathPolicy(_projectRoot);
        _replacer = replacer ?? new AtomicFileReplacer();
    }

    public async ValueTask<ProjectSaveResult> SaveAsync(
        ProjectSavePlan plan,
        ProjectSaveOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!string.Equals(Path.GetFullPath(plan.ProjectRoot), _projectRoot, PathComparison))
        {
            throw new InvalidOperationException("The save plan belongs to a different project root.");
        }

        List<ProjectDiagnostic> diagnostics = plan.Diagnostics.ToList();
        List<DocumentSaveResult> documentResults = [];
        List<FileMutationSaveResult> fileResults = [];
        if (!plan.CanSave)
        {
            foreach (DocumentSavePlan document in plan.Documents)
            {
                DocumentSaveStatus status = document.Disposition switch
                {
                    SaveDisposition.Unchanged => DocumentSaveStatus.SkippedUnchanged,
                    SaveDisposition.Conflict => DocumentSaveStatus.BlockedByExternalModification,
                    _ => DocumentSaveStatus.FailedValidation
                };
                documentResults.Add(new DocumentSaveResult(document.RelativePath, status, null, document.Diagnostics));
            }

            fileResults.AddRange(plan.Files.Select(mutation => new FileMutationSaveResult(
                mutation.MutationId,
                mutation.Kind,
                mutation.SourceRelativePath,
                mutation.DestinationRelativePath,
                mutation.Disposition == FileMutationDisposition.Conflict
                    ? FileMutationSaveStatus.BlockedByExternalModification
                    : FileMutationSaveStatus.FailedValidation,
                null,
                mutation.Diagnostics)));
            return Result(plan, false, documentResults, fileResults, diagnostics, null);
        }

        DocumentSavePlan[] writes = plan.Documents.Where(document => document.Disposition == SaveDisposition.WriteModified).ToArray();
        FileMutationSavePlan[] fileWrites = plan.Files.ToArray();
        foreach (DocumentSavePlan unchanged in plan.Documents.Where(document => document.Disposition == SaveDisposition.Unchanged))
        {
            documentResults.Add(new DocumentSaveResult(unchanged.RelativePath, DocumentSaveStatus.SkippedUnchanged, unchanged.OriginalFingerprint.Sha256, []));
        }

        if (writes.Length == 0 && fileWrites.Length == 0)
        {
            return Result(plan, true, documentResults, fileResults, diagnostics, null);
        }

        string playromaRoot = Path.Combine(_projectRoot, ".playroma");
        string lockPath = Path.Combine(playromaRoot, "save.lock");
        Directory.CreateDirectory(playromaRoot);
        await using FileStream? saveLock = AcquireLock(lockPath, diagnostics);
        if (saveLock is null)
        {
            return Result(plan, false, documentResults, fileResults, diagnostics, null);
        }

        try
        {
            if (!await RevalidateDocumentsAsync(writes, documentResults, diagnostics, cancellationToken).ConfigureAwait(false)
                || !await RevalidateFileMutationsAsync(fileWrites, fileResults, diagnostics, cancellationToken).ConfigureAwait(false))
            {
                return Result(plan, false, documentResults, fileResults, diagnostics, null);
            }

            string transactionDirectory = Path.Combine(playromaRoot, "transactions", plan.TransactionId.ToString("N"));
            string journalPath = Path.Combine(transactionDirectory, "transaction.json");
            Directory.CreateDirectory(transactionDirectory);
            List<SaveTransactionDocumentEntry> documentEntries = BuildDocumentEntries(plan, writes, transactionDirectory);
            List<SaveTransactionFileEntry> fileEntries = BuildFileEntries(fileWrites, transactionDirectory);
            SaveTransactionJournal journal = SaveTransactionJournal.Create(plan.TransactionId, documentEntries, fileEntries);

            try
            {
                await journal.WriteAsync(journalPath, cancellationToken).ConfigureAwait(false);
                journal = journal with { State = SaveTransactionState.PreflightComplete };
                await journal.WriteAsync(journalPath, cancellationToken).ConfigureAwait(false);

                await WriteDocumentTemporariesAsync(writes, documentEntries, cancellationToken).ConfigureAwait(false);
                await WriteAssetTemporariesAsync(fileWrites, fileEntries, cancellationToken).ConfigureAwait(false);
                journal = journal with { State = SaveTransactionState.TemporaryFilesWritten };
                await journal.WriteAsync(journalPath, cancellationToken).ConfigureAwait(false);

                await VerifyTemporariesAsync(writes, documentEntries, fileWrites, fileEntries, cancellationToken).ConfigureAwait(false);
                journal = journal with { State = SaveTransactionState.TemporaryFilesVerified };
                await journal.WriteAsync(journalPath, cancellationToken).ConfigureAwait(false);

                await CreateBackupsAsync(documentEntries, fileEntries, cancellationToken).ConfigureAwait(false);
                journal = journal with { State = SaveTransactionState.BackupsCreated };
                await journal.WriteAsync(journalPath, cancellationToken).ConfigureAwait(false);

                journal = journal with { State = SaveTransactionState.ReplacingFiles };
                await journal.WriteAsync(journalPath, cancellationToken).ConfigureAwait(false);
                await ApplyDocumentWritesAsync(writes, documentEntries, journalPath, journal, cancellationToken).ConfigureAwait(false);
                journal = await SaveTransactionJournal.ReadAsync(journalPath, cancellationToken).ConfigureAwait(false);
                await ApplyFileWritesAsync(fileWrites, fileEntries, journalPath, journal, cancellationToken).ConfigureAwait(false);
                journal = await SaveTransactionJournal.ReadAsync(journalPath, cancellationToken).ConfigureAwait(false);

                if (options.VerifyAfterWrite)
                {
                    await VerifyDestinationsAsync(writes, fileWrites, cancellationToken).ConfigureAwait(false);
                }

                journal = journal with
                {
                    State = SaveTransactionState.Committed,
                    Documents = documentEntries.ToArray(),
                    FileMutations = fileEntries.ToArray()
                };
                await journal.WriteAsync(journalPath, cancellationToken).ConfigureAwait(false);

                MarkDocumentsSaved(writes, documentResults);
                foreach (FileMutationSavePlan mutation in fileWrites)
                {
                    string? hash = mutation.Kind is FileMutationKind.Create or FileMutationKind.Replace
                        ? mutation.GeneratedSha256
                        : mutation.Kind == FileMutationKind.Move ? mutation.ExpectedSourceSha256 : null;
                    fileResults.Add(new FileMutationSaveResult(
                        mutation.MutationId,
                        mutation.Kind,
                        mutation.SourceRelativePath,
                        mutation.DestinationRelativePath,
                        StatusFor(mutation.Kind),
                        hash,
                        []));
                }

                ApplyRetention(Path.Combine(playromaRoot, "transactions"), options);
                return Result(plan, true, documentResults, fileResults, diagnostics, transactionDirectory);
            }
            catch (OperationCanceledException)
            {
                SaveTransactionJournal recovery = journal with
                {
                    State = SaveTransactionState.RecoveryRequired,
                    Documents = documentEntries.ToArray(),
                    FileMutations = fileEntries.ToArray()
                };
                await TryWriteRecoveryJournalAsync(recovery, journalPath, diagnostics).ConfigureAwait(false);
                MarkDocumentsFailed(writes);
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                diagnostics.Add(ProjectDiagnostic.Error(
                    DiagnosticCodes.AtomicReplacementFailed,
                    "The save transaction did not complete. Recovery information was retained.",
                    technicalDetail: exception.Message));
                SaveTransactionJournal recovery = journal with
                {
                    State = SaveTransactionState.RecoveryRequired,
                    Documents = documentEntries.ToArray(),
                    FileMutations = fileEntries.ToArray()
                };
                await TryWriteRecoveryJournalAsync(recovery, journalPath, diagnostics).ConfigureAwait(false);
                MarkDocumentsFailed(writes);
                documentResults.AddRange(writes.Select(document => new DocumentSaveResult(document.RelativePath, DocumentSaveStatus.FailedWriting, null, diagnostics)));
                fileResults.AddRange(fileWrites.Select(mutation => new FileMutationSaveResult(
                    mutation.MutationId, mutation.Kind, mutation.SourceRelativePath, mutation.DestinationRelativePath,
                    FileMutationSaveStatus.FailedWriting, null, diagnostics)));
                return Result(plan, false, documentResults, fileResults, diagnostics, transactionDirectory);
            }
        }
        finally
        {
            saveLock.Close();
            TryDelete(lockPath);
        }
    }

    private async ValueTask<bool> RevalidateDocumentsAsync(
        IReadOnlyList<DocumentSavePlan> writes,
        ICollection<DocumentSaveResult> results,
        ICollection<ProjectDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (DocumentSavePlan document in writes)
        {
            string expected = document.CurrentDiskFingerprint?.Sha256 ?? document.OriginalFingerprint.Sha256 ?? string.Empty;
            string path = _paths.ResolveRelative(document.RelativePath);
            string actual = await HashFileAsync(path, cancellationToken).ConfigureAwait(false);
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) continue;
            ProjectDiagnostic conflict = ProjectDiagnostic.Error(
                DiagnosticCodes.FileChangedExternally,
                "The file changed after the save plan was created. Saving was blocked.",
                new ProjectLocation(document.RelativePath));
            diagnostics.Add(conflict);
            results.Add(new DocumentSaveResult(document.RelativePath, DocumentSaveStatus.BlockedByExternalModification, actual, [conflict]));
            return false;
        }

        return true;
    }

    private async ValueTask<bool> RevalidateFileMutationsAsync(
        IReadOnlyList<FileMutationSavePlan> writes,
        ICollection<FileMutationSaveResult> results,
        ICollection<ProjectDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (FileMutationSavePlan mutation in writes)
        {
            string? sourceRelative = mutation.Kind == FileMutationKind.Replace
                ? mutation.DestinationRelativePath
                : mutation.SourceRelativePath;
            string? destinationRelative = mutation.DestinationRelativePath;
            string? sourcePath = sourceRelative is null ? null : _paths.ResolveRelative(sourceRelative);
            string? destinationPath = destinationRelative is null ? null : _paths.ResolveRelative(destinationRelative);
            bool samePhysicalPath = sourcePath is not null
                && destinationPath is not null
                && PathsReferToSameLocation(sourcePath, destinationPath);
            bool valid = mutation.Kind switch
            {
                FileMutationKind.Create => destinationPath is not null && !File.Exists(destinationPath),
                FileMutationKind.Replace => sourcePath is not null && File.Exists(sourcePath),
                FileMutationKind.Move => sourcePath is not null && destinationPath is not null && File.Exists(sourcePath)
                    && (samePhysicalPath || !File.Exists(destinationPath)),
                FileMutationKind.Delete => sourcePath is not null && File.Exists(sourcePath),
                _ => false
            };
            if (valid && sourcePath is not null && mutation.ExpectedSourceSha256 is not null)
            {
                string actual = await HashFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
                valid = string.Equals(actual, mutation.ExpectedSourceSha256, StringComparison.OrdinalIgnoreCase);
            }
            if (valid && mutation.StagedContentPath is not null && mutation.GeneratedSha256 is not null)
            {
                valid = File.Exists(mutation.StagedContentPath)
                    && string.Equals(await HashFileAsync(mutation.StagedContentPath, cancellationToken).ConfigureAwait(false), mutation.GeneratedSha256, StringComparison.OrdinalIgnoreCase);
            }
            if (valid) continue;

            ProjectDiagnostic conflict = ProjectDiagnostic.Error(
                DiagnosticCodes.Phase5AssetChangedExternally,
                "An asset involved in the staged operation changed or conflicts with the destination. Saving was blocked.",
                new ProjectLocation(sourceRelative ?? destinationRelative));
            diagnostics.Add(conflict);
            results.Add(new FileMutationSaveResult(
                mutation.MutationId, mutation.Kind, mutation.SourceRelativePath, mutation.DestinationRelativePath,
                FileMutationSaveStatus.BlockedByExternalModification, null, [conflict]));
            return false;
        }

        return true;
    }

    private List<SaveTransactionDocumentEntry> BuildDocumentEntries(
        ProjectSavePlan plan,
        IReadOnlyList<DocumentSavePlan> writes,
        string transactionDirectory)
    {
        List<SaveTransactionDocumentEntry> entries = [];
        foreach (DocumentSavePlan document in writes)
        {
            string safe = document.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            string tempPath = _paths.ResolveRelative(document.RelativePath) + $".playroma-{plan.TransactionId:N}.tmp";
            string backupPath = Path.Combine(transactionDirectory, "backups", "documents", safe);
            entries.Add(new SaveTransactionDocumentEntry(
                document.RelativePath,
                document.CurrentDiskFingerprint?.Sha256 ?? document.OriginalFingerprint.Sha256 ?? string.Empty,
                document.OutputSha256,
                tempPath,
                backupPath,
                false));
        }
        return entries;
    }

    private static List<SaveTransactionFileEntry> BuildFileEntries(
        IReadOnlyList<FileMutationSavePlan> writes,
        string transactionDirectory)
    {
        List<SaveTransactionFileEntry> entries = [];
        foreach (FileMutationSavePlan mutation in writes)
        {
            string extension = Path.GetExtension(mutation.DestinationRelativePath ?? mutation.SourceRelativePath ?? string.Empty);
            string? temporary = mutation.Kind is FileMutationKind.Create or FileMutationKind.Replace
                ? Path.Combine(transactionDirectory, "generated", mutation.MutationId.ToString("N") + extension)
                : null;
            string? backup = mutation.Kind == FileMutationKind.Create
                ? null
                : Path.Combine(transactionDirectory, "backups", "assets", mutation.MutationId.ToString("N") + extension + ".bak");
            entries.Add(new SaveTransactionFileEntry(
                mutation.MutationId,
                mutation.Kind,
                mutation.SourceRelativePath,
                mutation.DestinationRelativePath,
                mutation.ExpectedSourceSha256,
                mutation.GeneratedSha256,
                temporary,
                backup,
                mutation.Kind != FileMutationKind.Create,
                false));
        }
        return entries;
    }

    private static async ValueTask WriteDocumentTemporariesAsync(
        IReadOnlyList<DocumentSavePlan> writes,
        IReadOnlyList<SaveTransactionDocumentEntry> entries,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < writes.Count; index++)
        {
            string? directory = Path.GetDirectoryName(entries[index].TemporaryPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            await WriteThroughAsync(entries[index].TemporaryPath, writes[index].OutputBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask WriteAssetTemporariesAsync(
        IReadOnlyList<FileMutationSavePlan> writes,
        IReadOnlyList<SaveTransactionFileEntry> entries,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < writes.Count; index++)
        {
            FileMutationSavePlan mutation = writes[index];
            SaveTransactionFileEntry entry = entries[index];
            if (entry.TemporaryPath is null || mutation.StagedContentPath is null) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(entry.TemporaryPath) ?? throw new InvalidOperationException("Generated asset path has no directory."));
            await using FileStream input = new(mutation.StagedContentPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using FileStream output = new(entry.TemporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }
    }

    private static async ValueTask VerifyTemporariesAsync(
        IReadOnlyList<DocumentSavePlan> documents,
        IReadOnlyList<SaveTransactionDocumentEntry> documentEntries,
        IReadOnlyList<FileMutationSavePlan> files,
        IReadOnlyList<SaveTransactionFileEntry> fileEntries,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < documents.Count; index++)
        {
            string hash = await HashFileAsync(documentEntries[index].TemporaryPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, documents[index].OutputSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException($"Temporary output hash mismatch for {documents[index].RelativePath}.");
        }
        for (int index = 0; index < files.Count; index++)
        {
            if (fileEntries[index].TemporaryPath is null || files[index].GeneratedSha256 is null) continue;
            string hash = await HashFileAsync(fileEntries[index].TemporaryPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, files[index].GeneratedSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Temporary asset output hash mismatch.");
        }
    }

    private async ValueTask CreateBackupsAsync(
        IReadOnlyList<SaveTransactionDocumentEntry> documents,
        IReadOnlyList<SaveTransactionFileEntry> files,
        CancellationToken cancellationToken)
    {
        foreach (SaveTransactionDocumentEntry entry in documents)
        {
            await CopyVerifiedBackupAsync(_paths.ResolveRelative(entry.RelativePath), entry.BackupPath, entry.OriginalSha256, cancellationToken).ConfigureAwait(false);
        }
        foreach (SaveTransactionFileEntry entry in files)
        {
            if (!entry.OriginalExisted || entry.BackupPath is null) continue;
            string relative = entry.Kind == FileMutationKind.Replace ? entry.DestinationRelativePath! : entry.SourceRelativePath!;
            await CopyVerifiedBackupAsync(_paths.ResolveRelative(relative), entry.BackupPath, entry.OriginalSha256 ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask CopyVerifiedBackupAsync(string source, string backup, string expectedHash, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backup) ?? throw new InvalidOperationException("Backup path has no directory."));
        File.Copy(source, backup, overwrite: true);
        string hash = await HashFileAsync(backup, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Recovery backup hash mismatch.");
    }

    private async ValueTask ApplyDocumentWritesAsync(
        IReadOnlyList<DocumentSavePlan> writes,
        List<SaveTransactionDocumentEntry> entries,
        string journalPath,
        SaveTransactionJournal journal,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < writes.Count; index++)
        {
            DocumentSavePlan document = writes[index];
            SaveTransactionDocumentEntry entry = entries[index];
            string destination = _paths.ResolveRelative(entry.RelativePath);
            string preHash = await HashFileAsync(destination, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(preHash, entry.OriginalSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException($"The destination changed during the save transaction: {entry.RelativePath}.");
            if (document.Document is MutableJsonProjectDocument mutable) mutable.MarkSaving();
            else if (document.Document is MvPluginManifestDocument manifest) manifest.MarkSaving();
            await _replacer.ReplaceAsync(entry.TemporaryPath, destination, cancellationToken).ConfigureAwait(false);
            entries[index] = entry with { ReplacementComplete = true };
            journal = journal with { Documents = entries.ToArray() };
            await journal.WriteAsync(journalPath, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask ApplyFileWritesAsync(
        IReadOnlyList<FileMutationSavePlan> writes,
        List<SaveTransactionFileEntry> entries,
        string journalPath,
        SaveTransactionJournal journal,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < writes.Count; index++)
        {
            FileMutationSavePlan mutation = writes[index];
            SaveTransactionFileEntry entry = entries[index];
            cancellationToken.ThrowIfCancellationRequested();
            switch (mutation.Kind)
            {
                case FileMutationKind.Create:
                case FileMutationKind.Replace:
                {
                    string destination = _paths.ResolveRelative(mutation.DestinationRelativePath!);
                    await _replacer.ReplaceAsync(entry.TemporaryPath!, destination, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case FileMutationKind.Move:
                {
                    string source = _paths.ResolveRelative(mutation.SourceRelativePath!);
                    string destination = _paths.ResolveRelative(mutation.DestinationRelativePath!);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Move destination has no directory."));
                    MoveFileSafely(source, destination);
                    break;
                }
                case FileMutationKind.Delete:
                    File.Delete(_paths.ResolveRelative(mutation.SourceRelativePath!));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation.Kind));
            }
            entries[index] = entry with { ReplacementComplete = true };
            journal = journal with { FileMutations = entries.ToArray() };
            await journal.WriteAsync(journalPath, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask VerifyDestinationsAsync(
        IReadOnlyList<DocumentSavePlan> documents,
        IReadOnlyList<FileMutationSavePlan> files,
        CancellationToken cancellationToken)
    {
        foreach (DocumentSavePlan document in documents)
        {
            string hash = await HashFileAsync(_paths.ResolveRelative(document.RelativePath), cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, document.OutputSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException($"Saved output hash mismatch for {document.RelativePath}.");
        }
        foreach (FileMutationSavePlan mutation in files)
        {
            switch (mutation.Kind)
            {
                case FileMutationKind.Create:
                case FileMutationKind.Replace:
                {
                    string hash = await HashFileAsync(_paths.ResolveRelative(mutation.DestinationRelativePath!), cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(hash, mutation.GeneratedSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Saved asset hash mismatch.");
                    break;
                }
                case FileMutationKind.Move:
                {
                    string source = _paths.ResolveRelative(mutation.SourceRelativePath!);
                    string destination = _paths.ResolveRelative(mutation.DestinationRelativePath!);
                    if (!PathsReferToSameLocation(source, destination) && File.Exists(source)) throw new IOException("Moved asset remains at its original path.");
                    string hash = await HashFileAsync(destination, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(hash, mutation.ExpectedSourceSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Moved asset hash mismatch.");
                    break;
                }
                case FileMutationKind.Delete:
                    if (File.Exists(_paths.ResolveRelative(mutation.SourceRelativePath!))) throw new IOException("Deleted asset still exists.");
                    break;
            }
        }
    }

    private void MarkDocumentsSaved(IReadOnlyList<DocumentSavePlan> writes, ICollection<DocumentSaveResult> results)
    {
        foreach (DocumentSavePlan document in writes)
        {
            FileInfo info = new(_paths.ResolveRelative(document.RelativePath));
            ContentFingerprint fingerprint = new(info.Length, info.LastWriteTimeUtc, document.OutputSha256);
            if (document.Document is MutableJsonProjectDocument mutable) mutable.MarkSaved(document.OutputBytes, fingerprint);
            else if (document.Document is MvPluginManifestDocument manifest) manifest.MarkSaved(document.OutputBytes, fingerprint);
            results.Add(new DocumentSaveResult(document.RelativePath, DocumentSaveStatus.Saved, document.OutputSha256, []));
        }
    }

    private static void MarkDocumentsFailed(IEnumerable<DocumentSavePlan> writes)
    {
        foreach (DocumentSavePlan document in writes)
        {
            if (document.Document is MutableJsonProjectDocument mutable) mutable.MarkSaveFailed();
            else if (document.Document is MvPluginManifestDocument manifest) manifest.MarkSaveFailed();
        }
    }

    private static FileMutationSaveStatus StatusFor(FileMutationKind kind) => kind switch
    {
        FileMutationKind.Create => FileMutationSaveStatus.Created,
        FileMutationKind.Replace => FileMutationSaveStatus.Replaced,
        FileMutationKind.Move => FileMutationSaveStatus.Moved,
        FileMutationKind.Delete => FileMutationSaveStatus.Deleted,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static ProjectSaveResult Result(
        ProjectSavePlan plan,
        bool succeeded,
        IReadOnlyList<DocumentSaveResult> documents,
        IReadOnlyList<FileMutationSaveResult> files,
        IReadOnlyList<ProjectDiagnostic> diagnostics,
        string? transactionDirectory) =>
        new(plan.TransactionId, succeeded, documents, diagnostics, transactionDirectory) { FileMutations = files };

    private static async ValueTask TryWriteRecoveryJournalAsync(
        SaveTransactionJournal journal,
        string journalPath,
        ICollection<ProjectDiagnostic> diagnostics)
    {
        try { await journal.WriteAsync(journalPath, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Add(ProjectDiagnostic.Error(DiagnosticCodes.SaveRecoveryRequired, "The recovery journal could not be updated. Existing transaction files were retained.", technicalDetail: exception.Message));
        }
    }

    private static async ValueTask WriteThroughAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static FileStream? AcquireLock(string path, ICollection<ProjectDiagnostic> diagnostics)
    {
        try
        {
            FileStream stream = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            byte[] owner = System.Text.Encoding.UTF8.GetBytes($"pid={Environment.ProcessId};utc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
            stream.Write(owner);
            stream.Flush(flushToDisk: true);
            stream.Position = 0;
            return stream;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(ProjectDiagnostic.Error(DiagnosticCodes.ProjectSaveLockHeld, "The project save lock could not be acquired.", technicalDetail: exception.Message));
            return null;
        }
    }

    private static async ValueTask<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static void ApplyRetention(string transactionsRoot, ProjectSaveOptions options)
    {
        if (!Directory.Exists(transactionsRoot)) return;
        List<DirectoryInfo> committed = [];
        foreach (DirectoryInfo directory in new DirectoryInfo(transactionsRoot).EnumerateDirectories())
        {
            try
            {
                string journalPath = Path.Combine(directory.FullName, "transaction.json");
                if (!File.Exists(journalPath)) continue;
                SaveTransactionJournal journal = SaveTransactionJournal.ReadAsync(journalPath, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                if (journal.State == SaveTransactionState.Committed) committed.Add(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException) { }
        }
        DirectoryInfo[] ordered = committed.OrderByDescending(directory => directory.CreationTimeUtc).ToArray();
        long retainedBytes = 0;
        for (int index = 0; index < ordered.Length; index++)
        {
            long bytes = ordered[index].EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            bool retain = options.CreateBackup && index < options.BackupRetentionCount && retainedBytes + bytes <= options.MaximumBackupBytes;
            if (retain) { retainedBytes += bytes; continue; }
            try { ordered[index].Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }


    private static bool PathsReferToSameLocation(string left, string right) => string.Equals(
        Path.GetFullPath(left),
        Path.GetFullPath(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void MoveFileSafely(string source, string destination)
    {
        if (string.Equals(source, destination, StringComparison.Ordinal)) return;
        if (PathsReferToSameLocation(source, destination))
        {
            string directory = Path.GetDirectoryName(source) ?? throw new InvalidOperationException("Move source has no directory.");
            string temporary = Path.Combine(directory, $".playroma-case-move-{Guid.NewGuid():N}.tmp");
            try
            {
                File.Move(source, temporary);
                File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary) && !File.Exists(source))
                {
                    try { File.Move(temporary, source); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            return;
        }

        File.Move(source, destination);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
