using System.Globalization;
using Playroma.JRPG.Application;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class DatabaseRecordCategoryPane : IDatabaseCategoryPane
{
    private readonly MvProjectSession _session;
    private readonly ISelectionService _selection;
    private readonly Func<DocumentRequest, Task> _documentOpener;
    private readonly Func<string, MvDocumentKind, int?, Task> _rawOpener;
    private readonly Func<DatabaseTextGenerationRequestedEventArgs, Task> _textGeneratorOpener;
    private IProjectDocumentLease? _lease;
    private DatabaseDocumentControl? _control;
    private DatabasePaneState? _deferredState;

    public DatabaseRecordCategoryPane(
        DatabaseCategoryDescriptor descriptor,
        MvProjectSession session,
        ISelectionService selection,
        Func<DocumentRequest, Task> documentOpener,
        Func<string, MvDocumentKind, int?, Task> rawOpener)
        : this(descriptor, session, selection, documentOpener, rawOpener, _ => Task.CompletedTask)
    {
    }

    public DatabaseRecordCategoryPane(
        DatabaseCategoryDescriptor descriptor,
        MvProjectSession session,
        ISelectionService selection,
        Func<DocumentRequest, Task> documentOpener,
        Func<string, MvDocumentKind, int?, Task> rawOpener,
        Func<DatabaseTextGenerationRequestedEventArgs, Task> textGeneratorOpener)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _documentOpener = documentOpener ?? throw new ArgumentNullException(nameof(documentOpener));
        _rawOpener = rawOpener ?? throw new ArgumentNullException(nameof(rawOpener));
        _textGeneratorOpener = textGeneratorOpener ?? throw new ArgumentNullException(nameof(textGeneratorOpener));
    }

    public DatabaseCategoryDescriptor Descriptor { get; }
    public Control View => _control ?? throw new InvalidOperationException("The category pane has not been loaded.");
    public bool IsLoaded => _control is not null;
    public bool HasPendingLocalChanges => _control?.HasPendingChanges == true;
    public event EventHandler? PendingStateChanged;
    public event EventHandler? NavigationStateChanged;

    public async ValueTask LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_control is not null) return;
        _lease = await _session.Workspace.AcquireDocumentAsync(
            Descriptor.RelativePath,
            cancellationToken).ConfigureAwait(true);
        _control = new DatabaseDocumentControl(
            _session,
            _lease.Document,
            Descriptor.DocumentKind,
            _selection,
            _documentOpener);
        _control.RawRecordRequested += OnRawRecordRequested;
        _control.RecordActivated += OnRecordActivated;
        _control.SelectionChanged += OnNavigationStateChanged;
        _control.PendingChangesChanged += OnPendingChangesChanged;
        _control.TextGenerationRequested += OnTextGenerationRequested;
        await _control.LoadAsync(cancellationToken).ConfigureAwait(true);
        if (_deferredState is not null)
        {
            await RestoreStateAsync(_deferredState, cancellationToken).ConfigureAwait(true);
            _deferredState = null;
        }
    }

    public ValueTask NavigateAsync(
        DatabaseNavigationTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_control is null) throw new InvalidOperationException("The category pane has not been loaded.");
        if (target.RecordId.HasValue) _control.SelectRecord(target.RecordId.Value);
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyPendingAsync(CancellationToken cancellationToken = default) =>
        _control?.ApplyPendingAsync(cancellationToken) ?? ValueTask.CompletedTask;

    public ValueTask DiscardPendingAsync(CancellationToken cancellationToken = default) =>
        _control?.DiscardPendingAsync(cancellationToken) ?? ValueTask.CompletedTask;

    public DatabasePaneState CaptureState() => _control is null
        ? _deferredState ?? new DatabasePaneState(Descriptor.Key)
        : new DatabasePaneState(
            Descriptor.Key,
            _control.SelectedRecordId,
            _control.SearchText,
            RecordPanelWidth: _control.RecordPanelWidth,
            AdvancedVisible: _control.AdvancedVisible);

    public ValueTask RestoreStateAsync(
        DatabasePaneState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        if (_control is null)
        {
            _deferredState = state;
            return ValueTask.CompletedTask;
        }
        _control.SearchText = state.SearchText;
        _control.RecordPanelWidth = state.RecordPanelWidth;
        _control.SetAdvancedVisible(state.AdvancedVisible);
        if (state.SelectedRecordId.HasValue) _control.SelectRecord(state.SelectedRecordId.Value);
        return ValueTask.CompletedTask;
    }

    public void SetAdvancedVisible(bool visible) => _control?.SetAdvancedVisible(visible);

    public async ValueTask DisposeAsync()
    {
        if (_control is not null)
        {
            _control.RawRecordRequested -= OnRawRecordRequested;
            _control.RecordActivated -= OnRecordActivated;
            _control.SelectionChanged -= OnNavigationStateChanged;
            _control.PendingChangesChanged -= OnPendingChangesChanged;
            _control.TextGenerationRequested -= OnTextGenerationRequested;
            _control.Dispose();
            _control = null;
        }
        if (_lease is not null)
        {
            await _lease.DisposeAsync().ConfigureAwait(true);
            _lease = null;
        }
    }

    private void OnRecordActivated(object? sender, int recordId)
    {
        _control?.FocusEditor();
        NavigationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnRawRecordRequested(object? sender, int recordId)
    {
        try
        {
            await _rawOpener(Descriptor.RelativePath, Descriptor.DocumentKind, recordId).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(View, exception.Message, "Could not open raw JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnTextGenerationRequested(object? sender, DatabaseTextGenerationRequestedEventArgs e)
    {
        try { await _textGeneratorOpener(e).ConfigureAwait(true); }
        catch (Exception exception)
        {
            MessageBox.Show(View, exception.Message, "Could not open Text Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnPendingChangesChanged(object? sender, EventArgs e) =>
        PendingStateChanged?.Invoke(this, EventArgs.Empty);

    private void OnNavigationStateChanged(object? sender, EventArgs e) =>
        NavigationStateChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class SystemDatabaseCategoryPane : IDatabaseCategoryPane
{
    private readonly MvProjectSession _session;
    private readonly Func<DirectTextGenerationRequestedEventArgs, Task> _textGeneratorOpener;
    private IProjectDocumentLease? _lease;
    private SystemDocumentControl? _control;
    private DatabasePaneState? _deferredState;

    public SystemDatabaseCategoryPane(DatabaseCategoryDescriptor descriptor, MvProjectSession session)
        : this(descriptor, session, _ => Task.CompletedTask)
    {
    }

    public SystemDatabaseCategoryPane(DatabaseCategoryDescriptor descriptor, MvProjectSession session, Func<DirectTextGenerationRequestedEventArgs, Task> textGeneratorOpener)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _textGeneratorOpener = textGeneratorOpener ?? throw new ArgumentNullException(nameof(textGeneratorOpener));
    }

    public DatabaseCategoryDescriptor Descriptor { get; }
    public Control View => _control ?? throw new InvalidOperationException("The System pane has not been loaded.");
    public bool IsLoaded => _control is not null;
    public bool HasPendingLocalChanges => _control?.HasPendingChanges == true;
    public event EventHandler? PendingStateChanged;
    public event EventHandler? NavigationStateChanged;

    public async ValueTask LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_control is not null) return;
        _lease = await _session.Workspace.AcquireDocumentAsync(
            Descriptor.RelativePath,
            cancellationToken).ConfigureAwait(true);
        _control = new SystemDocumentControl(_session, _lease.Document);
        _control.PendingChangesChanged += OnPendingChangesChanged;
        _control.TextGenerationRequested += OnTextGenerationRequested;
        _control.SectionChanged += OnNavigationStateChanged;
        if (_deferredState is not null)
        {
            await RestoreStateAsync(_deferredState, cancellationToken).ConfigureAwait(true);
            _deferredState = null;
        }
    }

    public ValueTask NavigateAsync(
        DatabaseNavigationTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_control is null) throw new InvalidOperationException("The System pane has not been loaded.");
        _control.SelectSection(target.Section switch
        {
            DatabaseSection.Types => SystemEditorSection.Types,
            DatabaseSection.Terms => SystemEditorSection.Terms,
            _ when !string.IsNullOrWhiteSpace(target.SelectionPath)
                && Enum.TryParse(target.SelectionPath, ignoreCase: true, out SystemEditorSection parsed) => parsed,
            _ => SystemEditorSection.General
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyPendingAsync(CancellationToken cancellationToken = default) =>
        _control?.ApplyPendingAsync(cancellationToken) ?? ValueTask.CompletedTask;

    public ValueTask DiscardPendingAsync(CancellationToken cancellationToken = default) =>
        _control?.DiscardPendingAsync(cancellationToken) ?? ValueTask.CompletedTask;

    public DatabasePaneState CaptureState() => _control is null
        ? _deferredState ?? new DatabasePaneState(Descriptor.Key)
        : new DatabasePaneState(
            Descriptor.Key,
            ActiveSection: _control.ActiveSection.ToString());

    public ValueTask RestoreStateAsync(
        DatabasePaneState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        if (_control is null)
        {
            _deferredState = state;
            return ValueTask.CompletedTask;
        }
        if (Enum.TryParse(state.ActiveSection, ignoreCase: true, out SystemEditorSection section))
        {
            _control.SelectSection(section);
        }
        return ValueTask.CompletedTask;
    }

    public void SetAdvancedVisible(bool visible)
    {
        // System, Types and Terms use their normal editor tabs. Raw JSON is opened separately.
    }

    public async ValueTask DisposeAsync()
    {
        if (_control is not null)
        {
            _control.PendingChangesChanged -= OnPendingChangesChanged;
            _control.TextGenerationRequested -= OnTextGenerationRequested;
            _control.SectionChanged -= OnNavigationStateChanged;
            _control.Dispose();
            _control = null;
        }
        if (_lease is not null)
        {
            await _lease.DisposeAsync().ConfigureAwait(true);
            _lease = null;
        }
    }

    private async void OnTextGenerationRequested(object? sender, DirectTextGenerationRequestedEventArgs e)
    {
        try { await _textGeneratorOpener(e).ConfigureAwait(true); }
        catch (Exception exception)
        {
            MessageBox.Show(View, exception.Message, "Could not open Text Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnPendingChangesChanged(object? sender, EventArgs e) =>
        PendingStateChanged?.Invoke(this, EventArgs.Empty);

    private void OnNavigationStateChanged(object? sender, EventArgs e) =>
        NavigationStateChanged?.Invoke(this, EventArgs.Empty);
}
