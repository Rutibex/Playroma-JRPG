using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Playroma.JRPG.Editor.Shell;

/// <summary>
/// Complete project-specific editor workspace state. Project data mutations remain owned by
/// <see cref="IRecoveryService"/>; this snapshot records windows, navigation and isolated edit
/// sessions which do not yet belong to the shared project transaction history.
/// </summary>
public sealed record ProjectWorkspaceSnapshot(
    string ProjectRoot,
    DateTimeOffset CapturedUtc,
    string? PrimaryWorkspaceStateJson,
    IReadOnlyList<WindowWorkspaceState> Windows,
    WindowIdentityState? ActiveWindow,
    IReadOnlyList<PrivateSessionRecoveryState> PrivateSessions,
    int Version = 1)
{
    public IReadOnlyList<WindowWorkspaceState> EffectiveWindows => Windows ?? [];
    public IReadOnlyList<PrivateSessionRecoveryState> EffectivePrivateSessions => PrivateSessions ?? [];
}

public sealed record WindowIdentityState(EditorWindowKind Kind, string Identity)
{
    public WindowKey ToWindowKey() => new(Kind, Identity);
    public static WindowIdentityState From(WindowKey key) => new(key.Kind, key.Identity);
}

public sealed record WindowWorkspaceState(
    WindowRequest Request,
    NavigationLocation? Location,
    ShellBounds? NormalBounds,
    bool Maximized,
    string? MonitorDeviceName,
    bool WasVisible,
    int ActivationOrder,
    string? CustomStateJson = null,
    int Version = 1)
{
    public WindowKey Key => Request.Key;
}

/// <summary>
/// Opaque, versioned local edit-session payload. The owning window is solely responsible for
/// interpreting DataJson; no Form, Control, service or document lease is serialized.
/// </summary>
public sealed record PrivateSessionRecoveryState(
    string SessionType,
    WindowRequest WindowRequest,
    string DisplayTitle,
    string DataJson,
    string? BaselineFingerprint,
    DateTimeOffset CapturedUtc,
    int Version = 1)
{
    public WindowKey Key => WindowRequest.Key;
}

public enum ShutdownLocalAction
{
    Apply,
    Discard,
    PreserveRecovery
}

public enum ShutdownProjectAction
{
    Save,
    Discard,
    PreserveRecovery
}

public sealed record ShutdownContribution(
    WindowKey WindowKey,
    string Title,
    string Description,
    bool CanApply,
    bool CanDiscard,
    bool CanRecover,
    int PendingItemCount = 1);

/// <summary>
/// Two-stage shutdown participant. Prepare must be observational and may not apply, discard or
/// otherwise mutate the session. Commit is called only after the user confirms the coordinated
/// project-close decision.
/// </summary>
public interface IShutdownParticipant
{
    ValueTask<ShutdownContribution?> PrepareShutdownAsync(
        CancellationToken cancellationToken = default);

    ValueTask<bool> CommitShutdownAsync(
        ShutdownLocalAction action,
        CancellationToken cancellationToken = default);

    ValueTask<PrivateSessionRecoveryState?> CaptureRecoveryStateAsync(
        CancellationToken cancellationToken = default);

    ValueTask RestoreRecoveryStateAsync(
        PrivateSessionRecoveryState state,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional window-specific state beyond the shared NavigationLocation contract.</summary>
public interface IWorkspaceStateContributor
{
    ValueTask<string?> CaptureWorkspaceStateJsonAsync(
        CancellationToken cancellationToken = default);

    ValueTask RestoreWorkspaceStateJsonAsync(
        string? stateJson,
        CancellationToken cancellationToken = default);
}

public interface IProjectWorkspaceStateStore
{
    ValueTask<ProjectWorkspaceSnapshot?> ReadAsync(
        string projectRoot,
        CancellationToken cancellationToken = default);

    ValueTask WriteAsync(
        ProjectWorkspaceSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(
        string projectRoot,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Late-bound bridge allowing the platform-neutral workspace service to request a WinForms
/// workspace snapshot without taking a dependency on WinForms.
/// </summary>
public sealed class WorkspaceRecoveryContextBroker
{
    private Func<CancellationToken, ValueTask<ProjectWorkspaceSnapshot?>>? _capture;

    public void SetCaptureProvider(
        Func<CancellationToken, ValueTask<ProjectWorkspaceSnapshot?>> capture) =>
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));

    public void ClearCaptureProvider() => _capture = null;

    public ValueTask<ProjectWorkspaceSnapshot?> CaptureAsync(
        CancellationToken cancellationToken = default) =>
        _capture is null
            ? ValueTask.FromResult<ProjectWorkspaceSnapshot?>(null)
            : _capture(cancellationToken);
}

public sealed class JsonProjectWorkspaceStateStore : IProjectWorkspaceStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _root;

    public JsonProjectWorkspaceStateStore(string? root = null) =>
        _root = root ?? AppDataPaths.WorkspaceState;

    public async ValueTask<ProjectWorkspaceSnapshot?> ReadAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        string path = GetPath(projectRoot);
        ProjectWorkspaceSnapshot? snapshot = await TryReadAsync(path, projectRoot, cancellationToken).ConfigureAwait(false);
        if (snapshot is not null) return snapshot;
        return await TryReadAsync(path + ".previous", projectRoot, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<ProjectWorkspaceSnapshot?> TryReadAsync(
        string path,
        string projectRoot,
        CancellationToken cancellationToken)
    {
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
            if (!IsSupportedSnapshot(snapshot, projectRoot)) return null;
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

    public async ValueTask WriteAsync(
        ProjectWorkspaceSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Directory.CreateDirectory(_root);
        string path = GetPath(snapshot.ProjectRoot);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        string backup = path + ".previous";
        try
        {
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                8192,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(path)) File.Copy(path, backup, overwrite: true);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public ValueTask DeleteAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = GetPath(projectRoot);
        if (File.Exists(path)) File.Delete(path);
        string backup = path + ".previous";
        if (File.Exists(backup)) File.Delete(backup);
        return ValueTask.CompletedTask;
    }

    private static bool IsSupportedSnapshot(
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
            || (!string.IsNullOrWhiteSpace(snapshot.ActiveWindow.Identity));
    }

    private string GetPath(string projectRoot) =>
        Path.Combine(_root, HashText(Path.GetFullPath(projectRoot)) + ".json");

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
