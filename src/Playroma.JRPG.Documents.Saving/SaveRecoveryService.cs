using System.Security.Cryptography;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;

namespace Playroma.JRPG.Documents.Saving;

public enum RecoveryRecommendation
{
    None,
    CompleteCommit,
    RollBack,
    CleanupUncommitted,
    CleanupCommitted,
    ManualReview
}

public sealed record SaveRecoveryCandidate(
    string TransactionDirectory,
    SaveTransactionJournal Journal,
    RecoveryRecommendation Recommendation,
    IReadOnlyList<ProjectDiagnostic> Diagnostics);

public sealed class SaveRecoveryService
{
    private readonly string _projectRoot;
    private readonly ProjectPathPolicy _paths;

    public SaveRecoveryService(string projectRoot)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        _paths = new ProjectPathPolicy(_projectRoot);
    }

    public async ValueTask<IReadOnlyList<SaveRecoveryCandidate>> InspectAsync(CancellationToken cancellationToken = default)
    {
        string root = Path.Combine(_projectRoot, ".playroma", "transactions");
        if (!Directory.Exists(root)) return [];
        List<SaveRecoveryCandidate> candidates = [];
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            string journalPath = Path.Combine(directory, "transaction.json");
            if (!File.Exists(journalPath)) continue;
            try
            {
                SaveTransactionJournal journal = await SaveTransactionJournal.ReadAsync(journalPath, cancellationToken).ConfigureAwait(false);
                RecoveryRecommendation recommendation = DetermineRecommendation(journal);
                if (recommendation != RecoveryRecommendation.None) candidates.Add(new SaveRecoveryCandidate(directory, journal, recommendation, []));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                candidates.Add(new SaveRecoveryCandidate(
                    directory,
                    new SaveTransactionJournal("unknown", Guid.Empty, SaveTransactionState.RecoveryRequired, DateTimeOffset.MinValue, []),
                    RecoveryRecommendation.ManualReview,
                    [ProjectDiagnostic.Error(DiagnosticCodes.RecoveryStateAmbiguous, "The transaction journal could not be read.", technicalDetail: exception.Message)]));
            }
        }
        return candidates;
    }

    public async ValueTask RollBackAsync(SaveRecoveryCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Recommendation != RecoveryRecommendation.RollBack)
        {
            throw new InvalidOperationException("Automatic rollback is permitted only when every destination can be restored unambiguously from a verified backup.");
        }

        foreach (SaveTransactionDocumentEntry entry in candidate.Journal.Documents)
        {
            await RestoreBackupAsync(entry.BackupPath, entry.OriginalSha256, entry.RelativePath, cancellationToken).ConfigureAwait(false);
        }

        foreach (SaveTransactionFileEntry entry in candidate.Journal.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (entry.Kind)
            {
                case FileMutationKind.Create:
                    if (entry.DestinationRelativePath is not null) TryDelete(_paths.ResolveRelative(entry.DestinationRelativePath));
                    break;
                case FileMutationKind.Replace:
                    await RestoreBackupAsync(entry.BackupPath!, entry.OriginalSha256!, entry.DestinationRelativePath!, cancellationToken).ConfigureAwait(false);
                    break;
                case FileMutationKind.Move:
                    if (entry.DestinationRelativePath is not null) TryDelete(_paths.ResolveRelative(entry.DestinationRelativePath));
                    await RestoreBackupAsync(entry.BackupPath!, entry.OriginalSha256!, entry.SourceRelativePath!, cancellationToken).ConfigureAwait(false);
                    break;
                case FileMutationKind.Delete:
                    await RestoreBackupAsync(entry.BackupPath!, entry.OriginalSha256!, entry.SourceRelativePath!, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        SaveTransactionJournal completed = candidate.Journal with { State = SaveTransactionState.RolledBack };
        await completed.WriteAsync(Path.Combine(candidate.TransactionDirectory, "transaction.json"), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CompleteAsync(SaveRecoveryCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Recommendation != RecoveryRecommendation.CompleteCommit)
        {
            throw new InvalidOperationException("Automatic completion is permitted only when every replacement can be completed from verified transaction data.");
        }

        foreach (SaveTransactionDocumentEntry entry in candidate.Journal.Documents.Where(entry => !entry.ReplacementComplete))
        {
            await CompleteGeneratedAsync(entry.TemporaryPath, entry.GeneratedSha256, entry.RelativePath, cancellationToken).ConfigureAwait(false);
        }

        List<SaveTransactionFileEntry> fileEntries = candidate.Journal.Files.ToList();
        for (int index = 0; index < fileEntries.Count; index++)
        {
            SaveTransactionFileEntry entry = fileEntries[index];
            if (entry.ReplacementComplete) continue;
            switch (entry.Kind)
            {
                case FileMutationKind.Create:
                case FileMutationKind.Replace:
                    await CompleteGeneratedAsync(entry.TemporaryPath!, entry.GeneratedSha256!, entry.DestinationRelativePath!, cancellationToken).ConfigureAwait(false);
                    break;
                case FileMutationKind.Move:
                {
                    string source = _paths.ResolveRelative(entry.SourceRelativePath!);
                    string destination = _paths.ResolveRelative(entry.DestinationRelativePath!);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Move destination has no directory."));
                    if (File.Exists(source)) MoveFileSafely(source, destination);
                    break;
                }
                case FileMutationKind.Delete:
                    TryDelete(_paths.ResolveRelative(entry.SourceRelativePath!));
                    break;
            }
            fileEntries[index] = entry with { ReplacementComplete = true };
        }

        foreach (SaveTransactionDocumentEntry entry in candidate.Journal.Documents)
        {
            await VerifyDestinationAsync(entry.RelativePath, entry.GeneratedSha256, cancellationToken).ConfigureAwait(false);
        }
        foreach (SaveTransactionFileEntry entry in fileEntries)
        {
            switch (entry.Kind)
            {
                case FileMutationKind.Create:
                case FileMutationKind.Replace:
                    await VerifyDestinationAsync(entry.DestinationRelativePath!, entry.GeneratedSha256!, cancellationToken).ConfigureAwait(false);
                    break;
                case FileMutationKind.Move:
                    await VerifyDestinationAsync(entry.DestinationRelativePath!, entry.OriginalSha256!, cancellationToken).ConfigureAwait(false);
                    break;
                case FileMutationKind.Delete:
                    if (File.Exists(_paths.ResolveRelative(entry.SourceRelativePath!))) throw new InvalidDataException("Deleted recovery destination still exists.");
                    break;
            }
        }

        SaveTransactionJournal completed = candidate.Journal with
        {
            State = SaveTransactionState.Committed,
            Documents = candidate.Journal.Documents.Select(entry => entry with { ReplacementComplete = true }).ToArray(),
            FileMutations = fileEntries
        };
        await completed.WriteAsync(Path.Combine(candidate.TransactionDirectory, "transaction.json"), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask CleanupAsync(SaveRecoveryCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        bool safe = candidate.Recommendation is RecoveryRecommendation.CleanupUncommitted or RecoveryRecommendation.CleanupCommitted
            || candidate.Journal.State == SaveTransactionState.RolledBack;
        if (!safe) throw new InvalidOperationException("This transaction is not safe to clean up automatically.");
        foreach (SaveTransactionDocumentEntry entry in candidate.Journal.Documents) { cancellationToken.ThrowIfCancellationRequested(); TryDelete(entry.TemporaryPath); }
        foreach (SaveTransactionFileEntry entry in candidate.Journal.Files) { cancellationToken.ThrowIfCancellationRequested(); if (entry.TemporaryPath is not null) TryDelete(entry.TemporaryPath); }
        Directory.Delete(candidate.TransactionDirectory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private async ValueTask RestoreBackupAsync(string backupPath, string expectedHash, string relativePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(backupPath)) throw new FileNotFoundException("Recovery backup is missing.", backupPath);
        string backupHash = await HashFileAsync(backupPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(backupHash, expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Recovery backup hash mismatch: " + relativePath);
        string destination = _paths.ResolveRelative(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Recovery destination has no directory."));
        string temporary = destination + ".playroma-rollback.tmp";
        File.Copy(backupPath, temporary, overwrite: true);
        File.Move(temporary, destination, overwrite: true);
        await VerifyDestinationAsync(relativePath, expectedHash, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask CompleteGeneratedAsync(string temporaryPath, string expectedHash, string relativePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(temporaryPath)) throw new FileNotFoundException("Generated recovery file is missing.", temporaryPath);
        string hash = await HashFileAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Generated recovery file hash mismatch: " + relativePath);
        string destination = _paths.ResolveRelative(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Recovery destination has no directory."));
        File.Move(temporaryPath, destination, overwrite: true);
    }

    private async ValueTask VerifyDestinationAsync(string relativePath, string expectedHash, CancellationToken cancellationToken)
    {
        string destination = _paths.ResolveRelative(relativePath);
        if (!File.Exists(destination)) throw new FileNotFoundException("Completed recovery destination is missing.", destination);
        string hash = await HashFileAsync(destination, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Completed recovery destination hash mismatch: " + relativePath);
    }

    private static RecoveryRecommendation DetermineRecommendation(SaveTransactionJournal journal)
    {
        IEnumerable<bool> completions = journal.Documents.Select(entry => entry.ReplacementComplete)
            .Concat(journal.Files.Select(entry => entry.ReplacementComplete));
        bool[] states = completions.ToArray();
        return journal.State switch
        {
            SaveTransactionState.Committed => RecoveryRecommendation.CleanupCommitted,
            SaveTransactionState.RolledBack => RecoveryRecommendation.CleanupUncommitted,
            SaveTransactionState.Created or SaveTransactionState.PreflightComplete => RecoveryRecommendation.CleanupUncommitted,
            SaveTransactionState.TemporaryFilesWritten or SaveTransactionState.TemporaryFilesVerified => RecoveryRecommendation.CleanupUncommitted,
            SaveTransactionState.BackupsCreated => RecoveryRecommendation.RollBack,
            SaveTransactionState.ReplacingFiles or SaveTransactionState.RecoveryRequired =>
                states.Length > 0 && states.All(value => value)
                    ? RecoveryRecommendation.CompleteCommit
                    : states.Any(value => value)
                        ? RecoveryRecommendation.ManualReview
                        : RecoveryRecommendation.RollBack,
            _ => RecoveryRecommendation.ManualReview
        };
    }

    private static async ValueTask<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
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
            string temporary = Path.Combine(directory, $".playroma-case-recovery-{Guid.NewGuid():N}.tmp");
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
        File.Move(source, destination, overwrite: false);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
