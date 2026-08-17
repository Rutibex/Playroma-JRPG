using System.Security.Cryptography;
using Playroma.JRPG.Documents;

namespace Playroma.JRPG.Documents.Saving;

public enum FileMutationKind
{
    Create,
    Replace,
    Move,
    Delete
}

public sealed record PendingFileMutation(
    Guid Id,
    FileMutationKind Kind,
    string? SourceRelativePath,
    string? DestinationRelativePath,
    ContentFingerprint? ExpectedSourceFingerprint,
    ContentFingerprint? ExpectedDestinationFingerprint,
    string? StagedContentPath,
    string? StagedSha256,
    long StagedLength,
    string Description)
{
    public IReadOnlyCollection<string> AffectedPaths => new[] { SourceRelativePath, DestinationRelativePath }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path!.Replace('\\', '/'))
        .Distinct(PathComparer)
        .ToArray();

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

public sealed class ProjectFileOverlayChangedEventArgs : EventArgs
{
    public ProjectFileOverlayChangedEventArgs(IReadOnlyCollection<PendingFileMutation> mutations, IReadOnlyCollection<string> affectedPaths)
    {
        Mutations = mutations;
        AffectedPaths = affectedPaths;
    }

    public IReadOnlyCollection<PendingFileMutation> Mutations { get; }
    public IReadOnlyCollection<string> AffectedPaths { get; }
}

public interface IProjectFileOverlay : IAsyncDisposable
{
    string ProjectRoot { get; }
    IReadOnlyList<PendingFileMutation> PendingMutations { get; }
    bool HasPendingChanges { get; }

    ValueTask<PendingFileMutation> CreateStagedMutationAsync(
        FileMutationKind kind,
        string externalSourcePath,
        string? existingRelativePath,
        string destinationRelativePath,
        string description,
        CancellationToken cancellationToken = default);

    ValueTask<PendingFileMutation> CreatePathMutationAsync(
        FileMutationKind kind,
        string sourceRelativePath,
        string? destinationRelativePath,
        string description,
        CancellationToken cancellationToken = default);

    void Stage(PendingFileMutation mutation);
    bool Unstage(Guid mutationId);
    ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);
    bool Exists(string relativePath);
    void MarkCommitted(IEnumerable<Guid> mutationIds);
    void DiscardAll();
    event EventHandler<ProjectFileOverlayChangedEventArgs>? Changed;
}

public sealed class ProjectFileOverlay : IProjectFileOverlay
{
    private readonly object _sync = new();
    private readonly ProjectPathPolicy _paths;
    private readonly string _stagingRoot;
    private readonly Dictionary<Guid, PendingFileMutation> _mutations = [];
    private bool _disposed;

    public ProjectFileOverlay(string projectRoot, string? stagingRoot = null)
    {
        ProjectRoot = Path.GetFullPath(projectRoot);
        _paths = new ProjectPathPolicy(ProjectRoot);
        _stagingRoot = stagingRoot ?? CreateDefaultStagingRoot(ProjectRoot);
        Directory.CreateDirectory(_stagingRoot);
    }

    public string ProjectRoot { get; }

