using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.RpgMakerMV.Documents;

namespace Playroma.JRPG.Workspaces;

public sealed record DocumentCacheOptions(
    long MaximumCleanDocumentBytes = 256 * 1024 * 1024,
    int MaximumOpenMapDocuments = 8);

public interface IProjectDocumentLease : IAsyncDisposable
{
    string RelativePath { get; }
    IMutableProjectDocument Document { get; }
    object TypedDocument { get; }
}

public interface IMvProjectWorkspace : IAsyncDisposable
{
    MvProjectSnapshot Snapshot { get; }
    IReadOnlyList<ProjectDocumentHandle> Documents { get; }
    bool HasUnsavedChanges { get; }
    IProjectFileOverlay FileOverlay { get; }

    ValueTask<IMutableProjectDocument> OpenDocumentAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    ValueTask<object> OpenTypedDocumentAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    ValueTask<IProjectDocumentLease> AcquireDocumentAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    ValueTask<ProjectSavePlan> CreateSavePlanAsync(
        ProjectSaveOptions options,
        CancellationToken cancellationToken = default);

    ValueTask<ProjectSaveResult> SaveAsync(
        ProjectSaveOptions options,
        CancellationToken cancellationToken = default);

    void DiscardAllChanges();
}

public sealed class MvProjectWorkspace : IMvProjectWorkspace
{
    private readonly MutableDocumentFactory _factory;
    private readonly Dictionary<string, IMutableProjectDocument> _openDocuments;
    private readonly Dictionary<string, ProjectDocumentHandle> _handles;
    private readonly Dictionary<string, int> _leaseCounts;
    private readonly LinkedList<string> _lru = [];
    private readonly DocumentCacheOptions _cacheOptions;
    private bool _disposed;

    private MvProjectWorkspace(MvProjectSnapshot snapshot, DocumentCacheOptions cacheOptions)
    {
        Snapshot = snapshot;
        _cacheOptions = cacheOptions;
        _factory = new MutableDocumentFactory(snapshot.Identity.ProjectRoot);
        _openDocuments = new Dictionary<string, IMutableProjectDocument>(PathComparer);
        _handles = BuildHandles(snapshot).ToDictionary(handle => Normalize(handle.RelativePath), PathComparer);
        _leaseCounts = new Dictionary<string, int>(PathComparer);
        FileOverlay = new ProjectFileOverlay(snapshot.Identity.ProjectRoot);
    }

    public event EventHandler<WorkspaceDocumentStateChangedEventArgs>? DocumentStateChanged;

    public MvProjectSnapshot Snapshot { get; }
    public IProjectFileOverlay FileOverlay { get; }

    public IReadOnlyList<ProjectDocumentHandle> Documents => _handles.Values
        .Select(handle => _openDocuments.TryGetValue(Normalize(handle.RelativePath), out IMutableProjectDocument? open)
            ? handle with { State = open.State, Fingerprint = open.CurrentDiskFingerprint ?? open.OriginalFingerprint }
            : handle)
        .OrderBy(handle => handle.RelativePath, PathComparer)
        .ToArray();

    public bool HasUnsavedChanges => _openDocuments.Values.Any(document => document.State == MutableDocumentState.Modified)
        || FileOverlay.HasPendingChanges;

