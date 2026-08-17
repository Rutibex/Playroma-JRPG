using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.JRPG.Application;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Documents.Saving;

namespace Playroma.JRPG.Editor.Shell;

public sealed record RecoveryDocument(string RelativePath, string StorageName, string OriginalSha256);
public sealed record RecoveryFileMutation(
    FileMutationKind Kind,
    string? SourceRelativePath,
    string? DestinationRelativePath,
    string? StorageName,
    string Description);
public sealed record RecoveryManifest(
    string ProjectRoot,
    string? ProjectTitle,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<RecoveryDocument> Documents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<DocumentRequest>? OpenDocuments = null,
    IReadOnlyList<RecoveryFileMutation>? FileMutations = null,
    string? WorkspaceStorageName = null,
    int PrivateSessionCount = 0,
    int Version = 4)
{
    public IReadOnlyList<RecoveryFileMutation> Files => FileMutations ?? [];
    [JsonIgnore]
    public IReadOnlyList<DocumentRequest> LegacyOpenDocuments => OpenDocuments ?? [];
}
public sealed record RecoveryCandidate(string DirectoryPath, RecoveryManifest Manifest);

public interface IRecoveryService
{
    ValueTask<IReadOnlyList<RecoveryCandidate>> FindAsync(CancellationToken cancellationToken = default);
    ValueTask<RecoveryCandidate?> WriteAsync(
        MvProjectSession session,
        ProjectWorkspaceSnapshot? workspaceSnapshot = null,
        CancellationToken cancellationToken = default);
    ValueTask<ProjectWorkspaceSnapshot?> ReadWorkspaceAsync(
        RecoveryCandidate candidate,
        CancellationToken cancellationToken = default);
    ValueTask RestoreAsync(MvProjectSession session, RecoveryCandidate candidate, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(RecoveryCandidate candidate, CancellationToken cancellationToken = default);
    ValueTask DeleteForProjectAsync(string projectRoot, CancellationToken cancellationToken = default);
}

public sealed class RecoveryService : IRecoveryService, IDisposable
{
    private const string CurrentPointerFile = "current.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private readonly string _root;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public RecoveryService(string? root = null) => _root = root ?? AppDataPaths.Recovery;

