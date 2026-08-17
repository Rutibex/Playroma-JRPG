using System.Security.Cryptography;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.Documents.Saving;

public sealed class SavePlanBuilder
{
    private readonly string _projectRoot;
    private readonly ProjectPathPolicy _paths;
    private readonly IReadOnlyProjectFileSystem _fileSystem;
    private readonly FileFingerprintService _fingerprints;
    private readonly IDocumentMutationVerifier _verifier;

    public SavePlanBuilder(
        string projectRoot,
        IReadOnlyProjectFileSystem? fileSystem = null,
        IDocumentMutationVerifier? verifier = null)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        _paths = new ProjectPathPolicy(_projectRoot);
        _fileSystem = fileSystem ?? new PhysicalReadOnlyProjectFileSystem();
        _fingerprints = new FileFingerprintService(_fileSystem);
        _verifier = verifier ?? new DocumentMutationVerifier();
    }

    public ValueTask<ProjectSavePlan> BuildAsync(
        IEnumerable<IMutableProjectDocument> documents,
        ProjectSaveOptions options,
        CancellationToken cancellationToken = default) =>
        BuildAsync(documents, overlay: null, options, cancellationToken);

    public async ValueTask<ProjectSavePlan> BuildAsync(
        IEnumerable<IMutableProjectDocument> documents,
        IProjectFileOverlay? overlay,
        ProjectSaveOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        List<DocumentSavePlan> documentPlans = [];
        List<FileMutationSavePlan> filePlans = [];
        List<ProjectDiagnostic> allDiagnostics = [];
        foreach (IMutableProjectDocument document in documents.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentSavePlan plan = await BuildDocumentPlanAsync(document, options, cancellationToken).ConfigureAwait(false);
            documentPlans.Add(plan);
            allDiagnostics.AddRange(plan.Diagnostics);
        }

        if (overlay is not null)
        {
            foreach (PendingFileMutation mutation in overlay.PendingMutations.OrderBy(item => item.Description, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileMutationSavePlan plan = await BuildFilePlanAsync(mutation, options, cancellationToken).ConfigureAwait(false);
                filePlans.Add(plan);
                allDiagnostics.AddRange(plan.Diagnostics);
            }
        }

        ProjectDiagnostic? diskSpaceDiagnostic = CheckDiskSpace(documentPlans, filePlans);
        if (diskSpaceDiagnostic is not null) allDiagnostics.Add(diskSpaceDiagnostic);
        return new ProjectSavePlan(Guid.NewGuid(), _projectRoot, documentPlans, allDiagnostics, filePlans);
    }

    private async ValueTask<DocumentSavePlan> BuildDocumentPlanAsync(
        IMutableProjectDocument document,
        ProjectSaveOptions options,
        CancellationToken cancellationToken)
    {
        List<ProjectDiagnostic> diagnostics = [];
        if (document.State == MutableDocumentState.InvalidReadOnly)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.MutableDocumentReadOnly,
                "Invalid JSON documents cannot be saved through the structured editor.",
                new ProjectLocation(document.RelativePath)));
            return EmptyPlan(document, SaveDisposition.Invalid, diagnostics);
        }

        if (document.State == MutableDocumentState.Clean || document.Changes.Count == 0)
        {
            return EmptyPlan(document, SaveDisposition.Unchanged, diagnostics);
        }

        ReadOnlyMemory<byte> generatedOutput = await document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
        if (generatedOutput.Span.SequenceEqual(document.GetOriginalBytes().Span))
        {
            document.DiscardChanges();
            return EmptyPlan(document, SaveDisposition.Unchanged, diagnostics);
        }

        string fullPath = _paths.ResolveRelative(document.RelativePath);
        ContentFingerprint current;
        try
        {
            ReadFileSnapshot currentSnapshot = await _fingerprints.ReadAsync(fullPath, calculateSha256: true, cancellationToken).ConfigureAwait(false);
            current = currentSnapshot.Fingerprint;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.FileReadFailure,
                "The current project file could not be read before saving.",
                new ProjectLocation(document.RelativePath),
                exception.Message));
            return EmptyPlan(document, SaveDisposition.Conflict, diagnostics);
        }

        bool changedExternally = !StronglyEqual(document.OriginalFingerprint, current);
        if (changedExternally && options.ExternalChangePolicy == ExternalChangePolicy.Block)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.FileChangedExternally,
                "The file changed after Playroma opened it. Saving was blocked to protect the external changes.",
                new ProjectLocation(document.RelativePath)));
            return new DocumentSavePlan(
                document,
                document.RelativePath,
                SaveDisposition.Conflict,
                document.OriginalFingerprint,
                current,
                [],
                string.Empty,
                diagnostics);
        }

        byte[] outputBytes = generatedOutput.ToArray();
        if (options.VerificationLevel != SaveVerificationLevel.None && LooksLikeJson(document.RelativePath))
        {
            try
            {
                using System.Text.Json.JsonDocument ignored = System.Text.Json.JsonDocument.Parse(Playroma.JRPG.Documents.Json.Utf8JsonInput.ToBomlessArray(outputBytes));
            }
            catch (System.Text.Json.JsonException exception)
            {
                diagnostics.Add(ProjectDiagnostic.Error(
                    DiagnosticCodes.GeneratedJsonInvalid,
                    "Generated output is not valid JSON.",
                    new ProjectLocation(document.RelativePath),
                    exception.Message));
            }
        }

        if (options.VerificationLevel == SaveVerificationLevel.Full)
        {
            if (LooksLikeJson(document.RelativePath))
            {
                MutationVerificationResult verification = _verifier.Verify(
                    document.GetOriginalBytes().Span,
                    outputBytes,
                    document.Changes,
                    document.RelativePath);
                diagnostics.AddRange(verification.Diagnostics);
            }
            else if (document is Playroma.JRPG.RpgMakerMV.Documents.MvPluginManifestDocument pluginManifest)
            {
                ReadOnlyMemory<byte> pluginArray = await pluginManifest.GenerateArrayBytesAsync(cancellationToken).ConfigureAwait(false);
                MutationVerificationResult verification = _verifier.Verify(
                    pluginManifest.GetArrayOriginalBytes().Span,
                    pluginArray.Span,
                    document.Changes,
                    document.RelativePath + "#$plugins");
                diagnostics.AddRange(verification.Diagnostics);
            }
        }

        string hash = Convert.ToHexString(SHA256.HashData(outputBytes)).ToLowerInvariant();
        SaveDisposition disposition = diagnostics.Any(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
            ? SaveDisposition.Invalid
            : SaveDisposition.WriteModified;
        return new DocumentSavePlan(
            document,
            document.RelativePath,
            disposition,
            document.OriginalFingerprint,
            current,
            outputBytes,
            hash,
            diagnostics);
    }

    private async ValueTask<FileMutationSavePlan> BuildFilePlanAsync(
        PendingFileMutation mutation,
        ProjectSaveOptions options,
        CancellationToken cancellationToken)
    {
        List<ProjectDiagnostic> diagnostics = [];
        ContentFingerprint? currentSource = null;
        ContentFingerprint? currentDestination = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(mutation.SourceRelativePath))
            {
                currentSource = await TryFingerprintAsync(_paths.ResolveRelative(mutation.SourceRelativePath), cancellationToken).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(mutation.DestinationRelativePath))
            {
                currentDestination = await TryFingerprintAsync(_paths.ResolveRelative(mutation.DestinationRelativePath), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.Phase5AssetOperationConflict,
                "An asset path could not be verified before saving.",
                new ProjectLocation(mutation.SourceRelativePath ?? mutation.DestinationRelativePath),
                exception.Message));
        }

        switch (mutation.Kind)
        {
            case FileMutationKind.Create:
                if (currentDestination is not null)
                {
                    diagnostics.Add(Conflict(mutation.DestinationRelativePath, "The import destination already exists."));
                }
                ValidateStagedContent(mutation, diagnostics);
                break;
            case FileMutationKind.Replace:
                if (currentDestination is null)
                {
                    diagnostics.Add(Conflict(mutation.DestinationRelativePath, "The replacement destination no longer exists."));
                }
                else if (mutation.ExpectedDestinationFingerprint is not null
                         && !StronglyEqual(mutation.ExpectedDestinationFingerprint, currentDestination)
                         && options.ExternalChangePolicy == ExternalChangePolicy.Block)
                {
                    diagnostics.Add(Conflict(mutation.DestinationRelativePath, "The asset changed after the replacement was staged."));
                }
                ValidateStagedContent(mutation, diagnostics);
                break;
            case FileMutationKind.Move:
                if (currentSource is null)
                {
                    diagnostics.Add(Conflict(mutation.SourceRelativePath, "The asset to rename or move no longer exists."));
                }
                else if (mutation.ExpectedSourceFingerprint is not null
                         && !StronglyEqual(mutation.ExpectedSourceFingerprint, currentSource)
                         && options.ExternalChangePolicy == ExternalChangePolicy.Block)
                {
                    diagnostics.Add(Conflict(mutation.SourceRelativePath, "The asset changed after the move was staged."));
                }
                bool samePhysicalPath = PathsReferToSameLocation(mutation.SourceRelativePath, mutation.DestinationRelativePath);
                if (currentDestination is not null && !samePhysicalPath)
                {
                    diagnostics.Add(Conflict(mutation.DestinationRelativePath, "The rename or move destination already exists."));
                }
                break;
            case FileMutationKind.Delete:
                if (currentSource is null)
                {
                    diagnostics.Add(Conflict(mutation.SourceRelativePath, "The asset to delete no longer exists."));
                }
                else if (mutation.ExpectedSourceFingerprint is not null
                         && !StronglyEqual(mutation.ExpectedSourceFingerprint, currentSource)
                         && options.ExternalChangePolicy == ExternalChangePolicy.Block)
                {
                    diagnostics.Add(Conflict(mutation.SourceRelativePath, "The asset changed after deletion was staged."));
                }
                break;
            default:
                diagnostics.Add(ProjectDiagnostic.Error(
                    DiagnosticCodes.Phase5AssetOperationConflict,
                    "The staged file operation is not supported.",
                    new ProjectLocation(mutation.SourceRelativePath ?? mutation.DestinationRelativePath)));
                break;
        }

        FileMutationDisposition disposition = diagnostics.Any(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
            ? FileMutationDisposition.Conflict
            : FileMutationDisposition.Ready;
        return new FileMutationSavePlan(
            mutation,
            disposition,
            currentSource,
            currentDestination,
            mutation.Kind is FileMutationKind.Create or FileMutationKind.Replace ? mutation.StagedSha256 : currentSource?.Sha256,
            mutation.Kind is FileMutationKind.Create or FileMutationKind.Replace ? mutation.StagedLength : currentSource?.Length ?? 0,
            diagnostics);
    }

    private static void ValidateStagedContent(PendingFileMutation mutation, ICollection<ProjectDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(mutation.StagedContentPath) || !File.Exists(mutation.StagedContentPath))
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.Phase5StagedContentMissing,
                "The staged asset content is missing.",
                new ProjectLocation(mutation.DestinationRelativePath)));
            return;
        }
        FileInfo info = new(mutation.StagedContentPath);
        if (info.Length == 0)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.Phase5ZeroByteAsset,
                "The staged asset is empty.",
                new ProjectLocation(mutation.DestinationRelativePath)));
        }
        if (mutation.StagedLength != info.Length)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.Phase5AssetHashMismatch,
                "The staged asset length changed after it was imported.",
                new ProjectLocation(mutation.DestinationRelativePath)));
        }
    }

    private ProjectDiagnostic? CheckDiskSpace(
        IReadOnlyCollection<DocumentSavePlan> documentPlans,
        IReadOnlyCollection<FileMutationSavePlan> filePlans)
    {
        long required = documentPlans.Where(plan => plan.Disposition == SaveDisposition.WriteModified).Sum(plan => (long)plan.OutputBytes.Length * 2)
            + filePlans.Where(plan => plan.Disposition == FileMutationDisposition.Ready).Sum(plan => plan.OutputLength * 2);
        if (required <= 0) return null;
        try
        {
            string root = Path.GetPathRoot(_projectRoot) ?? _projectRoot;
            DriveInfo drive = new(root);
            if (drive.AvailableFreeSpace >= required) return null;
            return ProjectDiagnostic.Error(
                DiagnosticCodes.InsufficientDiskSpace,
                "There may not be enough free disk space to stage and back up the save transaction.",
                technicalDetail: $"Required estimate: {required:N0} bytes; available: {drive.AvailableFreeSpace:N0} bytes.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ProjectDiagnostic.Information(
                DiagnosticCodes.InsufficientDiskSpace,
                "Available disk space could not be checked before saving.",
                technicalDetail: exception.Message);
        }
    }

    private async ValueTask<ContentFingerprint?> TryFingerprintAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath)) return null;
        ReadFileSnapshot snapshot = await _fingerprints.ReadAsync(fullPath, calculateSha256: true, cancellationToken).ConfigureAwait(false);
        return snapshot.Fingerprint;
    }

    private static ProjectDiagnostic Conflict(string? path, string message) => ProjectDiagnostic.Error(
        DiagnosticCodes.Phase5AssetOperationConflict,
        message,
        new ProjectLocation(path));

    private bool PathsReferToSameLocation(string? source, string? destination)
    {
        if (source is null || destination is null) return false;
        string sourceFull = _paths.ResolveRelative(source);
        string destinationFull = _paths.ResolveRelative(destination);
        return string.Equals(sourceFull, destinationFull, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static DocumentSavePlan EmptyPlan(
        IMutableProjectDocument document,
        SaveDisposition disposition,
        IReadOnlyList<ProjectDiagnostic> diagnostics) =>
        new(document, document.RelativePath, disposition, document.OriginalFingerprint, document.CurrentDiskFingerprint,
            [], document.OriginalFingerprint.Sha256 ?? string.Empty, diagnostics);

    private static bool LooksLikeJson(string relativePath) => relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static bool StronglyEqual(ContentFingerprint left, ContentFingerprint right) =>
        left.Length == right.Length
        && !string.IsNullOrWhiteSpace(left.Sha256)
        && string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase);
}