    public IReadOnlyList<PendingFileMutation> PendingMutations
    {
        get
        {
            lock (_sync) return _mutations.Values.OrderBy(item => item.Description, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public bool HasPendingChanges
    {
        get { lock (_sync) return _mutations.Count > 0; }
    }

    public event EventHandler<ProjectFileOverlayChangedEventArgs>? Changed;

    public async ValueTask<PendingFileMutation> CreateStagedMutationAsync(
        FileMutationKind kind,
        string externalSourcePath,
        string? existingRelativePath,
        string destinationRelativePath,
        string description,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (kind is not FileMutationKind.Create and not FileMutationKind.Replace)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Staged content is valid only for create and replace mutations.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(externalSourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRelativePath);
        string fullExternal = Path.GetFullPath(externalSourcePath);
        if (!File.Exists(fullExternal)) throw new FileNotFoundException("The imported asset does not exist.", fullExternal);
        if (new FileInfo(fullExternal).Length == 0) throw new InvalidDataException("The imported asset is empty.");
        Guid id = Guid.NewGuid();
        string extension = Path.GetExtension(destinationRelativePath);
        string staged = Path.Combine(_stagingRoot, id.ToString("N") + extension);
        await CopyThroughAsync(fullExternal, staged, cancellationToken).ConfigureAwait(false);
        ContentFingerprint? sourceFingerprint = existingRelativePath is null
            ? null
            : await TryFingerprintAsync(_paths.ResolveRelative(existingRelativePath), cancellationToken).ConfigureAwait(false);
        ContentFingerprint? destinationFingerprint = await TryFingerprintAsync(_paths.ResolveRelative(destinationRelativePath), cancellationToken).ConfigureAwait(false);
        FileInfo stagedInfo = new(staged);
        string hash = await HashFileAsync(staged, cancellationToken).ConfigureAwait(false);
        return new PendingFileMutation(
            id,
            kind,
            Normalize(existingRelativePath),
            Normalize(destinationRelativePath),
            sourceFingerprint,
            destinationFingerprint,
            staged,
            hash,
            stagedInfo.Length,
            description);
    }

    public async ValueTask<PendingFileMutation> CreatePathMutationAsync(
        FileMutationKind kind,
        string sourceRelativePath,
        string? destinationRelativePath,
        string description,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (kind is not FileMutationKind.Move and not FileMutationKind.Delete)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Path mutations are valid only for move and delete operations.");
        }
        string source = NormalizeRequired(sourceRelativePath);
        string? destination = Normalize(destinationRelativePath);
        if (kind == FileMutationKind.Move && string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("A destination path is required for a move operation.", nameof(destinationRelativePath));
        }
        ContentFingerprint sourceFingerprint = await TryFingerprintAsync(_paths.ResolveRelative(source), cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The asset source does not exist.", source);
        ContentFingerprint? destinationFingerprint = destination is null
            ? null
            : await TryFingerprintAsync(_paths.ResolveRelative(destination), cancellationToken).ConfigureAwait(false);
        return new PendingFileMutation(
            Guid.NewGuid(), kind, source, destination, sourceFingerprint, destinationFingerprint,
            null, null, 0, description);
    }

    public void Stage(PendingFileMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ThrowIfDisposed();
        lock (_sync)
        {
            ValidateAgainstOverlay(mutation);
            _mutations[mutation.Id] = mutation;
        }
        Publish([mutation], mutation.AffectedPaths);
    }

    public bool Unstage(Guid mutationId)
    {
        ThrowIfDisposed();
        PendingFileMutation? removed;
        lock (_sync)
        {
            if (!_mutations.Remove(mutationId, out removed)) return false;
        }
        Publish([removed], removed.AffectedPaths);
        return true;
    }

    public async ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string normalized = NormalizeRequired(relativePath);
        PendingFileMutation[] pending = PendingMutations.ToArray();
        PendingFileMutation? destination = pending.LastOrDefault(item => PathEquals(item.DestinationRelativePath, normalized));
        if (destination is not null)
        {
            if (destination.Kind is FileMutationKind.Create or FileMutationKind.Replace)
            {
                if (string.IsNullOrWhiteSpace(destination.StagedContentPath) || !File.Exists(destination.StagedContentPath))
                {
                    throw new FileNotFoundException("The staged asset content is missing.", destination.StagedContentPath);
                }
                return new FileStream(destination.StagedContentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            if (destination.Kind == FileMutationKind.Move && destination.SourceRelativePath is not null)
            {
                return await OpenReadAsync(destination.SourceRelativePath, cancellationToken).ConfigureAwait(false);
            }
        }
        if (pending.Any(item => PathEquals(item.SourceRelativePath, normalized) && item.Kind is FileMutationKind.Delete or FileMutationKind.Move))
        {
            throw new FileNotFoundException("The asset is pending deletion or move.", normalized);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new FileStream(_paths.ResolveRelative(normalized), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public bool Exists(string relativePath)
    {
        ThrowIfDisposed();
        string normalized = NormalizeRequired(relativePath);
        PendingFileMutation[] pending = PendingMutations.ToArray();
        PendingFileMutation? destination = pending.LastOrDefault(item => PathEquals(item.DestinationRelativePath, normalized));
        if (destination is not null) return destination.Kind is FileMutationKind.Create or FileMutationKind.Replace or FileMutationKind.Move;
        if (pending.Any(item => PathEquals(item.SourceRelativePath, normalized) && item.Kind is FileMutationKind.Delete or FileMutationKind.Move)) return false;
        return File.Exists(_paths.ResolveRelative(normalized));
    }

    public void MarkCommitted(IEnumerable<Guid> mutationIds)
    {
        ArgumentNullException.ThrowIfNull(mutationIds);
        List<PendingFileMutation> removed = [];
        lock (_sync)
        {
            foreach (Guid id in mutationIds)
            {
                if (_mutations.Remove(id, out PendingFileMutation? mutation)) removed.Add(mutation);
            }
        }
        foreach (PendingFileMutation mutation in removed) TryDeleteStaged(mutation);
        if (removed.Count > 0) Publish(removed, removed.SelectMany(item => item.AffectedPaths).Distinct(PathComparer).ToArray());
    }

    public void DiscardAll()
    {
        ThrowIfDisposed();
        PendingFileMutation[] removed;
        lock (_sync)
        {
            removed = _mutations.Values.ToArray();
            _mutations.Clear();
        }
        foreach (PendingFileMutation mutation in removed) TryDeleteStaged(mutation);
        if (removed.Length > 0) Publish(removed, removed.SelectMany(item => item.AffectedPaths).Distinct(PathComparer).ToArray());
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        DiscardAll();
        _disposed = true;
        try
        {
            if (Directory.Exists(_stagingRoot) && !Directory.EnumerateFileSystemEntries(_stagingRoot).Any()) Directory.Delete(_stagingRoot);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return ValueTask.CompletedTask;
    }

    private void ValidateAgainstOverlay(PendingFileMutation candidate)
    {
        foreach (PendingFileMutation existing in _mutations.Values.Where(item => item.Id != candidate.Id))
        {
            if (candidate.AffectedPaths.Any(candidatePath => existing.AffectedPaths.Any(existingPath => PathComparer.Equals(candidatePath, existingPath))))
            {
                throw new InvalidOperationException("Another pending file operation already affects one of these paths.");
            }
        }
    }

    private void Publish(IReadOnlyCollection<PendingFileMutation> mutations, IReadOnlyCollection<string> paths) =>
        Changed?.Invoke(this, new ProjectFileOverlayChangedEventArgs(mutations, paths));

    private static async ValueTask<ContentFingerprint?> TryFingerprintAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath)) return null;
        FileInfo info = new(fullPath);
        string hash = await HashFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
        info.Refresh();
        return new ContentFingerprint(info.Length, info.LastWriteTimeUtc, hash);
    }

    private static async ValueTask CopyThroughAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private static async ValueTask<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CreateDefaultStagingRoot(string projectRoot)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string identity = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(projectRoot))).ToLowerInvariant()[..24];
        return Path.Combine(local, "Playroma", "JRPG", "staging", identity);
    }

    private static string NormalizeRequired(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Replace('\\', '/').TrimStart('/');
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : NormalizeRequired(value);
    private static bool PathEquals(string? left, string right) => left is not null && PathComparer.Equals(left, right);
    private static void TryDeleteStaged(PendingFileMutation mutation)
    {
        if (string.IsNullOrWhiteSpace(mutation.StagedContentPath)) return;
        try { if (File.Exists(mutation.StagedContentPath)) File.Delete(mutation.StagedContentPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