    public async ValueTask<IReadOnlyList<RecoveryCandidate>> FindAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root)) return [];
        List<RecoveryCandidate> candidates = [];
        string[] projectDirectories;
        try
        {
            projectDirectories = Directory.EnumerateDirectories(_root).ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        foreach (string projectDirectory in projectDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                RecoveryCandidate? candidate = await FindLatestValidCandidateAsync(
                    projectDirectory,
                    cancellationToken).ConfigureAwait(false);
                if (candidate is not null) candidates.Add(candidate);
            }
            catch (IOException)
            {
                // A recovery generation may be replaced or removed while candidates are scanned.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore unreadable recovery folders and keep scanning the remaining projects.
            }
        }
        return candidates.OrderByDescending(candidate => candidate.Manifest.CreatedUtc).ToArray();
    }

    public async ValueTask<RecoveryCandidate?> WriteAsync(
        MvProjectSession session,
        ProjectWorkspaceSnapshot? workspaceSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await WriteCoreAsync(
                session,
                workspaceSnapshot,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async ValueTask<RecoveryCandidate?> WriteCoreAsync(
        MvProjectSession session,
        ProjectWorkspaceSnapshot? workspaceSnapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        string projectRoot = session.Workspace.Snapshot.Identity.ProjectRoot;
        if (workspaceSnapshot is not null && !PathsEqual(projectRoot, workspaceSnapshot.ProjectRoot))
        {
            throw new InvalidOperationException("The workspace snapshot belongs to a different project.");
        }

        string projectKey = HashText(Path.GetFullPath(projectRoot))[..24];
        string projectDirectory = Path.Combine(_root, projectKey);
        string generationName = "snapshot-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..8];
        string temporaryDirectory = Path.Combine(projectDirectory, "." + generationName + ".tmp");
        string finalDirectory = Path.Combine(projectDirectory, generationName);
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(temporaryDirectory);
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "documents"));
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "files"));
        List<RecoveryDocument> documents = [];
        List<RecoveryFileMutation> files = [];
        try
        {
            foreach (ProjectDocumentHandle handle in session.Workspace.Documents.Where(handle => handle.State == MutableDocumentState.Modified))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMutableProjectDocument document = await session.Workspace.OpenDocumentAsync(handle.RelativePath, cancellationToken).ConfigureAwait(false);
                if (document is not MutableJsonProjectDocument json) continue;
                byte[] bytes = json.CaptureGeneratedBytes();
                string storageName = HashText(handle.RelativePath) + ".json";
                await File.WriteAllBytesAsync(Path.Combine(temporaryDirectory, "documents", storageName), bytes, cancellationToken).ConfigureAwait(false);
                documents.Add(new RecoveryDocument(handle.RelativePath, storageName, document.OriginalFingerprint.Sha256 ?? string.Empty));
            }
            foreach (PendingFileMutation mutation in session.Editing.Files.PendingMutations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? storageName = null;
                if (mutation.Kind is FileMutationKind.Create or FileMutationKind.Replace)
                {
                    if (string.IsNullOrWhiteSpace(mutation.StagedContentPath) || !File.Exists(mutation.StagedContentPath))
                    {
                        throw new FileNotFoundException("The staged file content required for recovery is missing.", mutation.StagedContentPath);
                    }
                    storageName = mutation.Id.ToString("N") + Path.GetExtension(mutation.DestinationRelativePath);
                    await CopyFileAsync(
                        mutation.StagedContentPath,
                        Path.Combine(temporaryDirectory, "files", storageName),
                        cancellationToken).ConfigureAwait(false);
                }
                files.Add(new RecoveryFileMutation(
                    mutation.Kind,
                    mutation.SourceRelativePath,
                    mutation.DestinationRelativePath,
                    storageName,
                    mutation.Description));
            }

            bool hasPrivateSessions = workspaceSnapshot?.EffectivePrivateSessions.Count > 0;
            if (documents.Count == 0 && files.Count == 0 && !hasPrivateSessions)
            {
                Directory.Delete(temporaryDirectory, recursive: true);
                return null;
            }

            string? workspaceStorageName = null;
            if (workspaceSnapshot is not null)
            {
                workspaceStorageName = "workspace.json";
                await using FileStream workspaceStream = new(
                    Path.Combine(temporaryDirectory, workspaceStorageName),
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    8192,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                await JsonSerializer.SerializeAsync(
                    workspaceStream,
                    workspaceSnapshot,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await workspaceStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            RecoveryManifest manifest = new(
                projectRoot,
                session.Workspace.Snapshot.Identity.ProjectTitle,
                DateTimeOffset.UtcNow,
                documents,
                OpenDocuments: null,
                FileMutations: files,
                WorkspaceStorageName: workspaceStorageName,
                PrivateSessionCount: workspaceSnapshot?.EffectivePrivateSessions.Count ?? 0);
            await using (FileStream stream = new(
                Path.Combine(temporaryDirectory, "manifest.json"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                8192,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            Directory.Move(temporaryDirectory, finalDirectory);
            await PublishGenerationAsync(projectDirectory, generationName, cancellationToken).ConfigureAwait(false);
            CleanupOldGenerations(projectDirectory, keep: 2);
            return new RecoveryCandidate(finalDirectory, manifest);
        }
        catch
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true);
            throw;
        }
    }

    public async ValueTask<ProjectWorkspaceSnapshot?> ReadWorkspaceAsync(
        RecoveryCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        string? storageName = candidate.Manifest.WorkspaceStorageName;
        if (string.IsNullOrWhiteSpace(storageName)) return null;
        string path = Path.Combine(candidate.DirectoryPath, storageName);
        if (!File.Exists(path)) return null;
        try
        {
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                8192,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            ProjectWorkspaceSnapshot? snapshot = await JsonSerializer.DeserializeAsync<ProjectWorkspaceSnapshot>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            if (!IsSupportedWorkspaceSnapshot(snapshot, candidate.Manifest.ProjectRoot)) return null;
            return snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async ValueTask RestoreAsync(MvProjectSession session, RecoveryCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!PathsEqual(session.Workspace.Snapshot.Identity.ProjectRoot, candidate.Manifest.ProjectRoot))
        {
            throw new InvalidOperationException("The recovery data belongs to a different project.");
        }
        foreach (RecoveryDocument recoveryDocument in candidate.Manifest.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMutableProjectDocument document = await session.Workspace.OpenDocumentAsync(recoveryDocument.RelativePath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(recoveryDocument.OriginalSha256)
                && !string.Equals(document.OriginalFingerprint.Sha256, recoveryDocument.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Recovery cannot be applied because '{recoveryDocument.RelativePath}' changed on disk.");
            }
            if (document is not MutableJsonProjectDocument json) continue;
            byte[] bytes = await File.ReadAllBytesAsync(
                Path.Combine(candidate.DirectoryPath, "documents", recoveryDocument.StorageName),
                cancellationToken).ConfigureAwait(false);
            json.RestoreGeneratedBytes(bytes);
        }
        foreach (RecoveryFileMutation file in candidate.Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PendingFileMutation mutation;
            if (file.Kind is FileMutationKind.Create or FileMutationKind.Replace)
            {
                if (string.IsNullOrWhiteSpace(file.DestinationRelativePath) || string.IsNullOrWhiteSpace(file.StorageName))
                {
                    throw new InvalidDataException("The recovery file mutation is incomplete.");
                }
                string recoveredContent = Path.Combine(candidate.DirectoryPath, "files", file.StorageName);
                mutation = await session.Editing.Files.CreateStagedMutationAsync(
                    file.Kind,
                    recoveredContent,
                    file.Kind == FileMutationKind.Replace ? file.DestinationRelativePath : null,
                    file.DestinationRelativePath,
                    file.Description,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(file.SourceRelativePath))
                {
                    throw new InvalidDataException("The recovery path mutation is incomplete.");
                }
                mutation = await session.Editing.Files.CreatePathMutationAsync(
                    file.Kind,
                    file.SourceRelativePath,
                    file.DestinationRelativePath,
                    file.Description,
                    cancellationToken).ConfigureAwait(false);
            }
            session.Editing.Files.Stage(mutation);
        }
    }

    public async ValueTask DeleteAsync(RecoveryCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? parent = Directory.GetParent(candidate.DirectoryPath)?.FullName;
            if (parent is not null && File.Exists(Path.Combine(parent, CurrentPointerFile)))
            {
                if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
            }
            else if (Directory.Exists(candidate.DirectoryPath))
            {
                Directory.Delete(candidate.DirectoryPath, recursive: true);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DeleteForProjectAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string projectKey = HashText(Path.GetFullPath(projectRoot))[..24];
            string projectDirectory = Path.Combine(_root, projectKey);
            if (Directory.Exists(projectDirectory)) Directory.Delete(projectDirectory, recursive: true);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async ValueTask<RecoveryCandidate?> FindLatestValidCandidateAsync(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        string? preferred = await ResolveCurrentGenerationAsync(
            projectDirectory,
            cancellationToken).ConfigureAwait(false);
        HashSet<string> visited = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            visited.Add(Path.GetFullPath(preferred));
            RecoveryCandidate? current = await TryReadCandidateAsync(
                preferred,
                cancellationToken).ConfigureAwait(false);
            if (current is not null) return current;
        }

        IEnumerable<string> fallbacks;
        try
        {
            fallbacks = Directory.EnumerateDirectories(projectDirectory, "snapshot-*")
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (IOException)
        {
            fallbacks = [];
        }
        catch (UnauthorizedAccessException)
        {
            fallbacks = [];
        }

        foreach (string fallback in fallbacks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(Path.GetFullPath(fallback))) continue;
            RecoveryCandidate? candidate = await TryReadCandidateAsync(
                fallback,
                cancellationToken).ConfigureAwait(false);
            if (candidate is not null) return candidate;
        }

        string legacyManifest = Path.Combine(projectDirectory, "manifest.json");
        if (File.Exists(legacyManifest)
            && visited.Add(Path.GetFullPath(projectDirectory)))
        {
            return await TryReadCandidateAsync(projectDirectory, cancellationToken).ConfigureAwait(false);
        }
        return null;
    }

    private async ValueTask<string?> ResolveCurrentGenerationAsync(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        string legacyManifest = Path.Combine(projectDirectory, "manifest.json");
        if (File.Exists(legacyManifest)) return projectDirectory;
        string pointer = Path.Combine(projectDirectory, CurrentPointerFile);
        if (File.Exists(pointer))
        {
            try
            {
                string json = await File.ReadAllTextAsync(pointer, cancellationToken).ConfigureAwait(false);
                string? generation = JsonSerializer.Deserialize<string>(json, SerializerOptions);
                if (!string.IsNullOrWhiteSpace(generation))
                {
                    string candidate = Path.Combine(projectDirectory, generation);
                    if (File.Exists(Path.Combine(candidate, "manifest.json"))) return candidate;
                }
            }
            catch (JsonException)
            {
                // Fall back to the most recent complete generation.
            }
            catch (IOException)
            {
                // Fall back to the most recent complete generation.
            }
            catch (UnauthorizedAccessException)
            {
                // Fall back to the most recent complete generation.
            }
        }

        try
        {
            return Directory.EnumerateDirectories(projectDirectory, "snapshot-*")
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .FirstOrDefault(path => File.Exists(Path.Combine(path, "manifest.json")));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async ValueTask<RecoveryCandidate?> TryReadCandidateAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath)) return null;
        try
        {
            await using FileStream stream = new(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                8192,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            RecoveryManifest? manifest = await JsonSerializer.DeserializeAsync<RecoveryManifest>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            if (manifest is null
                || manifest.Version > 4
                || string.IsNullOrWhiteSpace(manifest.ProjectRoot)
                || manifest.Documents is null
                || manifest.Documents.Any(document =>
                    document is null
                    || string.IsNullOrWhiteSpace(document.RelativePath)
                    || string.IsNullOrWhiteSpace(document.StorageName))
                || manifest.LegacyOpenDocuments.Any(document => document is null)
                || manifest.Files.Any(file => file is null))
            {
                return null;
            }
            if (manifest.Documents.Any(document =>
                    !File.Exists(Path.Combine(directory, "documents", document.StorageName))))
            {
                return null;
            }
            if (manifest.Files.Any(file =>
                    (file.Kind is FileMutationKind.Create or FileMutationKind.Replace)
                    && (string.IsNullOrWhiteSpace(file.StorageName)
                        || !File.Exists(Path.Combine(directory, "files", file.StorageName)))))
            {
                return null;
            }
            if (!string.IsNullOrWhiteSpace(manifest.WorkspaceStorageName)
                && !File.Exists(Path.Combine(directory, manifest.WorkspaceStorageName)))
            {
                return null;
            }
            return new RecoveryCandidate(directory, manifest);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsSupportedWorkspaceSnapshot(
        ProjectWorkspaceSnapshot? snapshot,
        string projectRoot)
    {
        if (snapshot is null || snapshot.Version > 1 || string.IsNullOrWhiteSpace(snapshot.ProjectRoot)) return false;
        if (!PathsEqual(snapshot.ProjectRoot, projectRoot)) return false;
        if (snapshot.EffectiveWindows.Any(window =>
                window is null
                || window.Request is null
                || string.IsNullOrWhiteSpace(window.Request.Identity))) return false;
        if (snapshot.EffectivePrivateSessions.Any(state =>
                state is null
                || state.WindowRequest is null
                || string.IsNullOrWhiteSpace(state.SessionType)
                || string.IsNullOrWhiteSpace(state.WindowRequest.Identity)
                || string.IsNullOrWhiteSpace(state.DataJson))) return false;
        return snapshot.ActiveWindow is null
            || !string.IsNullOrWhiteSpace(snapshot.ActiveWindow.Identity);
    }

    private static async ValueTask PublishGenerationAsync(
        string projectDirectory,
        string generationName,
        CancellationToken cancellationToken)
    {
        string pointer = Path.Combine(projectDirectory, CurrentPointerFile);
        string temporary = pointer + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(generationName, SerializerOptions),
            Encoding.UTF8,
            cancellationToken).ConfigureAwait(false);
        File.Move(temporary, pointer, overwrite: true);
    }

    private static void CleanupOldGenerations(string projectDirectory, int keep)
    {
        string[] generations;
        try
        {
            generations = Directory.EnumerateDirectories(projectDirectory, "snapshot-*")
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        foreach (string obsolete in generations.Skip(Math.Max(1, keep)))
        {
            try { Directory.Delete(obsolete, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        string[] temporaryDirectories;
        try
        {
            temporaryDirectories = Directory.EnumerateDirectories(projectDirectory, ".*.tmp").ToArray();
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        foreach (string temporary in temporaryDirectories)
        {
            try { Directory.Delete(temporary, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async ValueTask CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    public void Dispose() => _writeGate.Dispose();

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
