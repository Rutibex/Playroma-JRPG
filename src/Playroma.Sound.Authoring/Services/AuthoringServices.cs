using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.Sound.Authoring.Diagnostics;
using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Projects;

namespace Playroma.Sound.Authoring.Services;

public sealed class AuthoringSelectionChangedEventArgs : EventArgs
{
    public AuthoringSelectionChangedEventArgs(string documentId, string primaryObjectId, IReadOnlyList<string> objectIds)
    {
        DocumentId = documentId;
        PrimaryObjectId = primaryObjectId;
        ObjectIds = objectIds;
    }
    public string DocumentId { get; }
    public string PrimaryObjectId { get; }
    public IReadOnlyList<string> ObjectIds { get; }
}

public sealed class AuthoringSelectionService
{
    private readonly Dictionary<string, SelectionState> _selection = new(StringComparer.Ordinal);
    public event EventHandler<AuthoringSelectionChangedEventArgs>? SelectionChanged;

    public IReadOnlyList<string> GetSelection(string documentId) => _selection.TryGetValue(documentId, out SelectionState? state) ? state.ObjectIds : [];
    public string GetPrimary(string documentId) => _selection.TryGetValue(documentId, out SelectionState? state) ? state.PrimaryObjectId : "";

    public void Set(string documentId, IEnumerable<string> objectIds, string? primaryObjectId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(objectIds);
        string[] ids = objectIds.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
        string primary = primaryObjectId ?? ids.FirstOrDefault() ?? "";
        if (!string.IsNullOrEmpty(primary) && !ids.Contains(primary, StringComparer.Ordinal)) throw new ArgumentException("The primary object must be part of the selection.", nameof(primaryObjectId));
        _selection[documentId] = new SelectionState(primary, ids);
        SelectionChanged?.Invoke(this, new AuthoringSelectionChangedEventArgs(documentId, primary, ids));
    }

    public void Clear(string documentId) => Set(documentId, []);
    private sealed record SelectionState(string PrimaryObjectId, IReadOnlyList<string> ObjectIds);
}

public sealed record AuthoringClipboardPackage(int FormatVersion, string PayloadType, string SourceDocumentId, string JsonPayload, IReadOnlyDictionary<string, string> Metadata)
{
    public const int CurrentFormatVersion = 1;
}

public sealed class AuthoringClipboardSerializer
{
    private readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = false };

    public string Serialize(AuthoringClipboardPackage package) => JsonSerializer.Serialize(package ?? throw new ArgumentNullException(nameof(package)), _options);
    public AuthoringClipboardPackage Deserialize(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        AuthoringClipboardPackage package = JsonSerializer.Deserialize<AuthoringClipboardPackage>(content, _options) ?? throw new InvalidDataException("Clipboard package is empty.");
        if (package.FormatVersion != AuthoringClipboardPackage.CurrentFormatVersion) throw new NotSupportedException($"Clipboard format {package.FormatVersion} is not supported.");
        return package;
    }

    public AuthoringClipboardPackage CopyDocument(IAuthoringDocument document) => new(AuthoringClipboardPackage.CurrentFormatVersion, document.Kind.ToString(), document.DocumentId, document.SerializeContent(), new Dictionary<string, string>(StringComparer.Ordinal));
}

public sealed class DocumentCompilationCompletedEventArgs : EventArgs
{
    public DocumentCompilationCompletedEventArgs(AuthoringCompilationResult result) => Result = result;
    public AuthoringCompilationResult Result { get; }
}

