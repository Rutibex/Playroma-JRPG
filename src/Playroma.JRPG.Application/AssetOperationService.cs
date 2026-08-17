using Playroma.JRPG.Assets;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Indexing;

namespace Playroma.JRPG.Application;

public sealed record AssetManagementPreview(
    string Description,
    TransactionPreview Transaction,
    IReadOnlyList<AssetReferenceHint> ExactReferences,
    IReadOnlyList<AssetReferenceHint> HeuristicReferences,
    IReadOnlyList<string> FileChanges);

public enum AssetImportConflictAction
{
    Import,
    Replace,
    Skip
}

/// <summary>
/// One fully resolved item in a batch import. The UI may perform friendly conflict resolution,
/// but the application layer revalidates every item immediately before preview and commit.
/// </summary>
public sealed record AssetImportPlanItem(
    string SourceFile,
    AssetKey Destination,
    AssetImportConflictAction ConflictAction = AssetImportConflictAction.Import,
    string? ExistingRelativePath = null);

public sealed record AssetImportPlan(
    IReadOnlyList<AssetImportPlanItem> Items,
    string Description = "Import assets")
{
    public IReadOnlyList<AssetImportPlanItem> EffectiveItems => Items ?? [];
}

public sealed class AssetOperationService
{
    private readonly MvProjectSession _session;

