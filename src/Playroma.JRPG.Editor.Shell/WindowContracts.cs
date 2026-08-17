namespace Playroma.JRPG.Editor.Shell;

/// <summary>
/// Identifies the major top-level editor experiences supported by the multi-window shell.
/// The list is intentionally broader than the windows currently migrated so later phases do
/// not need to invent incompatible key formats.
/// </summary>
public enum EditorWindowKind
{
    ProjectHub,
    MapEditor,
    EventEditor,
    EventCommands,
    EventCommandParameters,
    Database,
    ResourceManager,
    DataImporter,
    PluginManager,
    MapProperties,
    Validation,
    References,
    Output,
    BackgroundTasks,
    RawJson,
    ProjectSummary,
    Preferences,
    Deployment,
    RecoveredSessions,
    MusicStudio,
    SoundStudio,
    TileStudio,
    SpriteStudio,
    Custom,
    TextStudio
}

public enum WindowLoadState
{
    Created,
    Loading,
    Ready,
    Failed,
    Stale,
    Closing,
    Closed
}

public enum WindowPresentation
{
    Modeless,
    OwnedModeless,
    Modal
}

public enum WindowScope
{
    Application,
    Project
}

/// <summary>
/// Stable, case-insensitive identity for one editor window. The identity is not display text;
/// it should be a durable value such as "database", "map-editor", or "map:138/event:71".
/// </summary>
public readonly struct WindowKey : IEquatable<WindowKey>
{
    public WindowKey(EditorWindowKind kind, string identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new ArgumentException("A window identity is required.", nameof(identity));
        }

        Kind = kind;
        Identity = identity.Trim().Replace('\\', '/');
    }

    public EditorWindowKind Kind { get; }
    public string Identity { get; }

    public bool Equals(WindowKey other) =>
        Kind == other.Kind && string.Equals(Identity, other.Identity, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is WindowKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        Kind,
        StringComparer.OrdinalIgnoreCase.GetHashCode(Identity ?? string.Empty));

    public override string ToString() => $"{Kind}:{Identity ?? string.Empty}";

    public static bool operator ==(WindowKey left, WindowKey right) => left.Equals(right);
    public static bool operator !=(WindowKey left, WindowKey right) => !left.Equals(right);
}

/// <summary>
/// Complete request used to find or create a top-level editor window.
/// </summary>
public sealed record WindowRequest(
    EditorWindowKind Kind,
    string Identity,
    string? DisplayTitle = null,
    string? ToolTip = null,
    WindowPresentation Presentation = WindowPresentation.Modeless,
    WindowScope Scope = WindowScope.Project,
    WindowKey? OwnerKey = null,
    string? PlacementId = null,
    DocumentRequest? Document = null,
    NavigationLocation? Location = null)
{
    public WindowKey Key => new(Kind, Identity);

    /// <summary>
    /// Windows of the same type normally share size and placement. A request can provide a
    /// more specific ID when separate placement families are desirable.
    /// </summary>
    public string EffectivePlacementId => string.IsNullOrWhiteSpace(PlacementId)
        ? Kind.ToString()
        : PlacementId.Trim();
}

public sealed class EditorWindowEventArgs : EventArgs
{
    public EditorWindowEventArgs(IEditorWindow window) => Window = window;
    public IEditorWindow Window { get; }
}

public sealed class ActiveEditorWindowChangedEventArgs : EventArgs
{
    public ActiveEditorWindowChangedEventArgs(IEditorWindow? previous, IEditorWindow? current)
    {
        Previous = previous;
        Current = current;
    }

    public IEditorWindow? Previous { get; }
    public IEditorWindow? Current { get; }
}

/// <summary>
/// Platform-neutral lifetime contract for a top-level editor window.
/// </summary>
public interface IEditorWindow : IAsyncDisposable
{
    WindowRequest Request { get; }
    WindowKey Key { get; }
    string Title { get; }
    string? ToolTip { get; }
    bool IsDirty { get; }
    bool CanActivate { get; }
    bool IsVisible { get; }
    WindowLoadState LoadState { get; }
    event EventHandler? StateChanged;
    ValueTask LoadAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default);
    NavigationLocation? CaptureLocation();
    ValueTask RestoreLocationAsync(NavigationLocation location, CancellationToken cancellationToken = default);
}

/// <summary>
/// Registry and lifecycle service for modeless editor windows.
/// </summary>
public interface IWindowService
{
    IReadOnlyList<IEditorWindow> OpenWindows { get; }
    event EventHandler? WindowsChanged;
    event EventHandler<EditorWindowEventArgs>? WindowOpened;
    event EventHandler<EditorWindowEventArgs>? WindowClosed;

    bool TryGet(WindowKey key, out IEditorWindow? window);
    ValueTask<IEditorWindow> OpenAsync(WindowRequest request, CancellationToken cancellationToken = default);
    ValueTask<bool> CloseAsync(IEditorWindow window, CancellationToken cancellationToken = default);
    ValueTask<bool> CloseAllAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> CloseScopeAsync(WindowScope scope, CancellationToken cancellationToken = default);
    ValueTask<bool> CanCloseAllAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> CanCloseScopeAsync(WindowScope scope, CancellationToken cancellationToken = default);
    ValueTask CloseAllForcedAsync(CancellationToken cancellationToken = default);
    ValueTask CloseScopeForcedAsync(WindowScope scope, CancellationToken cancellationToken = default);
    void Activate(IEditorWindow window);
}

public interface IActiveWindowService
{
    IEditorWindow? ActiveWindow { get; }
    event EventHandler<ActiveEditorWindowChangedEventArgs>? ActiveWindowChanged;
}
