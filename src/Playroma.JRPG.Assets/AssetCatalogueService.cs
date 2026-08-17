using System.Security.Cryptography;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Assets;

public interface IAssetCatalogueService : IAsyncDisposable
{
    IReadOnlyList<AssetRecord> Assets { get; }
    AssetCatalogueStatistics Statistics { get; }
    AssetRecord? Find(AssetKey key);
    IReadOnlyList<AssetRecord> Search(AssetQuery query);
    ValueTask BuildAsync(IProgress<AssetCatalogueProgress>? progress = null, CancellationToken cancellationToken = default);
    ValueTask RefreshPathsAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default);
    ValueTask<AssetRecord> EnsureMetadataAsync(AssetKey key, CancellationToken cancellationToken = default);
    ValueTask<string> EnsureHashAsync(AssetVariant variant, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<IReadOnlyList<AssetVariant>>> FindExactDuplicatesAsync(CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ProjectDiagnostic>> ValidateAllAsync(IProgress<AssetCatalogueProgress>? progress = null, CancellationToken cancellationToken = default);
    void ApplyPendingMutations(IEnumerable<AssetPendingMutation> mutations);
    event EventHandler? Changed;
}

public sealed class AssetCatalogueService : IAssetCatalogueService
{
    private readonly string _projectRoot;
    private readonly string _runtimePrefix;
    private readonly AssetInventory _initialInventory;
    private readonly AssetCache _cache;
    private readonly IAssetContentSource _content;
    private readonly object _sync = new();
    private readonly Dictionary<AssetKey, AssetRecord> _records = new(AssetKeyComparer.Instance);
    private readonly HashSet<string> _duplicatePaths = new(PathComparer);
    private IReadOnlyList<AssetReferenceHint> _references;
    private bool _disposed;
    private DateTimeOffset _builtUtc;

    public AssetCatalogueService(
        string projectRoot,
        AssetInventory initialInventory,
        string runtimeRelativePrefix = "",
        IEnumerable<AssetReferenceHint>? references = null,
        AssetCacheOptions? cacheOptions = null,
        IAssetContentSource? contentSource = null)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        _initialInventory = initialInventory ?? throw new ArgumentNullException(nameof(initialInventory));
        _runtimePrefix = NormalizePrefix(runtimeRelativePrefix);
        _references = references?.ToArray() ?? [];
        _cache = new AssetCache(_projectRoot, cacheOptions);
        _content = contentSource ?? new PhysicalAssetContentSource(_projectRoot);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<AssetRecord> Assets
    {
        get { lock (_sync) return _records.Values.OrderBy(record => record.Key.Category).ThenBy(record => record.Key.LogicalName, StringComparer.OrdinalIgnoreCase).ToArray(); }
    }

    public AssetCatalogueStatistics Statistics
    {
        get
        {
            IReadOnlyList<AssetRecord> assets = Assets;
            return new AssetCatalogueStatistics(
                assets.Count,
                assets.Sum(asset => asset.Variants.Count),
                assets.Count(asset => asset.IsMissing),
                assets.Sum(asset => asset.Variants.Count(variant => variant.IsEncrypted)),
                assets.Sum(asset => asset.Usage.ExactReferenceCount),
                assets.Sum(asset => asset.Usage.HeuristicReferenceCount),
                assets.Sum(asset => asset.TotalBytes),
                _builtUtc);
        }
    }

    public void ReplaceReferences(IEnumerable<AssetReferenceHint> references)
    {
        _references = references?.ToArray() ?? [];
        ReattachUsage();
    }

    public AssetRecord? Find(AssetKey key)
    {
        lock (_sync) return _records.TryGetValue(key, out AssetRecord? record) ? record : null;
    }

    public IReadOnlyList<AssetRecord> Search(AssetQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        HashSet<string> duplicatePaths;
        lock (_sync) duplicatePaths = new HashSet<string>(_duplicatePaths, PathComparer);
        return Assets.Where(asset =>
                (!query.Category.HasValue || asset.Key.Category == query.Category.Value)
                && (!query.MediaKind.HasValue || asset.MediaKind == query.MediaKind.Value)
                && (!query.Used.HasValue || asset.Usage.HasKnownUse == query.Used.Value)
                && (!query.Missing.HasValue || asset.IsMissing == query.Missing.Value)
                && (!query.Encrypted.HasValue || asset.Variants.Any(variant => variant.IsEncrypted) == query.Encrypted.Value)
                && (!query.Status.HasValue || asset.Status == query.Status.Value)
                && (string.IsNullOrWhiteSpace(query.Extension) || asset.Variants.Any(variant => variant.Extension.TrimStart('.').Equals(query.Extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase)))
                && (!query.Duplicate.HasValue || asset.Variants.Any(variant => duplicatePaths.Contains(variant.NormalizedRelativePath)) == query.Duplicate.Value)
                && (string.IsNullOrWhiteSpace(query.Text)
                    || asset.Key.LogicalName.Contains(query.Text, StringComparison.OrdinalIgnoreCase)
                    || asset.Variants.Any(variant => variant.RelativePath.Contains(query.Text, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
    }

    public async ValueTask BuildAsync(IProgress<AssetCatalogueProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        List<AssetEntry> entries = await DiscoverCurrentEntriesAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<AssetKey, List<AssetVariant>> grouped = new(AssetKeyComparer.Instance);
        for (int index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssetEntry entry = entries[index];
            progress?.Report(new AssetCatalogueProgress(index, entries.Count, entry.RelativePath, "Discovering assets"));
            string runtimeRelative = StripRuntimePrefix(entry.RelativePath);
            if (!AssetCategoryRegistry.TryCreateKey(runtimeRelative, entry.Category, out AssetKey key)) continue;
            if (!grouped.TryGetValue(key, out List<AssetVariant>? variants)) grouped[key] = variants = [];
            variants.Add(new AssetVariant(entry.RelativePath, entry.Extension, entry.Size, entry.LastWriteTimeUtc, entry.Sha256, entry.IsEncrypted, entry.IsEncrypted ? AssetStatus.Encrypted : AssetStatus.Available));
        }

        Dictionary<AssetKey, AssetRecord> replacement = new(AssetKeyComparer.Instance);
        foreach ((AssetKey key, List<AssetVariant> variants) in grouped)
        {
            AssetMediaKind media = variants.Select(variant => AssetCategoryRegistry.ResolveMediaKind(variant.Extension)).FirstOrDefault(kind => kind != AssetMediaKind.Unknown);
            List<ProjectDiagnostic> diagnostics = [];
            AssetStatus status = variants.Any(variant => variant.IsEncrypted) ? AssetStatus.Encrypted : AssetStatus.Available;
            string[] cases = variants.Select(variant => variant.RelativePath).Distinct(StringComparer.Ordinal).ToArray();
            if (variants.Any(variant => variant.Size == 0))
            {
                status = AssetStatus.Invalid;
                diagnostics.Add(ProjectDiagnostic.Error(DiagnosticCodes.Phase5ZeroByteAsset, "The asset contains an empty physical file.", new ProjectLocation(cases[0])));
            }
            if (media == AssetMediaKind.Unknown)
            {
                status = AssetStatus.UnsupportedFormat;
                diagnostics.Add(ProjectDiagnostic.Warning(DiagnosticCodes.Phase5UnsupportedPreview, "The asset extension is not supported by a built-in preview provider.", new ProjectLocation(cases[0])));
            }
            if (cases.GroupBy(path => path.ToUpperInvariant(), StringComparer.Ordinal).Any(group => group.Count() > 1))
            {
                status = AssetStatus.CaseConflict;
                diagnostics.Add(ProjectDiagnostic.Warning(DiagnosticCodes.AssetCaseCollision, "Asset variants differ only by casing.", new ProjectLocation(cases[0])));
            }
            if (variants.GroupBy(variant => variant.Extension, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            {
                status = AssetStatus.VariantConflict;
                diagnostics.Add(ProjectDiagnostic.Warning(DiagnosticCodes.Phase5VariantConflict, "The logical asset has multiple physical variants with the same extension.", new ProjectLocation(cases[0])));
            }

            replacement[key] = new AssetRecord(key, media, variants.OrderBy(variant => AssetCategoryRegistry.ExtensionPreference(variant.Extension)).ToArray(), AssetMetadataSummary.Empty, BuildUsage(key), diagnostics, status);
        }

        foreach (AssetReferenceHint reference in _references)
        {
            if (replacement.ContainsKey(reference.Key)) continue;
            replacement[reference.Key] = new AssetRecord(
                reference.Key,
                InferMediaKind(reference.Key.Category),
                [],
                AssetMetadataSummary.Empty,
                BuildUsage(reference.Key),
                [ProjectDiagnostic.Error(
                    DiagnosticCodes.Phase5MissingAsset,
                    "A referenced logical asset has no physical file.",
                    new ProjectLocation(reference.SourceRelativePath, JsonPath: reference.SourceLocation))],
                AssetStatus.Missing);
        }

        lock (_sync)
        {
            _records.Clear();
            _duplicatePaths.Clear();
            foreach ((AssetKey key, AssetRecord record) in replacement) _records[key] = record;
            _builtUtc = DateTimeOffset.UtcNow;
        }
        progress?.Report(new AssetCatalogueProgress(entries.Count, entries.Count, null, "Ready"));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask RefreshPathsAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        string[] normalized = relativePaths.Select(Normalize).Distinct(PathComparer).ToArray();
        if (normalized.Length == 0) return;
        await BuildAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AssetRecord> EnsureMetadataAsync(AssetKey key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        AssetRecord record = Find(key) ?? throw new KeyNotFoundException($"Asset '{key}' was not found.");
        AssetVariant? variant = record.PreferredVariant;
        if (variant is null || variant.IsEncrypted) return record;
        string cacheKey = AssetCache.BuildKey(variant, "metadata");
        AssetMetadataSummary? metadata = await _cache.TryReadMetadataAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            string fullPath = _content.GetFullPath(variant.RelativePath);
            metadata = await AssetMetadataReaders.ReadAsync(fullPath, record.MediaKind, cancellationToken).ConfigureAwait(false);
            await _cache.WriteMetadataAsync(cacheKey, metadata, cancellationToken).ConfigureAwait(false);
        }

        AssetStatus status = IsMetadataValid(metadata, record.MediaKind) ? record.Status : AssetStatus.Invalid;
        List<ProjectDiagnostic> diagnostics = record.Diagnostics
            .Where(item => item.Code != DiagnosticCodes.Phase5InvalidSheetGrid && item.Code != DiagnosticCodes.Phase5CorruptAsset)
            .ToList();
        if (!IsMetadataValid(metadata, record.MediaKind))
        {
            diagnostics.Add(ProjectDiagnostic.Warning(DiagnosticCodes.Phase5CorruptAsset, "The asset metadata could not be read or validated.", new ProjectLocation(variant.RelativePath)));
        }
        AddSheetDiagnostics(key, metadata, variant.RelativePath, diagnostics);
        AssetRecord updated = record with { Metadata = metadata, Status = status, Diagnostics = diagnostics };
        lock (_sync) _records[key] = updated;
        Changed?.Invoke(this, EventArgs.Empty);
        return updated;
    }

    public async ValueTask<string> EnsureHashAsync(AssetVariant variant, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(variant.Sha256)) return variant.Sha256;
        await using Stream stream = await _content.OpenReadAsync(variant.RelativePath, cancellationToken).ConfigureAwait(false);
        byte[] hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        bool changed = false;
        lock (_sync)
        {
            foreach ((AssetKey key, AssetRecord record) in _records.ToArray())
            {
                int index = record.Variants.ToList().FindIndex(candidate => PathComparer.Equals(candidate.NormalizedRelativePath, variant.NormalizedRelativePath));
                if (index < 0) continue;
                AssetVariant[] variants = record.Variants.ToArray();
                variants[index] = variants[index] with { Sha256 = hash };
                _records[key] = record with { Variants = variants };
                changed = true;
                break;
            }
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return hash;
    }

    public async ValueTask<IReadOnlyList<IReadOnlyList<AssetVariant>>> FindExactDuplicatesAsync(CancellationToken cancellationToken = default)
    {
        List<IReadOnlyList<AssetVariant>> result = [];
        foreach (IGrouping<long, AssetVariant> sizeGroup in Assets.SelectMany(asset => asset.Variants).Where(variant => variant.Size > 0 && !variant.IsEncrypted).GroupBy(variant => variant.Size).Where(group => group.Count() > 1))
        {
            Dictionary<string, List<AssetVariant>> hashes = new(StringComparer.OrdinalIgnoreCase);
            foreach (AssetVariant variant in sizeGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string hash = await EnsureHashAsync(variant, cancellationToken).ConfigureAwait(false);
                if (!hashes.TryGetValue(hash, out List<AssetVariant>? group)) hashes[hash] = group = [];
                group.Add(variant);
            }

            result.AddRange(hashes.Values.Where(group => group.Count > 1));
        }

        lock (_sync)
        {
            _duplicatePaths.Clear();
            foreach (AssetVariant variant in result.SelectMany(group => group)) _duplicatePaths.Add(variant.NormalizedRelativePath);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public void ApplyPendingMutations(IEnumerable<AssetPendingMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        lock (_sync)
        {
            foreach (AssetPendingMutation mutation in mutations)
            {
                switch (mutation.Kind)
                {
                    case AssetPendingMutationKind.Create:
                        ApplyCreateOrReplace(mutation, replace: false);
                        break;
                    case AssetPendingMutationKind.Replace:
                        ApplyCreateOrReplace(mutation, replace: true);
                        break;
                    case AssetPendingMutationKind.Move:
                        ApplyMove(mutation);
                        break;
                    case AssetPendingMutationKind.Delete:
                        ApplyDelete(mutation);
                        break;
                }
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyCreateOrReplace(AssetPendingMutation mutation, bool replace)
    {
        string path = mutation.DestinationRelativePath ?? throw new InvalidOperationException("A pending create or replacement has no destination.");
        string runtimeRelative = StripRuntimePrefix(path);
        if (!AssetCategoryRegistry.TryResolveCategory(runtimeRelative, out AssetCategory category)
            || !AssetCategoryRegistry.TryCreateKey(runtimeRelative, category, out AssetKey key)) return;
        string extension = Path.GetExtension(path);
        AssetVariant variant = new(path, extension, mutation.GeneratedLength, DateTimeOffset.UtcNow, null, AssetCategoryRegistry.IsEncryptedExtension(extension), replace ? AssetStatus.PendingReplacement : AssetStatus.PendingImport);
        if (_records.TryGetValue(key, out AssetRecord? record))
        {
            List<AssetVariant> variants = record.Variants.ToList();
            int index = variants.FindIndex(item => item.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase));
            if (replace && index >= 0) variants[index] = variant;
            else variants.Add(variant);
            _records[key] = record with { Variants = variants, Status = replace ? AssetStatus.PendingReplacement : AssetStatus.PendingImport };
        }
        else
        {
            _records[key] = new AssetRecord(key, AssetCategoryRegistry.ResolveMediaKind(extension), [variant], AssetMetadataSummary.Empty, BuildUsage(key), [], AssetStatus.PendingImport);
        }
    }

    private void ApplyMove(AssetPendingMutation mutation)
    {
        string source = mutation.SourceRelativePath ?? throw new InvalidOperationException("A pending move has no source.");
        string destination = mutation.DestinationRelativePath ?? throw new InvalidOperationException("A pending move has no destination.");
        string sourceRuntime = StripRuntimePrefix(source);
        string destinationRuntime = StripRuntimePrefix(destination);
        if (!AssetCategoryRegistry.TryResolveCategory(sourceRuntime, out AssetCategory sourceCategory)
            || !AssetCategoryRegistry.TryCreateKey(sourceRuntime, sourceCategory, out AssetKey sourceKey)
            || !AssetCategoryRegistry.TryResolveCategory(destinationRuntime, out AssetCategory destinationCategory)
            || !AssetCategoryRegistry.TryCreateKey(destinationRuntime, destinationCategory, out AssetKey destinationKey)) return;
        if (!_records.TryGetValue(sourceKey, out AssetRecord? sourceRecord)) return;
        AssetVariant? existing = sourceRecord.Variants.FirstOrDefault(variant => variant.RelativePath.Equals(source, PathComparison));
        if (existing is null) return;
        List<AssetVariant> remaining = sourceRecord.Variants.Where(variant => !variant.RelativePath.Equals(source, PathComparison)).ToList();
        if (remaining.Count == 0) _records.Remove(sourceKey);
        else _records[sourceKey] = sourceRecord with { Variants = remaining };
        AssetVariant moved = existing with { RelativePath = destination, Extension = Path.GetExtension(destination), Status = AssetStatus.PendingRename };
        if (_records.TryGetValue(destinationKey, out AssetRecord? destinationRecord))
        {
            _records[destinationKey] = destinationRecord with { Variants = destinationRecord.Variants.Append(moved).ToArray(), Status = AssetStatus.PendingRename };
        }
        else
        {
            _records[destinationKey] = new AssetRecord(destinationKey, sourceRecord.MediaKind, [moved], sourceRecord.Metadata, BuildUsage(destinationKey), sourceRecord.Diagnostics, AssetStatus.PendingRename);
        }
    }

    private void ApplyDelete(AssetPendingMutation mutation)
    {
        string source = mutation.SourceRelativePath ?? throw new InvalidOperationException("A pending deletion has no source.");
        string runtime = StripRuntimePrefix(source);
        if (!AssetCategoryRegistry.TryResolveCategory(runtime, out AssetCategory category)
            || !AssetCategoryRegistry.TryCreateKey(runtime, category, out AssetKey key)
            || !_records.TryGetValue(key, out AssetRecord? record)) return;
        List<AssetVariant> variants = record.Variants.Select(variant =>
            variant.RelativePath.Equals(source, PathComparison) ? variant with { Status = AssetStatus.PendingDeletion } : variant).ToList();
        _records[key] = record with { Variants = variants, Status = AssetStatus.PendingDeletion };
    }

    public async ValueTask<IReadOnlyList<ProjectDiagnostic>> ValidateAllAsync(
        IProgress<AssetCatalogueProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        AssetRecord[] snapshot = Assets.ToArray();
        for (int index = 0; index < snapshot.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssetRecord asset = snapshot[index];
            progress?.Report(new AssetCatalogueProgress(index, snapshot.Length, asset.Key.ToString(), "Validating assets"));
            if (!asset.IsMissing && !asset.Variants.All(variant => variant.IsEncrypted) && asset.MediaKind != AssetMediaKind.Unknown)
            {
                await EnsureMetadataAsync(asset.Key, cancellationToken).ConfigureAwait(false);
            }
        }
        progress?.Report(new AssetCatalogueProgress(snapshot.Length, snapshot.Length, null, "Asset validation complete"));
        return Assets.SelectMany(asset => asset.Diagnostics).ToArray();
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        lock (_sync)
        {
            _records.Clear();
            _duplicatePaths.Clear();
        }
        return ValueTask.CompletedTask;
    }

    private async ValueTask<List<AssetEntry>> DiscoverCurrentEntriesAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, AssetEntry> entries = new(PathComparer);
        string runtimeRoot = string.IsNullOrEmpty(_runtimePrefix) ? _projectRoot : Resolve(_runtimePrefix.TrimEnd('/'));
        foreach ((AssetCategory category, string directory) in AssetCategoryRegistry.Directories)
        {
            string fullDirectory = Path.Combine(runtimeRoot, directory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullDirectory)) continue;
            foreach (string fullPath in Directory.EnumerateFiles(fullDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo info = new(fullPath);
                string relative = Normalize(Path.GetRelativePath(_projectRoot, fullPath));
                string extension = info.Extension;
                entries[relative] = new AssetEntry(relative, category, AssetCategoryRegistry.ResolveMediaKind(extension), extension, info.Length, info.LastWriteTimeUtc, AssetCategoryRegistry.IsEncryptedExtension(extension), null);
            }
        }

        if (entries.Count == 0)
        {
            foreach (AssetEntry entry in _initialInventory.Assets) entries[Normalize(entry.RelativePath)] = entry;
        }

        if (_content is IAssetOverlayContentSource overlay)
        {
            foreach (AssetContentOverlayEntry pending in overlay.PendingEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? source = pending.SourceRelativePath is null ? null : Normalize(pending.SourceRelativePath);
                string? destination = pending.DestinationRelativePath is null ? null : Normalize(pending.DestinationRelativePath);
                AssetEntry? sourceEntry = source is not null && entries.TryGetValue(source, out AssetEntry? found) ? found : null;
                if (pending.Kind is AssetContentOverlayKind.Move or AssetContentOverlayKind.Delete)
                {
                    if (source is not null) entries.Remove(source);
                }
                if (pending.Kind is AssetContentOverlayKind.Create or AssetContentOverlayKind.Replace or AssetContentOverlayKind.Move)
                {
                    if (destination is null) continue;
                    string runtimeRelative = StripRuntimePrefix(destination);
                    if (!AssetCategoryRegistry.TryResolveCategory(runtimeRelative, out AssetCategory category)) continue;
                    string extension = Path.GetExtension(destination);
                    long length = pending.ContentLength > 0 ? pending.ContentLength : sourceEntry?.Size ?? 0;
                    entries[destination] = new AssetEntry(
                        destination,
                        category,
                        AssetCategoryRegistry.ResolveMediaKind(extension),
                        extension,
                        length,
                        pending.LastWriteTimeUtc,
                        AssetCategoryRegistry.IsEncryptedExtension(extension),
                        null);
                }
            }
        }

        await Task.Yield();
        return entries.Values.OrderBy(entry => entry.RelativePath, PathComparer).ToList();
    }

    private AssetUsageSummary BuildUsage(AssetKey key)
    {
        AssetReferenceHint[] references = _references.Where(reference => AssetKeyComparer.Instance.Equals(reference.Key, key)).ToArray();
        return new AssetUsageSummary(references.Count(reference => reference.IsExact), references.Count(reference => !reference.IsExact), AssetCategoryRegistry.IsRuntimeReserved(key), references);
    }

    private void ReattachUsage()
    {
        lock (_sync)
        {
            foreach ((AssetKey key, AssetRecord record) in _records.ToArray()) _records[key] = record with { Usage = BuildUsage(key) };
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void AddSheetDiagnostics(AssetKey key, AssetMetadataSummary metadata, string relativePath, ICollection<ProjectDiagnostic> diagnostics)
    {
        ImageAssetMetadata? image = metadata.Image;
        if (image is null || !image.IsValid) return;
        string? message = key.Category switch
        {
            AssetCategory.Characters => MvSpriteSheetDescriptors.DescribeCharacter(key.LogicalName, image.Width, image.Height).ValidationMessage,
            AssetCategory.Faces => MvSpriteSheetDescriptors.DescribeFaces(image.Width, image.Height).ValidationMessage,
            AssetCategory.SideViewActors => MvSpriteSheetDescriptors.DescribeSideViewActor(image.Width, image.Height).ValidationMessage,
            AssetCategory.Animations => MvSpriteSheetDescriptors.DescribeAnimation(image.Width, image.Height).ValidationMessage,
            AssetCategory.Tilesets => MvSpriteSheetDescriptors.DescribeTiles(image.Width, image.Height).ValidationMessage,
            AssetCategory.System when key.LogicalName.Equals("IconSet", StringComparison.OrdinalIgnoreCase) => MvSpriteSheetDescriptors.DescribeIconSet(image.Width, image.Height).ValidationMessage,
            _ => null
        };
        if (message is not null) diagnostics.Add(ProjectDiagnostic.Warning(DiagnosticCodes.Phase5InvalidSheetGrid, message, new ProjectLocation(relativePath)));
    }

    private static bool IsMetadataValid(AssetMetadataSummary metadata, AssetMediaKind mediaKind) => mediaKind switch
    {
        AssetMediaKind.Image => metadata.Image?.IsValid ?? false,
        AssetMediaKind.Audio => metadata.Audio?.IsValid ?? false,
        AssetMediaKind.Movie => metadata.Movie?.IsValid ?? false,
        AssetMediaKind.Font => metadata.Font?.IsValid ?? false,
        _ => false
    };

    private static AssetMediaKind InferMediaKind(AssetCategory category) => category switch
    {
        AssetCategory.BackgroundMusic or AssetCategory.BackgroundSounds or AssetCategory.MusicEffects or AssetCategory.SoundEffects => AssetMediaKind.Audio,
        AssetCategory.Fonts => AssetMediaKind.Font,
        AssetCategory.Movies => AssetMediaKind.Movie,
        AssetCategory.Unknown => AssetMediaKind.Unknown,
        _ => AssetMediaKind.Image
    };

    private string Resolve(string relativePath) => Path.GetFullPath(Path.Combine(_projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    private string StripRuntimePrefix(string path) => string.IsNullOrEmpty(_runtimePrefix) ? Normalize(path) : Normalize(path).StartsWith(_runtimePrefix, StringComparison.OrdinalIgnoreCase) ? Normalize(path)[_runtimePrefix.Length..] : Normalize(path);
    private static string NormalizePrefix(string value) { string normalized = Normalize(value).Trim('/'); return string.IsNullOrEmpty(normalized) || normalized == "." ? string.Empty : normalized + "/"; }
    private static string Normalize(string value) => value.Replace('\\', '/').TrimStart('/');
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed class AssetKeyComparer : IEqualityComparer<AssetKey>
    {
        public static AssetKeyComparer Instance { get; } = new();
        public bool Equals(AssetKey x, AssetKey y) => x.Category == y.Category && string.Equals(x.LogicalName, y.LogicalName, PathComparison);
        public int GetHashCode(AssetKey obj) => HashCode.Combine(obj.Category, PathComparer.GetHashCode(obj.LogicalName));
        private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}