public sealed class DocumentCompilationCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<IAuthoringDocument, CancellationTokenSource> _pending = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public event EventHandler<DocumentCompilationCompletedEventArgs>? CompilationCompleted;

    public void Schedule(IAuthoringDocument document, AuthoringCompilationContext context, TimeSpan? debounce = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        TimeSpan delay = debounce ?? TimeSpan.FromMilliseconds(250);
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounce));
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_pending.Remove(document, out CancellationTokenSource? old)) old.Cancel();
            cancellation = new CancellationTokenSource();
            _pending.Add(document, cancellation);
        }
        _ = RunAsync(document, context, delay, cancellation);
    }

    public void Cancel(IAuthoringDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_gate)
        {
            if (_pending.Remove(document, out CancellationTokenSource? cancellation)) cancellation.Cancel();
        }
    }

    private async Task RunAsync(IAuthoringDocument document, AuthoringCompilationContext context, TimeSpan delay, CancellationTokenSource cancellation)
    {
        AuthoringCompilationResult result;
        try
        {
            await Task.Delay(delay, cancellation.Token).ConfigureAwait(false);
            result = await document.CompileAsync(context, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            result = new AuthoringCompilationResult(document.DocumentId, document.Revision, AuthoringCompilationState.Cancelled, ValidationSummary.Empty, null, TimeSpan.Zero, exception);
        }
        catch (Exception exception)
        {
            result = new AuthoringCompilationResult(document.DocumentId, document.Revision, AuthoringCompilationState.Failed, new ValidationSummary([new AuthoringDiagnostic("AUTHORING_COMPILE", AuthoringDiagnosticSeverity.Error, exception.Message, document.DocumentId, SourcePath: document.FilePath)]), null, TimeSpan.Zero, exception);
        }
        finally
        {
            lock (_gate)
            {
                if (_pending.TryGetValue(document, out CancellationTokenSource? current) && ReferenceEquals(current, cancellation)) _pending.Remove(document);
            }
            cancellation.Dispose();
        }
        if (!_disposed) CompilationCompleted?.Invoke(this, new DocumentCompilationCompletedEventArgs(result));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            foreach (CancellationTokenSource cancellation in _pending.Values) cancellation.Cancel();
            _pending.Clear();
        }
    }
}

public sealed record AutosaveEnvelope(
    int FormatVersion,
    string ProjectId,
    string DocumentId,
    AuthoringDocumentKind DocumentKind,
    string OriginalPath,
    long Revision,
    DateTimeOffset SavedAtUtc,
    string Content)
{
    public const int CurrentFormatVersion = 1;
}

public sealed class AuthoringAutosaveService
{
    private readonly string _rootDirectory;
    private readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };

    public AuthoringAutosaveService(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string Save(string projectId, IAuthoringDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(document);
        string directory = Path.Combine(_rootDirectory, Sanitize(projectId));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Sanitize(document.DocumentId) + ".autosave.json");
        var envelope = new AutosaveEnvelope(AutosaveEnvelope.CurrentFormatVersion, projectId, document.DocumentId, document.Kind, document.FilePath, document.Revision, DateTimeOffset.UtcNow, document.SerializeContent());
        AtomicTextFile.Write(path, JsonSerializer.Serialize(envelope, _options));
        return path;
    }

    public IReadOnlyList<AutosaveEnvelope> Scan(string? projectId = null)
    {
        string directory = string.IsNullOrWhiteSpace(projectId) ? _rootDirectory : Path.Combine(_rootDirectory, Sanitize(projectId));
        if (!Directory.Exists(directory)) return [];
        var result = new List<AutosaveEnvelope>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.autosave.json", SearchOption.AllDirectories))
        {
            try
            {
                AutosaveEnvelope? envelope = JsonSerializer.Deserialize<AutosaveEnvelope>(File.ReadAllText(path), _options);
                if (envelope is not null && envelope.FormatVersion == AutosaveEnvelope.CurrentFormatVersion) result.Add(envelope);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return result.OrderByDescending(item => item.SavedAtUtc).ToArray();
    }

    public void Delete(string projectId, string documentId)
    {
        string path = Path.Combine(_rootDirectory, Sanitize(projectId), Sanitize(documentId) + ".autosave.json");
        if (File.Exists(path)) File.Delete(path);
    }

    private static string Sanitize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string safe = new(value.Select(character =>
            character < 32 || character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'
                ? '_'
                : character).ToArray());
        safe = safe.Trim().TrimEnd('.');
        if (safe.Length == 0) safe = "document";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).Substring(0, 10);
        return safe + "-" + hash;
    }
}

public sealed record DocumentDependency(string AssetId, AuthoringAssetKind Kind, string RelativePath, bool Exists, string ReferencedBy);