    public AssetOperationService(MvProjectSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public async ValueTask<AssetManagementPreview> PreviewImportAsync(
        string sourceFile,
        AssetKey destination,
        CancellationToken cancellationToken = default)
    {
        AssetKey validatedDestination = ValidateDestination(destination);
        string extension = ValidateImportSource(sourceFile, validatedDestination.Category);
        string destinationPath = BuildProjectRelativePath(validatedDestination, extension);
        EnsureDestinationAvailable(destinationPath);
        await using ProjectTransaction transaction = _session.BeginTransaction($"Import asset {validatedDestination}");
        transaction.Add(new ImportAssetFileCommand(sourceFile, destinationPath));
        TransactionPreview preview = await transaction.PreviewAsync(cancellationToken).ConfigureAwait(false);
        return new AssetManagementPreview(transaction.Description, preview, [], [], [$"Create {destinationPath}"]);
    }

    public async ValueTask ImportAsync(
        string sourceFile,
        AssetKey destination,
        CancellationToken cancellationToken = default)
    {
        AssetKey validatedDestination = ValidateDestination(destination);
        string extension = ValidateImportSource(sourceFile, validatedDestination.Category);
        string destinationPath = BuildProjectRelativePath(validatedDestination, extension);
        EnsureDestinationAvailable(destinationPath);
        await using ProjectTransaction transaction = _session.BeginTransaction($"Import asset {validatedDestination}");
        transaction.Add(new ImportAssetFileCommand(sourceFile, destinationPath));
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await _session.RefreshAssetCatalogueAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AssetManagementPreview> PreviewBatchImportAsync(
        AssetImportPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        (ProjectTransaction transaction, IReadOnlyList<string> fileChanges) = BuildBatchImportTransaction(plan);
        await using (transaction)
        {
            TransactionPreview preview = await transaction.PreviewAsync(cancellationToken).ConfigureAwait(false);
            return new AssetManagementPreview(transaction.Description, preview, [], [], fileChanges);
        }
    }

    public async ValueTask ImportBatchAsync(
        AssetImportPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        (ProjectTransaction transaction, _) = BuildBatchImportTransaction(plan);
        await using (transaction)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        await _session.RefreshAssetCatalogueAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AssetManagementPreview> PreviewReplaceAsync(
        AssetVariant existing,
        string replacementFile,
        CancellationToken cancellationToken = default)
    {
        ValidateReplacementSource(replacementFile, existing);
        await using ProjectTransaction transaction = _session.BeginTransaction($"Replace asset {existing.RelativePath}");
        transaction.Add(new ReplaceAssetFileCommand(replacementFile, existing.RelativePath));
        TransactionPreview preview = await transaction.PreviewAsync(cancellationToken).ConfigureAwait(false);
        return new AssetManagementPreview(transaction.Description, preview, [], [], [$"Replace {existing.RelativePath}"]);
    }

    public async ValueTask ReplaceAsync(
        AssetVariant existing,
        string replacementFile,
        CancellationToken cancellationToken = default)
    {
        ValidateReplacementSource(replacementFile, existing);
        await using ProjectTransaction transaction = _session.BeginTransaction($"Replace asset {existing.RelativePath}");
        transaction.Add(new ReplaceAssetFileCommand(replacementFile, existing.RelativePath));
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await _session.RefreshAssetCatalogueAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AssetManagementPreview> PreviewRenameAsync(
        AssetKey source,
        string newLogicalName,
        CancellationToken cancellationToken = default)
    {
        AssetRecord record = _session.Assets.Find(source) ?? throw new KeyNotFoundException($"Asset '{source}' was not found.");
        AssetKey destination = new(source.Category, ValidateLogicalName(newLogicalName));
        (ProjectTransaction transaction, IReadOnlyList<AssetReferenceHint> exact, IReadOnlyList<AssetReferenceHint> heuristic, IReadOnlyList<string> files) = BuildRenameTransaction(record, destination);
        await using (transaction)
        {
            TransactionPreview preview = await transaction.PreviewAsync(cancellationToken).ConfigureAwait(false);
            return new AssetManagementPreview(transaction.Description, preview, exact, heuristic, files);
        }
    }

    public async ValueTask RenameAsync(
        AssetKey source,
        string newLogicalName,
        CancellationToken cancellationToken = default)
    {
        AssetRecord record = _session.Assets.Find(source) ?? throw new KeyNotFoundException($"Asset '{source}' was not found.");
        AssetKey destination = new(source.Category, ValidateLogicalName(newLogicalName));
        (ProjectTransaction transaction, _, _, _) = BuildRenameTransaction(record, destination);
        await using (transaction)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        await _session.RefreshReferencesAsync(cancellationToken).ConfigureAwait(false);
        await _session.RefreshAssetCatalogueAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AssetManagementPreview> PreviewDeleteAsync(
        AssetKey source,
        bool allowReferenced = false,
        CancellationToken cancellationToken = default)
    {
        AssetRecord record = _session.Assets.Find(source) ?? throw new KeyNotFoundException($"Asset '{source}' was not found.");
        if (!allowReferenced && record.Usage.ExactReferenceCount > 0)
        {
            throw new InvalidOperationException("The asset has exact references. Replace or clear those references before deleting it.");
        }
        if (record.Variants.Count == 0) throw new InvalidOperationException("The missing logical asset has no physical file to delete.");
        ProjectTransaction transaction = _session.BeginTransaction($"Delete asset {source}");
        foreach (AssetVariant variant in record.Variants.Where(variant => variant.Status != AssetStatus.PendingDeletion)) transaction.Add(new DeleteAssetFileCommand(variant.RelativePath));
        await using (transaction)
        {
            TransactionPreview preview = await transaction.PreviewAsync(cancellationToken).ConfigureAwait(false);
            return new AssetManagementPreview(
                transaction.Description,
                preview,
                record.Usage.References.Where(reference => reference.IsExact).ToArray(),
                record.Usage.References.Where(reference => !reference.IsExact).ToArray(),
                record.Variants.Select(variant => $"Delete {variant.RelativePath}").ToArray());
        }
    }

    public async ValueTask DeleteAsync(
        AssetKey source,
        bool allowReferenced = false,
        CancellationToken cancellationToken = default)
    {
        AssetRecord record = _session.Assets.Find(source) ?? throw new KeyNotFoundException($"Asset '{source}' was not found.");
        if (!allowReferenced && record.Usage.ExactReferenceCount > 0)
        {
            throw new InvalidOperationException("The asset has exact references. Replace or clear those references before deleting it.");
        }
        if (record.Variants.Count == 0) throw new InvalidOperationException("The missing logical asset has no physical file to delete.");
        await using ProjectTransaction transaction = _session.BeginTransaction($"Delete asset {source}");
        foreach (AssetVariant variant in record.Variants.Where(variant => variant.Status != AssetStatus.PendingDeletion)) transaction.Add(new DeleteAssetFileCommand(variant.RelativePath));
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await _session.RefreshAssetCatalogueAsync(cancellationToken).ConfigureAwait(false);
    }

    private (ProjectTransaction Transaction, IReadOnlyList<AssetReferenceHint> Exact, IReadOnlyList<AssetReferenceHint> Heuristic, IReadOnlyList<string> Files)
        BuildRenameTransaction(AssetRecord record, AssetKey destination)
    {
        ProjectTransaction transaction = _session.BeginTransaction($"Rename asset {record.Key} to {destination.LogicalName}");
        List<string> fileChanges = [];
        foreach (AssetVariant variant in record.Variants)
        {
            string destinationPath = ReplaceLogicalPath(variant.RelativePath, destination, variant.Extension);
            if (!PathsEqual(variant.RelativePath, destinationPath) && _session.Editing.Files.Exists(destinationPath))
            {
                throw new IOException($"The rename destination already exists: {destinationPath}");
            }
            transaction.Add(new MoveAssetFileCommand(variant.RelativePath, destinationPath));
            fileChanges.Add($"Move {variant.RelativePath} -> {destinationPath}");
        }

        AssetReferenceHint[] exact = record.Usage.References.Where(reference => reference.IsExact).ToArray();
        AssetReferenceHint[] heuristic = record.Usage.References.Where(reference => !reference.IsExact).ToArray();
        foreach (AssetReferenceHint reference in exact)
        {
            transaction.Add(new SetJsonStringCommand(
                reference.SourceRelativePath,
                new JsonPath(reference.SourceLocation),
                destination.LogicalName,
                $"Update asset reference in {reference.SourceRelativePath}"));
        }
        return (transaction, exact, heuristic, fileChanges);
    }

    private (ProjectTransaction Transaction, IReadOnlyList<string> FileChanges) BuildBatchImportTransaction(AssetImportPlan plan)
    {
        AssetImportPlanItem[] items = plan.EffectiveItems
            .Where(item => item.ConflictAction != AssetImportConflictAction.Skip)
            .ToArray();
        if (items.Length == 0) throw new InvalidOperationException("The batch import contains no files to stage.");

        List<(AssetImportPlanItem Item, string OperationPath)> resolved = [];
        HashSet<string> destinations = new(PathComparer);
        foreach (AssetImportPlanItem item in items)
        {
            AssetKey validatedDestination = ValidateDestination(item.Destination);
            AssetImportPlanItem validatedItem = item with { Destination = validatedDestination };
            string extension = ValidateImportSource(validatedItem.SourceFile, validatedDestination.Category);
            string operationPath;
            switch (validatedItem.ConflictAction)
            {
                case AssetImportConflictAction.Import:
                    operationPath = BuildProjectRelativePath(validatedDestination, extension);
                    EnsureDestinationAvailable(operationPath);
                    break;

                case AssetImportConflictAction.Replace:
                    operationPath = ResolveExistingReplacementPath(validatedItem, extension);
                    ValidateReplacementSource(validatedItem.SourceFile, new AssetVariant(
                        operationPath,
                        Path.GetExtension(operationPath),
                        0,
                        DateTimeOffset.MinValue,
                        null,
                        AssetCategoryRegistry.IsEncryptedExtension(Path.GetExtension(operationPath))));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(validatedItem.ConflictAction), validatedItem.ConflictAction, "Unsupported import action.");
            }

            if (!destinations.Add(operationPath))
            {
                throw new IOException($"More than one import item resolves to '{operationPath}'. Choose unique destinations before importing.");
            }
            resolved.Add((validatedItem, operationPath));
        }

        string description = string.IsNullOrWhiteSpace(plan.Description)
            ? $"Import {resolved.Count:N0} asset(s)"
            : plan.Description.Trim();
        ProjectTransaction transaction = _session.BeginTransaction(description);
        List<string> fileChanges = [];
        foreach ((AssetImportPlanItem item, string operationPath) in resolved)
        {
            if (item.ConflictAction == AssetImportConflictAction.Replace)
            {
                transaction.Add(new ReplaceAssetFileCommand(item.SourceFile, operationPath));
                fileChanges.Add($"Replace {operationPath}");
            }
            else
            {
                transaction.Add(new ImportAssetFileCommand(item.SourceFile, operationPath));
                fileChanges.Add($"Create {operationPath}");
            }
        }
        return (transaction, fileChanges);
    }

    private string ResolveExistingReplacementPath(AssetImportPlanItem item, string extension)
    {
        AssetRecord record = _session.Assets.Find(item.Destination)
            ?? throw new InvalidOperationException($"No existing asset was found for replacement at '{item.Destination}'.");
        if (!string.IsNullOrWhiteSpace(item.ExistingRelativePath))
        {
            string normalized = item.ExistingRelativePath.Replace('\\', '/');
            AssetVariant? selected = record.Variants.FirstOrDefault(candidate =>
                candidate.NormalizedRelativePath.Equals(normalized, PathComparison)
                && candidate.Status != AssetStatus.PendingDeletion);
            if (selected is null || !_session.Editing.Files.Exists(normalized))
            {
                throw new FileNotFoundException("The selected replacement target is not a current variant of the destination asset.", normalized);
            }
            if (!selected.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The replacement extension '{extension}' does not match '{selected.Extension}'.");
            }
            return normalized;
        }

        AssetVariant? variant = record.Variants.FirstOrDefault(candidate =>
            candidate.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase)
            && candidate.Status != AssetStatus.PendingDeletion);
        return variant?.RelativePath
            ?? throw new InvalidOperationException($"The asset '{item.Destination}' has no '{extension}' variant to replace.");
    }

    private static AssetKey ValidateDestination(AssetKey destination)
    {
        if (destination.Category == AssetCategory.Unknown
            || !AssetCategoryRegistry.Directories.ContainsKey(destination.Category))
        {
            throw new InvalidDataException("An import destination must use a concrete asset category.");
        }
        return new AssetKey(destination.Category, ValidateLogicalName(destination.LogicalName));
    }

    private string BuildProjectRelativePath(AssetKey key, string extension)
    {
        string runtimeRelative = AssetCategoryRegistry.BuildRuntimeRelativePath(key, extension);
        string prefix = RuntimePrefix();
        return string.IsNullOrEmpty(prefix) ? runtimeRelative : prefix + "/" + runtimeRelative;
    }

    private string ReplaceLogicalPath(string existingRelativePath, AssetKey destination, string extension)
    {
        string normalized = existingRelativePath.Replace('\\', '/');
        string categoryDirectory = AssetCategoryRegistry.Directories[destination.Category];
        int categoryIndex = normalized.IndexOf(categoryDirectory + "/", StringComparison.OrdinalIgnoreCase);
        string prefix = categoryIndex <= 0 ? string.Empty : normalized[..categoryIndex].TrimEnd('/');
        string replacement = AssetCategoryRegistry.BuildRuntimeRelativePath(destination, extension);
        return string.IsNullOrEmpty(prefix) ? replacement : prefix + "/" + replacement;
    }

    private string RuntimePrefix()
    {
        string projectRoot = _session.Workspace.Snapshot.Identity.ProjectRoot;
        string runtimeRoot = _session.Workspace.Snapshot.Identity.RuntimeRoot ?? projectRoot;
        string relative = Path.GetRelativePath(projectRoot, runtimeRoot).Replace('\\', '/').Trim('/');
        return relative == "." ? string.Empty : relative;
    }

    private string ValidateImportSource(string sourceFile, AssetCategory destinationCategory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        string fullPath = Path.GetFullPath(sourceFile);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The imported asset does not exist.", fullPath);
        if (new FileInfo(fullPath).Length == 0) throw new InvalidDataException("The imported asset is empty.");
        string extension = Path.GetExtension(fullPath);
        if (destinationCategory == AssetCategory.Unknown)
        {
            throw new InvalidDataException("An import destination must use a concrete asset category.");
        }
        if (AssetCategoryRegistry.IsEncryptedExtension(extension))
        {
            throw new InvalidDataException("Encrypted deployment assets cannot be imported as editable project sources.");
        }
        AssetMediaKind actual = AssetCategoryRegistry.ResolveMediaKind(extension);
        AssetMediaKind expected = ExpectedMediaKind(destinationCategory);
        if (actual == AssetMediaKind.Unknown)
        {
            throw new InvalidDataException($"The file extension '{extension}' is not a supported MV asset format.");
        }
        if (expected != AssetMediaKind.Unknown && actual != expected)
        {
            throw new InvalidDataException($"A {actual} file cannot be imported into the {destinationCategory} category, which expects {expected} assets.");
        }
        return extension;
    }

    private static void ValidateReplacementSource(string replacementFile, AssetVariant existing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementFile);
        string fullPath = Path.GetFullPath(replacementFile);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The replacement asset does not exist.", fullPath);
        if (new FileInfo(fullPath).Length == 0) throw new InvalidDataException("The replacement asset is empty.");
        string actualExtension = Path.GetExtension(fullPath);
        if (!actualExtension.Equals(existing.Extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The replacement extension '{actualExtension}' does not match the selected variant extension '{existing.Extension}'. Add it as a separate variant instead.");
        }
    }

    private void EnsureDestinationAvailable(string destinationPath)
    {
        if (_session.Editing.Files.Exists(destinationPath))
        {
            throw new IOException($"An asset already exists at '{destinationPath}'. Use Replace or choose another logical name.");
        }
    }

    private static string ValidateLogicalName(string logicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        if (Path.IsPathRooted(logicalName)) throw new ArgumentException("A logical asset name must be relative.", nameof(logicalName));
        string normalized = AssetKey.NormalizeLogicalName(logicalName);
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("A logical asset name may not be empty or contain traversal segments.", nameof(logicalName));
        }
        const string portableInvalid = "<>:\"|?*";
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        foreach (string segment in segments)
        {
            if (segment.Any(character => character < 32 || portableInvalid.Contains(character)))
            {
                throw new ArgumentException($"The asset name segment '{segment}' contains a filename character that is not portable.", nameof(logicalName));
            }
            if (segment.EndsWith(' ') || segment.EndsWith('.'))
            {
                throw new ArgumentException($"The asset name segment '{segment}' may not end in a space or period.", nameof(logicalName));
            }
            string stem = segment.Split('.')[0];
            if (reserved.Contains(stem, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"The asset name segment '{segment}' is reserved by Windows.", nameof(logicalName));
            }
        }
        return string.Join('/', segments);
    }

    private static AssetMediaKind ExpectedMediaKind(AssetCategory category) => category switch
    {
        AssetCategory.BackgroundMusic or AssetCategory.BackgroundSounds or AssetCategory.MusicEffects or AssetCategory.SoundEffects => AssetMediaKind.Audio,
        AssetCategory.Fonts => AssetMediaKind.Font,
        AssetCategory.Movies => AssetMediaKind.Movie,
        AssetCategory.Unknown => AssetMediaKind.Unknown,
        _ => AssetMediaKind.Image
    };

    private static bool PathsEqual(string left, string right) => string.Equals(
        left.Replace('\\', '/'),
        right.Replace('\\', '/'),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

}