    public static async ValueTask<OperationResult<MvProjectWorkspace>> OpenAsync(
        string projectPath,
        DocumentCacheOptions? cacheOptions = null,
        CancellationToken cancellationToken = default)
    {
        MvProjectLoader loader = new();
        OperationResult<MvProjectSnapshot> loaded = await loader.LoadAsync(
            projectPath,
            new ProjectLoadOptions(ProjectLoadDepth.StandardData),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (loaded.Value is null)
        {
            return OperationResult<MvProjectWorkspace>.Failure(loaded.Diagnostics);
        }

        MvProjectWorkspace workspace = new(loaded.Value, cacheOptions ?? new DocumentCacheOptions());
        return OperationResult<MvProjectWorkspace>.Success(workspace, loaded.Diagnostics);
    }

    public async ValueTask<IMutableProjectDocument> OpenDocumentAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string normalized = Normalize(relativePath);
        if (_openDocuments.TryGetValue(normalized, out IMutableProjectDocument? existing))
        {
            Touch(normalized);
            return existing;
        }

        if (!_handles.TryGetValue(normalized, out ProjectDocumentHandle? handle))
        {
            if (!FileOverlay.Exists(normalized) || !normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new FileNotFoundException("The path is not a recognized mutable MV project document.", normalized);
            }
            await using Stream staged = await FileOverlay.OpenReadAsync(normalized, cancellationToken).ConfigureAwait(false);
            using MemoryStream memory = new();
            await staged.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            byte[] stagedBytes = memory.ToArray();
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stagedBytes)).ToLowerInvariant();
            MvDocumentKind inferredKind = Path.GetFileNameWithoutExtension(normalized).StartsWith("Map", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileNameWithoutExtension(normalized), "MapInfos", StringComparison.OrdinalIgnoreCase)
                ? MvDocumentKind.Map
                : MvDocumentKind.Unknown;
            ContentFingerprint fingerprint = new(stagedBytes.Length, DateTimeOffset.UtcNow, hash);
            handle = new ProjectDocumentHandle(normalized, inferredKind, MutableDocumentState.Unloaded, fingerprint, stagedBytes.Length);
            _handles[normalized] = handle;
            IMutableProjectDocument generated = MutableJsonProjectDocument.Create(normalized, inferredKind, stagedBytes, fingerprint);
            _openDocuments.Add(normalized, generated);
            _handles[normalized] = handle with { State = generated.State, Fingerprint = generated.OriginalFingerprint };
            Touch(normalized);
            PublishState(normalized, generated.State);
            return generated;
        }

        IMutableProjectDocument document;
        if (normalized.EndsWith("js/plugins.js", StringComparison.OrdinalIgnoreCase))
        {
            string fullPath = Path.Combine(Snapshot.Identity.ProjectRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
            byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            FileInfo info = new(fullPath);
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            document = MvPluginManifestDocument.Parse(
                normalized,
                bytes,
                new ContentFingerprint(info.Length, info.LastWriteTimeUtc, hash));
        }
        else
        {
            document = await _factory.OpenJsonAsync(normalized, handle.Kind, cancellationToken).ConfigureAwait(false);
        }

        _openDocuments.Add(normalized, document);
        _handles[normalized] = handle with { State = document.State, Fingerprint = document.OriginalFingerprint };
        Touch(normalized);
        EvictCleanDocuments();
        PublishState(normalized, document.State);
        return document;
    }

    public async ValueTask<object> OpenTypedDocumentAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        IMutableProjectDocument document = await OpenDocumentAsync(relativePath, cancellationToken).ConfigureAwait(false);
        return WrapTyped(document);
    }

    public async ValueTask<IProjectDocumentLease> AcquireDocumentAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string normalized = Normalize(relativePath);
        IMutableProjectDocument document = await OpenDocumentAsync(normalized, cancellationToken).ConfigureAwait(false);
        _leaseCounts.TryGetValue(normalized, out int count);
        _leaseCounts[normalized] = checked(count + 1);
        return new ProjectDocumentLease(this, normalized, document, WrapTyped(document));
    }

    public async ValueTask<ProjectSavePlan> CreateSavePlanAsync(
        ProjectSaveOptions options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        SavePlanBuilder builder = new(Snapshot.Identity.ProjectRoot);
        return await builder.BuildAsync(_openDocuments.Values, FileOverlay, options, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ProjectSaveResult> SaveAsync(
        ProjectSaveOptions options,
        CancellationToken cancellationToken = default)
    {
        ProjectSavePlan plan = await CreateSavePlanAsync(options, cancellationToken).ConfigureAwait(false);
        ProjectSaveService service = new(Snapshot.Identity.ProjectRoot);
        ProjectSaveResult result = await service.SaveAsync(plan, options, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) FileOverlay.MarkCommitted(result.Files.Select(file => file.MutationId));
        foreach (IMutableProjectDocument document in _openDocuments.Values)
        {
            string normalized = Normalize(document.RelativePath);
            if (_handles.TryGetValue(normalized, out ProjectDocumentHandle? handle))
            {
                _handles[normalized] = handle with
                {
                    State = document.State,
                    Fingerprint = document.CurrentDiskFingerprint ?? document.OriginalFingerprint
                };
                PublishState(normalized, document.State);
            }
        }

        return result;
    }

    public void DiscardAllChanges()
    {
        ThrowIfDisposed();
        FileOverlay.DiscardAll();
        foreach (IMutableProjectDocument document in _openDocuments.Values)
        {
            document.DiscardChanges();
            PublishState(Normalize(document.RelativePath), document.State);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        foreach (IMutableProjectDocument document in _openDocuments.Values) document.Dispose();
        _openDocuments.Clear();
        _leaseCounts.Clear();
        await FileOverlay.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    internal void ReleaseLease(string relativePath)
    {
        if (_disposed) return;
        string normalized = Normalize(relativePath);
        if (!_leaseCounts.TryGetValue(normalized, out int count)) return;
        if (count <= 1) _leaseCounts.Remove(normalized);
        else _leaseCounts[normalized] = count - 1;
        EvictCleanDocuments();
    }

    private static object WrapTyped(IMutableProjectDocument document) =>
        document is MutableJsonProjectDocument json ? MvTypedDocumentFactory.Wrap(json) : document;

    private static IEnumerable<ProjectDocumentHandle> BuildHandles(MvProjectSnapshot snapshot)
    {
        foreach (MvDatabaseFileSummary file in snapshot.Database.Files.Where(file => file.IsValidJson))
        {
            ProjectFileEntry? entry = snapshot.Files.Find(file.RelativePath);
            yield return new ProjectDocumentHandle(
                Normalize(file.RelativePath),
                file.Kind,
                MutableDocumentState.Unloaded,
                entry?.Fingerprint ?? new ContentFingerprint(0, DateTimeOffset.MinValue, null),
                entry?.Size ?? 0);
        }

        foreach (MvMapDescriptor map in snapshot.Maps.Where(map => map.FileExists))
        {
            ProjectFileEntry? entry = snapshot.Files.Find(map.RelativePath);
            yield return new ProjectDocumentHandle(
                Normalize(map.RelativePath),
                MvDocumentKind.Map,
                MutableDocumentState.Unloaded,
                entry?.Fingerprint ?? new ContentFingerprint(0, DateTimeOffset.MinValue, null),
                entry?.Size ?? 0);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Plugins.ManifestRelativePath))
        {
            ProjectFileEntry? entry = snapshot.Files.Find(snapshot.Plugins.ManifestRelativePath);
            yield return new ProjectDocumentHandle(
                Normalize(snapshot.Plugins.ManifestRelativePath),
                MvDocumentKind.Unknown,
                MutableDocumentState.Unloaded,
                entry?.Fingerprint ?? new ContentFingerprint(0, DateTimeOffset.MinValue, snapshot.Plugins.ManifestSha256),
                entry?.Size ?? 0);
        }
    }

    private void Touch(string relativePath)
    {
        LinkedListNode<string>? existing = _lru.Find(relativePath);
        if (existing is not null) _lru.Remove(existing);
        _lru.AddFirst(relativePath);
    }

    private void EvictCleanDocuments()
    {
        long cleanBytes = _openDocuments.Values.Where(document => document.State == MutableDocumentState.Clean)
            .Sum(document => document.GetOriginalBytes().Length);
        int openMaps = _openDocuments.Values.Count(document => document.Kind == MvDocumentKind.Map);
        LinkedListNode<string>? node = _lru.Last;
        while (node is not null && (cleanBytes > _cacheOptions.MaximumCleanDocumentBytes || openMaps > _cacheOptions.MaximumOpenMapDocuments))
        {
            LinkedListNode<string>? previous = node.Previous;
            string key = node.Value;
            IMutableProjectDocument document = _openDocuments[key];
            bool leased = _leaseCounts.TryGetValue(key, out int leaseCount) && leaseCount > 0;
            if (!leased && document.State == MutableDocumentState.Clean)
            {
                cleanBytes -= document.GetOriginalBytes().Length;
                if (document.Kind == MvDocumentKind.Map) openMaps--;
                document.Dispose();
                _openDocuments.Remove(key);
                _lru.Remove(node);
                PublishState(key, MutableDocumentState.Unloaded);
            }
            node = previous;
        }
    }

    private void PublishState(string relativePath, MutableDocumentState state) =>
        DocumentStateChanged?.Invoke(this, new WorkspaceDocumentStateChangedEventArgs(relativePath, state));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static string Normalize(string value) => value.Replace('\\', '/');
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed class ProjectDocumentLease : IProjectDocumentLease
    {
        private readonly MvProjectWorkspace _owner;
        private int _disposed;

        public ProjectDocumentLease(
            MvProjectWorkspace owner,
            string relativePath,
            IMutableProjectDocument document,
            object typedDocument)
        {
            _owner = owner;
            RelativePath = relativePath;
            Document = document;
            TypedDocument = typedDocument;
        }

        public string RelativePath { get; }
        public IMutableProjectDocument Document { get; }
        public object TypedDocument { get; }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _owner.ReleaseLease(RelativePath);
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class WorkspaceDocumentStateChangedEventArgs : EventArgs
{
    public WorkspaceDocumentStateChangedEventArgs(string relativePath, MutableDocumentState state)
    {
        RelativePath = relativePath;
        State = state;
    }

    public string RelativePath { get; }
    public MutableDocumentState State { get; }
}