public sealed class ProjectDependencyTracker
{
    public IReadOnlyList<DocumentDependency> Analyze(AuthoringProjectDefinition project, string projectRoot, IAuthoringDocument document)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(document);
        if (document is not StreamingSceneDocument sceneDocument) return [];
        var assets = project.Assets.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var result = new List<DocumentDependency>();
        Playroma.Sound.Streaming.Definitions.SoundSceneDefinition scene = sceneDocument.CreateEditableCopy();
        IEnumerable<string> referencedSounds = scene.Sounds.Select(item => item.SoundId)
            .Concat(scene.Layers.Select(item => item.SoundId))
            .Concat(scene.Emitters.SelectMany(item => item.SoundIds))
            .Concat(scene.EventClusters.SelectMany(cluster => cluster.Events.Select(item => item.SoundId)))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal);
        foreach (string soundId in referencedSounds)
        {
            if (!assets.TryGetValue(soundId, out ProjectAssetEntry? asset))
            {
                result.Add(new DocumentDependency(soundId, AuthoringAssetKind.Other, "", false, document.DocumentId));
                continue;
            }
            string path = ProjectPath.Resolve(projectRoot, asset.RelativePath);
            result.Add(new DocumentDependency(soundId, asset.Kind, asset.RelativePath, File.Exists(path), document.DocumentId));
        }
        return result;
    }
}

public sealed record RecentProjectEntry(string Path, string DisplayName, DateTimeOffset LastOpenedUtc, bool Pinned);

