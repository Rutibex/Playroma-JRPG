using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Documents;

namespace Playroma.JRPG.Documents.Saving;

public enum ExternalChangePolicy
{
    Block,
    ExplicitOverwrite
}

public enum SaveVerificationLevel
{
    None,
    ParseOnly,
    Full
}

public enum SaveDisposition
{
    Unchanged,
    WriteModified,
    CopyUnchanged,
    Conflict,
    Invalid,
    Unsupported
}

public enum DocumentSaveStatus
{
    Saved,
    SkippedUnchanged,
    BlockedByExternalModification,
    FailedValidation,
    FailedWriting,
    Recovered
}

public enum FileMutationDisposition
{
    Ready,
    Conflict,
    Invalid,
    Unsupported
}

public enum FileMutationSaveStatus
{
    Created,
    Replaced,
    Moved,
    Deleted,
    BlockedByExternalModification,
    FailedValidation,
    FailedWriting,
    Recovered
}

public sealed record ProjectSaveOptions(
    bool CreateBackup = true,
    int BackupRetentionCount = 10,
    long MaximumBackupBytes = 2L * 1024 * 1024 * 1024,
    ExternalChangePolicy ExternalChangePolicy = ExternalChangePolicy.Block,
    SaveVerificationLevel VerificationLevel = SaveVerificationLevel.Full,
    bool VerifyAfterWrite = true);

public sealed record DocumentSavePlan(
    IMutableProjectDocument Document,
    string RelativePath,
    SaveDisposition Disposition,
    ContentFingerprint OriginalFingerprint,
    ContentFingerprint? CurrentDiskFingerprint,
    byte[] OutputBytes,
    string OutputSha256,
    IReadOnlyList<ProjectDiagnostic> Diagnostics);

public sealed record FileMutationSavePlan(
    PendingFileMutation Mutation,
    FileMutationDisposition Disposition,
    ContentFingerprint? CurrentSourceFingerprint,
    ContentFingerprint? CurrentDestinationFingerprint,
    string? OutputSha256,
    long OutputLength,
    IReadOnlyList<ProjectDiagnostic> Diagnostics)
{
    public Guid MutationId => Mutation.Id;
    public FileMutationKind Kind => Mutation.Kind;
    public string? SourceRelativePath => Mutation.SourceRelativePath;
    public string? DestinationRelativePath => Mutation.DestinationRelativePath;
    public string? StagedContentPath => Mutation.StagedContentPath;
    public string? GeneratedSha256 => OutputSha256;
    public string? ExpectedSourceSha256 => Mutation.ExpectedSourceFingerprint?.Sha256;
}

public sealed record ProjectSavePlan(
    Guid TransactionId,
    string ProjectRoot,
    IReadOnlyList<DocumentSavePlan> Documents,
    IReadOnlyList<ProjectDiagnostic> Diagnostics,
    IReadOnlyList<FileMutationSavePlan>? FileMutations = null)
{
    public IReadOnlyList<FileMutationSavePlan> Files => FileMutations ?? [];

    public bool CanSave => Documents.All(document => document.Disposition is SaveDisposition.Unchanged or SaveDisposition.WriteModified)
        && Files.All(file => file.Disposition == FileMutationDisposition.Ready)
        && Diagnostics.All(diagnostic => diagnostic.Severity is not DiagnosticSeverity.Error and not DiagnosticSeverity.Fatal);
}

public sealed record DocumentSaveResult(
    string RelativePath,
    DocumentSaveStatus Status,
    string? Sha256,
    IReadOnlyList<ProjectDiagnostic> Diagnostics);

public sealed record FileMutationSaveResult(
    Guid MutationId,
    FileMutationKind Kind,
    string? SourceRelativePath,
    string? DestinationRelativePath,
    FileMutationSaveStatus Status,
    string? Sha256,
    IReadOnlyList<ProjectDiagnostic> Diagnostics);

public sealed record ProjectSaveResult(
    Guid TransactionId,
    bool Succeeded,
    IReadOnlyList<DocumentSaveResult> Documents,
    IReadOnlyList<ProjectDiagnostic> Diagnostics,
    string? TransactionDirectory,
    IReadOnlyList<FileMutationSaveResult>? FileMutations = null)
{
    public IReadOnlyList<FileMutationSaveResult> Files => FileMutations ?? [];
}

public sealed record ExternalModificationConflict(
    string RelativePath,
    ContentFingerprint OpenedFingerprint,
    ContentFingerprint CurrentFingerprint);

public sealed record ProjectCopyOptions(
    bool IncludePlayromaMetadata = false,
    bool IncludeSaveGames = false,
    bool IncludeDeploymentExecutables = true,
    bool VerifyEveryCopiedFile = true);
