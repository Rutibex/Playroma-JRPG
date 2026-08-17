using Playroma.JRPG.Application;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Documents;

/// <summary>
/// Reusable record-category surface used by the dedicated Database window.
/// The control keeps the existing raw-backed Phase 8/9 editors while exposing
/// navigation, pending-buffer and view-state hooks to DatabaseForm.
/// </summary>
public sealed class DatabaseDocumentControl : UserControl
{
    private readonly DatabaseEditorHostControl _host;

    public DatabaseDocumentControl(
        MvProjectSession session,
        IMutableProjectDocument document,
        MvDocumentKind kind,
        ISelectionService selection,
        Func<DocumentRequest, Task>? documentOpener = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selection);
        Dock = DockStyle.Fill;
        _host = new DatabaseEditorHostControl(session, document, kind, selection, documentOpener);
        _host.RawRecordRequested += (_, id) => RawRecordRequested?.Invoke(this, id);
        _host.RecordActivated += (_, id) => RecordActivated?.Invoke(this, id);
        _host.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        _host.PendingChangesChanged += (_, _) => PendingChangesChanged?.Invoke(this, EventArgs.Empty);
        _host.TextGenerationRequested += (_, e) => TextGenerationRequested?.Invoke(this, e);
        Controls.Add(_host);
    }

    public event EventHandler<int>? RawRecordRequested;
    public event EventHandler<int>? RecordActivated;
    public event EventHandler? SelectionChanged;
    public event EventHandler? PendingChangesChanged;
    public event EventHandler<DatabaseTextGenerationRequestedEventArgs>? TextGenerationRequested;

    public int? SelectedRecordId => _host.SelectedRecordId;
    public string SearchText
    {
        get => _host.SearchText;
        set => _host.SearchText = value;
    }
    public int RecordPanelWidth
    {
        get => _host.RecordPanelWidth;
        set => _host.RecordPanelWidth = value;
    }
    public bool AdvancedVisible => _host.AdvancedVisible;
    public bool HasPendingChanges => _host.HasPendingChanges;

    public ValueTask LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public void SelectRecord(int recordId) => _host.SelectRecord(recordId);
    public void JumpToId(int recordId) => _host.JumpToId(recordId);
    public void FocusSearch() => _host.FocusSearch();
    public void FocusEditor() => _host.FocusEditor();
    public void SetAdvancedVisible(bool visible) => _host.SetAdvancedVisible(visible);
    public ValueTask ApplyPendingAsync(CancellationToken cancellationToken = default) => _host.ApplyPendingAsync(cancellationToken);
    public ValueTask DiscardPendingAsync(CancellationToken cancellationToken = default) => _host.DiscardPendingAsync(cancellationToken);
}