public sealed class RecentProjectStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly int _capacity;

    public RecentProjectStore(string path, int capacity = 20)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _path = Path.GetFullPath(path);
        _capacity = capacity;
    }

    public IReadOnlyList<RecentProjectEntry> Load()
    {
        if (!File.Exists(_path)) return [];
        try { return JsonSerializer.Deserialize<List<RecentProjectEntry>>(File.ReadAllText(_path), _options) ?? []; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    public void Touch(string path, string displayName, bool pinned = false)
    {
        string fullPath = Path.GetFullPath(path);
        List<RecentProjectEntry> entries = Load().Where(item => !StringComparer.OrdinalIgnoreCase.Equals(item.Path, fullPath)).ToList();
        entries.Add(new RecentProjectEntry(fullPath, displayName, DateTimeOffset.UtcNow, pinned));
        entries = entries.OrderByDescending(item => item.Pinned).ThenByDescending(item => item.LastOpenedUtc).Take(_capacity).ToList();
        AtomicTextFile.Write(_path, JsonSerializer.Serialize(entries, _options));
    }
}

public sealed class AutosaveFailedEventArgs : EventArgs
{
    public AutosaveFailedEventArgs(string documentId, Exception exception)
    {
        DocumentId = documentId;
        Exception = exception;
    }
    public string DocumentId { get; }
    public Exception Exception { get; }
}

/// <summary>
/// Periodically captures dirty documents. It has no dependency on a UI framework and
/// never modifies the documents; hosts may start and stop it with project lifetime.
/// </summary>
public sealed class AuthoringAutosaveCoordinator : IAsyncDisposable
{
    private readonly AuthoringAutosaveService _service;
    private readonly string _projectId;
    private readonly Func<IReadOnlyCollection<IAuthoringDocument>> _documents;
    private readonly TimeSpan _interval;
    private CancellationTokenSource? _cancellation;
    private Task? _worker;

    public AuthoringAutosaveCoordinator(
        AuthoringAutosaveService service,
        string projectId,
        Func<IReadOnlyCollection<IAuthoringDocument>> documents,
        TimeSpan? interval = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        _projectId = projectId;
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _interval = interval ?? TimeSpan.FromMinutes(2);
        if (_interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
    }

    public event EventHandler<AutosaveFailedEventArgs>? AutosaveFailed;
    public bool IsRunning => _worker is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning) return;
        _cancellation = new CancellationTokenSource();
        _worker = RunAsync(_cancellation.Token);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation = _cancellation;
        Task? worker = _worker;
        _cancellation = null;
        _worker = null;
        if (cancellation is null || worker is null) return;
        cancellation.Cancel();
        try { await worker.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally { cancellation.Dispose(); }
    }

    public void SaveDirtyDocuments()
    {
        foreach (IAuthoringDocument document in _documents().Where(item => item.IsDirty).ToArray())
        {
            try { _service.Save(_projectId, document); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                AutosaveFailed?.Invoke(this, new AutosaveFailedEventArgs(document.DocumentId, exception));
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) SaveDirtyDocuments();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

public static class RecoveryDocumentFactory
{
    public static IAuthoringDocument Create(AutosaveEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.FormatVersion != AutosaveEnvelope.CurrentFormatVersion) throw new NotSupportedException($"Autosave format {envelope.FormatVersion} is not supported.");
        string path = string.IsNullOrWhiteSpace(envelope.OriginalPath)
            ? Path.Combine(Path.GetTempPath(), envelope.DocumentId + Extension(envelope.DocumentKind))
            : Path.GetFullPath(envelope.OriginalPath);

        IAuthoringDocument document = envelope.DocumentKind switch
        {
            AuthoringDocumentKind.SoundGraph when File.Exists(path) => SoundGraphDocument.Load(path),
            AuthoringDocumentKind.SoundGraph => new SoundGraphDocument(path, new Playroma.Sound.Graph.Definitions.SoundGraphDefinition { Id = envelope.DocumentId, DisplayName = envelope.DocumentId }),
            AuthoringDocumentKind.GeneratorRequest when File.Exists(path) => GeneratorRequestDocument.Load(path),
            AuthoringDocumentKind.GeneratorRequest => new GeneratorRequestDocument(path, new EditableGeneratorRequest { Id = envelope.DocumentId, DisplayName = envelope.DocumentId }),
            AuthoringDocumentKind.StreamingScene when File.Exists(path) => StreamingSceneDocument.Load(path),
            AuthoringDocumentKind.StreamingScene => new StreamingSceneDocument(path, new Playroma.Sound.Streaming.Definitions.SoundSceneDefinition { Id = envelope.DocumentId, DisplayName = envelope.DocumentId }),
            _ => throw new NotSupportedException($"Recovery for document kind {envelope.DocumentKind} is not supported.")
        };
        Apply(document, envelope);
        return document;
    }

    public static void Apply(IAuthoringDocument document, AutosaveEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(envelope);
        if (document.Kind != envelope.DocumentKind) throw new InvalidOperationException("Autosave document kind does not match the target document.");
        string recoveredId = ReadDocumentId(envelope);
        if (!StringComparer.Ordinal.Equals(recoveredId, envelope.DocumentId))
            throw new InvalidDataException($"Autosave content declares document ID '{recoveredId}', but its envelope identifies '{envelope.DocumentId}'.");
        if (File.Exists(document.FilePath) && !StringComparer.Ordinal.Equals(document.DocumentId, envelope.DocumentId))
            throw new InvalidDataException($"The source document declares ID '{document.DocumentId}', but the autosave belongs to '{envelope.DocumentId}'.");
        document.ReplaceSerializedContent(envelope.Content, "Recover autosave");
    }

    private static string ReadDocumentId(AutosaveEnvelope envelope) => envelope.DocumentKind switch
    {
        AuthoringDocumentKind.SoundGraph => new Playroma.Sound.Graph.Serialization.SoundGraphSerializer().Load(envelope.Content).Id,
        AuthoringDocumentKind.GeneratorRequest => new EditableGeneratorRequestSerializer().Deserialize(envelope.Content).Id,
        AuthoringDocumentKind.StreamingScene => new Playroma.Sound.Streaming.Serialization.SoundSceneSerializer().Deserialize(envelope.Content).Id,
        _ => throw new NotSupportedException($"Recovery for document kind {envelope.DocumentKind} is not supported.")
    };

    private static string Extension(AuthoringDocumentKind kind) => kind switch
    {
        AuthoringDocumentKind.SoundGraph => ".playsound",
        AuthoringDocumentKind.GeneratorRequest => ".playgen",
        AuthoringDocumentKind.StreamingScene => ".playstream",
        _ => ".json"
    };
}
