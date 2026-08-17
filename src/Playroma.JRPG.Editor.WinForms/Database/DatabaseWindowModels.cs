using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Database;

public enum DatabaseSection
{
    Records,
    CommonEvents,
    System,
    Types,
    Terms
}

public enum SystemEditorSection
{
    General,
    Options,
    PositionsAndVehicles,
    MusicAndSounds,
    GraphicsAndMotions,
    Types,
    Terms,
    BattleTest
}

public sealed record DatabaseNavigationTarget(
    string CategoryKey,
    MvDocumentKind DocumentKind,
    int? RecordId = null,
    DatabaseSection Section = DatabaseSection.Records,
    string? SelectionPath = null,
    int? EventPageIndex = null)
{
    public static DatabaseNavigationTarget FromDocumentRequest(DocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        MvDocumentKind kind = request.DatabaseKind ?? request.Kind switch
        {
            EditorDocumentKind.SystemData => MvDocumentKind.System,
            _ => MvDocumentKind.Actors
        };
        string category = request.Identity.StartsWith("database:", StringComparison.OrdinalIgnoreCase)
            ? request.Identity["database:".Length..]
            : kind.ToString();
        DatabaseSection section = kind switch
        {
            MvDocumentKind.CommonEvents => DatabaseSection.CommonEvents,
            MvDocumentKind.System when string.Equals(category, "Types", StringComparison.OrdinalIgnoreCase) => DatabaseSection.Types,
            MvDocumentKind.System when string.Equals(category, "Terms", StringComparison.OrdinalIgnoreCase) => DatabaseSection.Terms,
            MvDocumentKind.System => DatabaseSection.System,
            _ => DatabaseSection.Records
        };
        return new DatabaseNavigationTarget(
            category,
            kind,
            request.RecordId,
            section,
            request.EventSurface,
            request.EventPageIndex);
    }

    public DocumentRequest ToDocumentRequest(string? relativePath, string? displayTitle = null) =>
        Section switch
        {
            DatabaseSection.System or DatabaseSection.Types or DatabaseSection.Terms => new DocumentRequest(
                EditorDocumentKind.SystemData,
                "database:" + CategoryKey,
                relativePath,
                MvDocumentKind.System,
                DisplayTitle: displayTitle ?? CategoryKey,
                EventSurface: Section.ToString()),
            _ => new DocumentRequest(
                EditorDocumentKind.Database,
                "database:" + CategoryKey,
                relativePath,
                DocumentKind,
                RecordId,
                DisplayTitle: displayTitle ?? CategoryKey,
                EventSurface: Section.ToString(),
                EventPageIndex: EventPageIndex)
        };
}

public sealed record DatabaseCategoryDescriptor(
    string Key,
    string DisplayName,
    MvDocumentKind DocumentKind,
    string RelativePath,
    DatabaseSection Section = DatabaseSection.Records,
    string? PaneKey = null)
{
    public string EffectivePaneKey => string.IsNullOrWhiteSpace(PaneKey) ? Key : PaneKey;
}

public sealed record DatabasePaneState(
    string CategoryKey,
    int? SelectedRecordId = null,
    string SearchText = "",
    string ActiveSection = "",
    int RecordPanelWidth = 330,
    bool AdvancedVisible = false);

public enum DatabasePendingEditDecision
{
    Apply,
    Discard,
    Cancel
}

public interface IDatabasePendingEditSource
{
    bool HasPendingChanges { get; }
    event EventHandler? PendingChangesChanged;
    ValueTask ApplyPendingAsync(CancellationToken cancellationToken = default);
    ValueTask DiscardPendingAsync(CancellationToken cancellationToken = default);
}

public interface IDatabaseCategoryPane : IAsyncDisposable
{
    DatabaseCategoryDescriptor Descriptor { get; }
    Control View { get; }
    bool IsLoaded { get; }
    bool HasPendingLocalChanges { get; }
    event EventHandler? PendingStateChanged;
    event EventHandler? NavigationStateChanged;

    ValueTask LoadAsync(CancellationToken cancellationToken = default);
    ValueTask NavigateAsync(DatabaseNavigationTarget target, CancellationToken cancellationToken = default);
    ValueTask ApplyPendingAsync(CancellationToken cancellationToken = default);
    ValueTask DiscardPendingAsync(CancellationToken cancellationToken = default);
    DatabasePaneState CaptureState();
    ValueTask RestoreStateAsync(DatabasePaneState state, CancellationToken cancellationToken = default);
    void SetAdvancedVisible(bool visible);
}

public sealed class DatabaseNavigationHistory
{
    private readonly List<DatabaseNavigationTarget> _history = [];
    private int _position = -1;

    public bool CanGoBack => _position > 0;
    public bool CanGoForward => _position >= 0 && _position < _history.Count - 1;
    public DatabaseNavigationTarget? Current => _position >= 0 && _position < _history.Count ? _history[_position] : null;
    public event EventHandler? Changed;

    public void Visit(DatabaseNavigationTarget target, bool recordHistory = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (recordHistory)
        {
            if (_position < _history.Count - 1)
            {
                _history.RemoveRange(_position + 1, _history.Count - _position - 1);
            }
            if (!Equals(Current, target))
            {
                _history.Add(target);
                _position = _history.Count - 1;
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public DatabaseNavigationTarget? GoBack()
    {
        if (!CanGoBack) return Current;
        _position--;
        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    public DatabaseNavigationTarget? GoForward()
    {
        if (!CanGoForward) return Current;
        _position++;
        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }
}
