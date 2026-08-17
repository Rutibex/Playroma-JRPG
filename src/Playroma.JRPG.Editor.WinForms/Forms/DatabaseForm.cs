using Playroma.JRPG.Application;
using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Accessibility;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.Editor.WinForms.Eventing;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>
/// One project-scoped RPG Maker-style Database window. Category panes are created lazily and
/// share the project's mutable document cache, undo history, validation and save transaction.
/// </summary>
public sealed class DatabaseForm : EditorToolForm, IShutdownParticipant
{
    private readonly IWorkspaceService _workspace;
    private readonly ISelectionService _globalSelection;
    private readonly IWinFormsWindowService _windows;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly DatabaseSelectionContext _selectionContext;
    private readonly DatabaseNavigationHistory _history = new();
    private readonly Dictionary<string, IDatabaseCategoryPane> _panes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DatabasePaneState> _paneStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private readonly MenuStrip _menu = new() { Dock = DockStyle.Top };
    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly SplitContainer _mainSplit = ControlHelpers.CreateSplitContainer(
        Orientation.Vertical, 155, 118, 620, splitterWidth: 6);
    private readonly ListBox _categories = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        BorderStyle = BorderStyle.None,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 32,
        Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Regular),
        AccessibleName = "Database categories",
        AccessibleDescription = "Select Actors, Classes, Skills, Items, or another database category. Use Control Tab to move forward and Control Shift Tab to move backward.",
        AccessibleRole = AccessibleRole.List
    };
    private readonly Panel _paneHost = new() { Dock = DockStyle.Fill };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Select a Database category.",
        AccessibleName = "No database category selected"
    };
    private readonly Panel _loading = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label _loadingText = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Loading Database category…"
    };
    private readonly StatusStrip _status = new() { Dock = DockStyle.Bottom };
    private readonly ToolStripStatusLabel _locationStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _pendingStatus = new();
    private readonly ToolStripStatusLabel _projectStatus = new();
    private readonly Panel _bottom = new() { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(8) };
    private readonly Button _save = new() { Text = "&Save Project", AutoSize = true, MinimumSize = new Size(105, 30) };
    private readonly Button _close = new() { Text = "&Close", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _undo = new() { Text = "&Undo", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _redo = new() { Text = "&Redo", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly ToolStripButton _backButton = new("Back") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _forwardButton = new("Forward") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _advancedButton = new("Advanced") { CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Text };

    private MvProjectSession? _session;
    private DatabaseCategoryRegistry? _registry;
    private DatabaseCategoryDescriptor? _activeDescriptor;
    private IDatabaseCategoryPane? _activePane;
    private DatabaseNavigationTarget? _activeTarget;
    private CancellationTokenSource? _navigationCancellation;
    private bool _suppressCategorySelection;
    private bool _disposed;

    public DatabaseForm(
        WindowRequest request,
        IWorkspaceService workspace,
        ISelectionService selection,
        IWinFormsWindowService windows,
        ISettingsService settings,
        INotificationService notifications)
        : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _globalSelection = selection ?? throw new ArgumentNullException(nameof(selection));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _selectionContext = new DatabaseSelectionContext(_globalSelection);

        Name = nameof(DatabaseForm);
        Text = "Database";
        MinimumSize = new Size(960, 680);
        Size = new Size(1220, 800);
        ShowInTaskbar = true;
        KeyPreview = true;
        _mainSplit.FixedPanel = FixedPanel.Panel1;

        BuildMenu();
        MainMenuStrip = _menu;
        BuildToolbar();
        BuildLayout();
        WireEvents();
        Shown += (_, _) =>
        {
            AccessibilityMetadata.ApplyFormDefaults(this);
            if (ActiveControl is null) _categories.Focus();
        };
    }

    public override string Title
    {
        get
        {
            string? title = _session?.Workspace.Snapshot.Identity.ProjectTitle;
            string project = !string.IsNullOrWhiteSpace(title)
                ? title
                : _session?.Workspace.Snapshot.Identity.ProjectRoot is { } root
                    ? Path.GetFileName(root)
                    : string.Empty;
            string dirty = IsDirty ? " *" : string.Empty;
            return string.IsNullOrWhiteSpace(project) ? "Database" + dirty : $"Database — {project}{dirty}";
        }
    }

    public override bool IsDirty => _session?.State.HasUnsavedChanges == true || _panes.Values.Any(pane => pane.HasPendingLocalChanges);

    public ValueTask<ShutdownContribution?> PrepareShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int pending = _panes.Values.Distinct().Count(pane => pane.HasPendingLocalChanges);
        if (pending == 0) return ValueTask.FromResult<ShutdownContribution?>(null);
        ShutdownContribution contribution = new(
            Key,
            Title,
            $"{pending} Database section{(pending == 1 ? string.Empty : "s")} contain unapplied control changes.",
            CanApply: true,
            CanDiscard: true,
            CanRecover: false,
            PendingItemCount: pending);
        return ValueTask.FromResult<ShutdownContribution?>(contribution);
    }

    public async ValueTask<bool> CommitShutdownAsync(
        ShutdownLocalAction action,
        CancellationToken cancellationToken = default)
    {
        IDatabaseCategoryPane[] pending = _panes.Values
            .Distinct()
            .Where(pane => pane.HasPendingLocalChanges)
            .ToArray();
        if (pending.Length == 0) return true;
        if (action == ShutdownLocalAction.PreserveRecovery) return false;
        try
        {
            foreach (IDatabaseCategoryPane pane in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (action == ShutdownLocalAction.Apply)
                    await pane.ApplyPendingAsync(cancellationToken).ConfigureAwait(true);
                else
                    await pane.DiscardPendingAsync(cancellationToken).ConfigureAwait(true);
            }
            UpdateWindowState();
            return !_panes.Values.Any(pane => pane.HasPendingLocalChanges);
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not resolve Database changes", exception.Message, exception);
            return false;
        }
    }

    public ValueTask<PrivateSessionRecoveryState?> CaptureRecoveryStateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<PrivateSessionRecoveryState?>(null);
    }

    public ValueTask RestoreRecoveryStateAsync(
        PrivateSessionRecoveryState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public override NavigationLocation? CaptureLocation()
    {
        if (_activeTarget is null || _activeDescriptor is null) return base.CaptureLocation();
        DatabasePaneState state = _activePane?.CaptureState()
            ?? new DatabasePaneState(_activeDescriptor.Key, _activeTarget.RecordId);
        DatabaseNavigationTarget target = _activeTarget with
        {
            RecordId = state.SelectedRecordId ?? _activeTarget.RecordId,
            SelectionPath = string.IsNullOrWhiteSpace(state.ActiveSection) ? _activeTarget.SelectionPath : state.ActiveSection
        };
        return new NavigationLocation(
            target.ToDocumentRequest(_activeDescriptor.RelativePath, _activeDescriptor.DisplayName),
            SelectionPath: target.SelectionPath,
            RecordId: target.RecordId);
    }

    public override async ValueTask RestoreLocationAsync(
        NavigationLocation location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        DatabaseNavigationTarget target = DatabaseNavigationTarget.FromDocumentRequest(location.Document) with
        {
            RecordId = location.RecordId ?? location.Document.RecordId,
            SelectionPath = location.SelectionPath ?? location.Document.EventSurface
        };
        if (_registry is null)
        {
            _activeTarget = target;
            return;
        }
        await NavigateAsync(target, recordHistory: true, cancellationToken).ConfigureAwait(true);
    }

    public override async ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await ResolvePendingChangesAsync(
                "Some Database sections contain changes that have not been applied.\n\n" +
                "Yes: Apply all pending section changes and close.\n" +
                "No: Discard only the unapplied section buffers and close.\n" +
                "Cancel: Continue editing.",
                cancellationToken).ConfigureAwait(true))
        {
            return false;
        }
        CaptureSettings();
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(true);
        return true;
    }

    protected override async ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        _session = _workspace.CurrentSession
            ?? throw new InvalidOperationException("The Database cannot open without an active project.");
        _registry = DatabaseCategoryRegistry.Create(_session);
        if (_registry.Categories.Count == 0)
        {
            throw new InvalidOperationException("This project does not contain supported Database files.");
        }

        _workspace.StateChanged += OnWorkspaceStateChanged;
        _session.StateChanged += OnSessionStateChanged;
        _session.Undo.HistoryChanged += OnUndoHistoryChanged;

        RestoreSavedState();
        PopulateCategories();
        InstallContent(BuildRoot());

        DatabaseNavigationTarget target = ResolveInitialTarget();
        await NavigateAsync(target, recordHistory: true, cancellationToken).ConfigureAwait(true);
        UpdateWindowState();
    }

    protected override async ValueTask OnDisposeWindowAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = null;

        if (_session is not null)
        {
            _session.StateChanged -= OnSessionStateChanged;
            _session.Undo.HistoryChanged -= OnUndoHistoryChanged;
        }
        _workspace.StateChanged -= OnWorkspaceStateChanged;

        foreach (IDatabaseCategoryPane pane in _panes.Values.Distinct())
        {
            pane.PendingStateChanged -= OnPanePendingStateChanged;
            pane.NavigationStateChanged -= OnPaneNavigationStateChanged;
            await pane.DisposeAsync().ConfigureAwait(true);
        }
        _panes.Clear();
        _selectionContext.Clear();
        _lifetimeCancellation.Dispose();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_activeDescriptor is not null)
        {
            _selectionContext.Activate(_activeDescriptor.EffectivePaneKey);
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        _selectionContext.Suspend();
        base.OnDeactivate(e);
    }

    public override EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.EditUndo)
            return _session?.Undo.CanUndo == true ? EditorCommandStatus.Enabled("Undo Database Edit") : EditorCommandStatus.Disabled("Undo Database Edit");
        if (command == EditorCommandIds.EditRedo)
            return _session?.Undo.CanRedo == true ? EditorCommandStatus.Enabled("Redo Database Edit") : EditorCommandStatus.Disabled("Redo Database Edit");
        if (command == EditorCommandIds.EditFind) return EditorCommandStatus.Enabled("Find Database Record");
        if (command == EditorCommandIds.NavigateBack)
            return _history.CanGoBack ? EditorCommandStatus.Enabled("Previous Database Record") : EditorCommandStatus.Disabled("Previous Database Record");
        if (command == EditorCommandIds.NavigateForward)
            return _history.CanGoForward ? EditorCommandStatus.Enabled("Next Database Record") : EditorCommandStatus.Disabled("Next Database Record");
        return base.QueryCommand(command, context);
    }

    public override async ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.EditUndo) await UndoSafelyAsync().ConfigureAwait(true);
        else if (command == EditorCommandIds.EditRedo) await RedoSafelyAsync().ConfigureAwait(true);
        else if (command == EditorCommandIds.EditFind) FocusActiveSearch();
        else if (command == EditorCommandIds.NavigateBack) await NavigateHistoryAsync(back: true).ConfigureAwait(true);
        else if (command == EditorCommandIds.NavigateForward) await NavigateHistoryAsync(back: false).ConfigureAwait(true);
        else await base.ExecuteCommandAsync(command, context).ConfigureAwait(true);
        NotifyCommandStateChanged();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S))
        {
            _ = SaveProjectSafelyAsync();
            return true;
        }
        if (keyData == (Keys.Control | Keys.F))
        {
            FocusActiveSearch();
            return true;
        }
        if (keyData == (Keys.Control | Keys.G))
        {
            GoToRecordId();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.Left))
        {
            _ = NavigateHistoryAsync(back: true);
            return true;
        }
        if (keyData == (Keys.Alt | Keys.Right))
        {
            _ = NavigateHistoryAsync(back: false);
            return true;
        }
        if (keyData == (Keys.Control | Keys.Tab))
        {
            SelectAdjacentCategory(1);
            return true;
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            SelectAdjacentCategory(-1);
            return true;
        }
        if (keyData == (Keys.Control | Keys.Z))
        {
            _ = UndoSafelyAsync();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Y))
        {
            _ = RedoSafelyAsync();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Control BuildRoot()
    {
        Panel root = new() { Dock = DockStyle.Fill };
        root.Controls.Add(_mainSplit);
        root.Controls.Add(_status);
        root.Controls.Add(_bottom);
        root.Controls.Add(_toolbar);
        root.Controls.Add(_menu);
        return root;
    }

    private void BuildLayout()
    {
        Label heading = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = "&Database",
            Padding = new Padding(10, 10, 4, 8),
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            UseMnemonic = true,
            AccessibleName = "Database categories heading"
        };
        Panel categoryPanel = new() { Dock = DockStyle.Fill, Padding = new Padding(5) };
        categoryPanel.Controls.Add(_categories);
        categoryPanel.Controls.Add(heading);
        _mainSplit.Panel1.Controls.Add(categoryPanel);

        _paneHost.Controls.Add(_empty);
        _loading.Controls.Add(_loadingText);
        _paneHost.Controls.Add(_loading);
        _mainSplit.Panel2.Controls.Add(_paneHost);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        buttons.Controls.AddRange([_undo, _redo, _save, _close]);
        _bottom.Controls.Add(buttons);

        _status.Items.AddRange([_locationStatus, _pendingStatus, _projectStatus]);
    }

    private void BuildMenu()
    {
        ToolStripMenuItem file = new("&File");
        ToolStripMenuItem save = new("&Save Project") { ShortcutKeys = Keys.Control | Keys.S };
        ToolStripMenuItem close = new("&Close Database") { ShortcutKeys = Keys.Control | Keys.W };
        save.Click += async (_, _) => await SaveProjectSafelyAsync().ConfigureAwait(true);
        close.Click += (_, _) => Close();
        file.DropDownItems.AddRange([save, new ToolStripSeparator(), close]);

        ToolStripMenuItem edit = new("&Edit");
        ToolStripMenuItem undo = new("&Undo") { ShortcutKeys = Keys.Control | Keys.Z };
        ToolStripMenuItem redo = new("&Redo") { ShortcutKeys = Keys.Control | Keys.Y };
        ToolStripMenuItem find = new("Find Records") { ShortcutKeys = Keys.Control | Keys.F };
        undo.Click += async (_, _) => await UndoSafelyAsync().ConfigureAwait(true);
        redo.Click += async (_, _) => await RedoSafelyAsync().ConfigureAwait(true);
        find.Click += (_, _) => FocusActiveSearch();
        edit.DropDownItems.AddRange([undo, redo, new ToolStripSeparator(), find]);

        ToolStripMenuItem navigate = new("&Navigate");
        ToolStripMenuItem back = new("&Back") { ShortcutKeys = Keys.Alt | Keys.Left };
        ToolStripMenuItem forward = new("&Forward") { ShortcutKeys = Keys.Alt | Keys.Right };
        ToolStripMenuItem goTo = new("&Go to Record ID…") { ShortcutKeys = Keys.Control | Keys.G };
        back.Click += async (_, _) => await NavigateHistoryAsync(back: true).ConfigureAwait(true);
        forward.Click += async (_, _) => await NavigateHistoryAsync(back: false).ConfigureAwait(true);
        goTo.Click += (_, _) => GoToRecordId();
        navigate.DropDownItems.AddRange([back, forward, new ToolStripSeparator(), goTo]);

        ToolStripMenuItem view = new("&View");
        ToolStripMenuItem advanced = new("Advanced Details") { CheckOnClick = true };
        ToolStripMenuItem raw = new("Open Current File as Raw JSON");
        advanced.CheckedChanged += (_, _) => SetAdvancedVisible(advanced.Checked);
        raw.Click += async (_, _) => await OpenCurrentRawAsync().ConfigureAwait(true);
        view.DropDownItems.AddRange([advanced, raw]);

        _menu.Items.AddRange([file, edit, navigate, view]);
    }

    private void BuildToolbar()
    {
        _toolbar.Items.Add(_backButton);
        _toolbar.Items.Add(_forwardButton);
        _toolbar.Items.Add(new ToolStripSeparator());
        ToolStripButton save = new("Save") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        ToolStripButton undo = new("Undo") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        ToolStripButton redo = new("Redo") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        ToolStripButton find = new("Find") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _toolbar.Items.AddRange([save, undo, redo, find, new ToolStripSeparator(), _advancedButton]);
        _backButton.Click += async (_, _) => await NavigateHistoryAsync(back: true).ConfigureAwait(true);
        _forwardButton.Click += async (_, _) => await NavigateHistoryAsync(back: false).ConfigureAwait(true);
        save.Click += async (_, _) => await SaveProjectSafelyAsync().ConfigureAwait(true);
        undo.Click += async (_, _) => await UndoSafelyAsync().ConfigureAwait(true);
        redo.Click += async (_, _) => await RedoSafelyAsync().ConfigureAwait(true);
        find.Click += (_, _) => FocusActiveSearch();
        _advancedButton.CheckedChanged += (_, _) => SetAdvancedVisible(_advancedButton.Checked);
    }

    private void WireEvents()
    {
        _categories.SelectedIndexChanged += OnCategorySelectionChanged;
        _categories.DrawItem += DrawCategoryItem;
        _save.Click += async (_, _) => await SaveProjectSafelyAsync().ConfigureAwait(true);
        _close.Click += (_, _) => Close();
        _undo.Click += async (_, _) => await UndoSafelyAsync().ConfigureAwait(true);
        _redo.Click += async (_, _) => await RedoSafelyAsync().ConfigureAwait(true);
        _history.Changed += (_, _) => UpdateHistoryButtons();
        _mainSplit.SplitterMoved += (_, _) => CaptureSettings();
        FormClosed += (_, _) => _navigationCancellation?.Cancel();
    }

    private void DrawCategoryItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _categories.Items.Count) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool focused = (e.State & DrawItemState.Focus) != 0;
        Color background = selected ? SystemColors.Highlight : _categories.BackColor;
        Color foreground = selected ? SystemColors.HighlightText : _categories.ForeColor;
        using SolidBrush backgroundBrush = new(background);
        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
        Rectangle indicator = new(e.Bounds.Left, e.Bounds.Top, selected ? 4 : 0, e.Bounds.Height);
        if (selected)
        {
            using SolidBrush indicatorBrush = new(SystemColors.HotTrack);
            e.Graphics.FillRectangle(indicatorBrush, indicator);
        }
        Rectangle textBounds = new(e.Bounds.Left + 10, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 14), e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            Convert.ToString(_categories.Items[e.Index]) ?? string.Empty,
            _categories.Font,
            textBounds,
            foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -3, -3), foreground, background);
    }

    private void PopulateCategories()
    {
        if (_registry is null) return;
        _suppressCategorySelection = true;
        try
        {
            _categories.BeginUpdate();
            _categories.Items.Clear();
            foreach (DatabaseCategoryDescriptor descriptor in _registry.Categories)
            {
                _categories.Items.Add(new CategoryListItem(descriptor));
            }
        }
        finally
        {
            _categories.EndUpdate();
            _suppressCategorySelection = false;
        }
    }

    private DatabaseNavigationTarget ResolveInitialTarget()
    {
        DatabaseNavigationTarget? requested = _activeTarget;
        if (requested is null && Request.Location?.Document is { } locationDocument && IsSpecificDatabaseRequest(locationDocument))
        {
            requested = DatabaseNavigationTarget.FromDocumentRequest(locationDocument) with
            {
                RecordId = Request.Location.RecordId ?? locationDocument.RecordId,
                SelectionPath = Request.Location.SelectionPath ?? locationDocument.EventSurface
            };
        }
        if (requested is null && Request.Document is { } document && IsSpecificDatabaseRequest(document))
        {
            requested = DatabaseNavigationTarget.FromDocumentRequest(document);
        }
        if (requested is not null && ResolveDescriptor(requested) is not null) return requested;

        DatabaseWindowLayoutSettings saved = _settings.Current.EffectiveDatabaseWindowLayout;
        DatabaseCategoryDescriptor descriptor = _registry!.Find(saved.SelectedCategory)
            ?? _registry.Categories[0];
        DatabasePaneState? state = GetSavedPaneState(descriptor.Key);
        return new DatabaseNavigationTarget(
            descriptor.Key,
            descriptor.DocumentKind,
            state?.SelectedRecordId,
            descriptor.Section,
            state?.ActiveSection);
    }

    private async ValueTask NavigateAsync(
        DatabaseNavigationTarget target,
        bool recordHistory,
        CancellationToken cancellationToken)
    {
        DatabaseCategoryDescriptor descriptor = ResolveDescriptor(target)
            ?? throw new InvalidOperationException($"Database category '{target.CategoryKey}' is not available in this project.");
        DatabaseNavigationTarget normalized = target with
        {
            CategoryKey = descriptor.Key,
            DocumentKind = descriptor.DocumentKind,
            Section = descriptor.Section
        };

        if (_activeDescriptor is not null && _activePane is not null)
        {
            DatabasePaneState captured = _activePane.CaptureState() with { CategoryKey = _activeDescriptor.Key };
            _paneStates[_activeDescriptor.Key] = captured;
        }

        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellation.Token);
        CancellationToken token = _navigationCancellation.Token;

        ShowLoading($"Loading {descriptor.DisplayName}…");
        try
        {
            IDatabaseCategoryPane pane = await GetOrCreatePaneAsync(descriptor, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            _selectionContext.Activate(descriptor.EffectivePaneKey);
            ShowPane(pane.View);
            await pane.NavigateAsync(normalized, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();

            _activeDescriptor = descriptor;
            _activePane = pane;
            DatabasePaneState live = pane.CaptureState();
            _activeTarget = normalized with
            {
                RecordId = live.SelectedRecordId ?? normalized.RecordId,
                SelectionPath = string.IsNullOrWhiteSpace(live.ActiveSection) ? normalized.SelectionPath : live.ActiveSection
            };
            SelectCategory(descriptor);
            _history.Visit(_activeTarget, recordHistory);
            UpdateWindowState();
            CaptureSettings();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer category navigation superseded this load.
        }
        catch (Exception exception)
        {
            ShowPane(new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Text = $"Could not load {descriptor.DisplayName}.\r\n\r\n{exception}"
            });
            _notifications.Error("Database category failed to load", exception.Message, exception);
        }
        finally
        {
            HideLoading();
        }
    }

    private async ValueTask<IDatabaseCategoryPane> GetOrCreatePaneAsync(
        DatabaseCategoryDescriptor requestedDescriptor,
        CancellationToken cancellationToken)
    {
        string paneKey = requestedDescriptor.EffectivePaneKey;
        if (!_panes.TryGetValue(paneKey, out IDatabaseCategoryPane? pane))
        {
            DatabaseCategoryDescriptor constructionDescriptor = requestedDescriptor.Section is DatabaseSection.System or DatabaseSection.Types or DatabaseSection.Terms
                ? _registry!.Find("System") ?? requestedDescriptor
                : requestedDescriptor;
            ISelectionService localSelection = _selectionContext.GetOrCreate(paneKey);
            pane = constructionDescriptor.Section switch
            {
                DatabaseSection.System or DatabaseSection.Types or DatabaseSection.Terms =>
                    new SystemDatabaseCategoryPane(constructionDescriptor, _session!, OpenDirectTextStudioTargetAsync),
                _ => new DatabaseRecordCategoryPane(
                    constructionDescriptor,
                    _session!,
                    localSelection,
                    OpenDocumentFromPaneAsync,
                    OpenRawAsync,
                    OpenTextStudioTargetAsync)
            };
            pane.PendingStateChanged += OnPanePendingStateChanged;
            pane.NavigationStateChanged += OnPaneNavigationStateChanged;
            _panes.Add(paneKey, pane);
            if (GetSavedPaneState(requestedDescriptor.Key) is { } saved)
            {
                await pane.RestoreStateAsync(saved, cancellationToken).ConfigureAwait(true);
            }
        }
        await pane.LoadAsync(cancellationToken).ConfigureAwait(true);
        if (GetSavedPaneState(requestedDescriptor.Key) is { } state)
        {
            await pane.RestoreStateAsync(state, cancellationToken).ConfigureAwait(true);
        }
        pane.SetAdvancedVisible(_advancedButton.Checked);
        return pane;
    }

    private DatabaseCategoryDescriptor? ResolveDescriptor(DatabaseNavigationTarget target) =>
        _registry?.Find(target.CategoryKey)
        ?? _registry?.Find(target.DocumentKind, target.Section);

    private static bool IsSpecificDatabaseRequest(DocumentRequest request) =>
        request.DatabaseKind.HasValue
        || !string.Equals(request.Identity, "database", StringComparison.OrdinalIgnoreCase);

    private async Task OpenDocumentFromPaneAsync(DocumentRequest document)
    {
        switch (document.Kind)
        {
            case EditorDocumentKind.EventEditor:
            {
                WindowRequest request = new(
                    EditorWindowKind.EventEditor,
                    EventWindowIdentity.Create(document),
                    document.DisplayTitle,
                    document.RelativePath,
                    WindowPresentation.Modal,
                    WindowScope.Project,
                    OwnerKey: Key,
                    PlacementId: "EventEditor",
                    Document: document,
                    Location: new NavigationLocation(document, RecordId: document.RecordId));
                await _windows.ShowDialogAsync(request, this, _lifetimeCancellation.Token).ConfigureAwait(true);
                if (_activePane is not null && _activeTarget is not null)
                {
                    await _activePane.NavigateAsync(_activeTarget, _lifetimeCancellation.Token).ConfigureAwait(true);
                }
                break;
            }
            case EditorDocumentKind.RawJson:
                await OpenRawAsync(
                    document.RelativePath ?? throw new InvalidOperationException("Raw JSON request has no path."),
                    document.DatabaseKind ?? _activeDescriptor?.DocumentKind ?? MvDocumentKind.Actors,
                    document.RecordId).ConfigureAwait(true);
                break;
            case EditorDocumentKind.ProjectFile when
                !string.IsNullOrWhiteSpace(document.RelativePath) &&
                Path.GetExtension(document.RelativePath).Equals(".png", StringComparison.OrdinalIgnoreCase):
            {
                WindowRequest request = new(
                    EditorWindowKind.SpriteStudio,
                    Identity: "sprite-studio:" + document.RelativePath.Replace('\\', '/'),
                    DisplayTitle: "Playroma Sprite Studio — " + (document.DisplayTitle ?? Path.GetFileName(document.RelativePath)),
                    ToolTip: document.RelativePath,
                    Document: document,
                    Location: new NavigationLocation(document, SelectionPath: document.EventSurface, RecordId: document.RecordId));
                await _windows.OpenAsync(request, _lifetimeCancellation.Token).ConfigureAwait(true);
                break;
            }
            default:
                throw new NotSupportedException($"The Database cannot open document kind '{document.Kind}'.");
        }
    }

    private async Task OpenDirectTextStudioTargetAsync(DirectTextGenerationRequestedEventArgs request)
    {
        IEditorWindow window = await _windows.OpenAsync(new WindowRequest(
            EditorWindowKind.TextStudio, "text-studio", "Playroma Text Studio",
            Scope: WindowScope.Project, OwnerKey: Key, PlacementId: "PlayromaTextStudio"), _lifetimeCancellation.Token).ConfigureAwait(true);
        if (window is not TextStudioForm studio)
            throw new InvalidOperationException("The Text Studio window does not expose target activation.");
        studio.ActivateDirectTarget(request.Target);
        _windows.Activate(window);
    }

    private async Task OpenTextStudioTargetAsync(DatabaseTextGenerationRequestedEventArgs target)
    {
        IEditorWindow window = await _windows.OpenAsync(new WindowRequest(
            EditorWindowKind.TextStudio,
            "text-studio",
            "Playroma Text Studio",
            Scope: WindowScope.Project,
            OwnerKey: Key,
            PlacementId: "PlayromaTextStudio"), _lifetimeCancellation.Token).ConfigureAwait(true);
        if (window is not TextStudioForm studio)
            throw new InvalidOperationException("The Text Studio window does not expose database target activation.");
        if (target.OpenBatchBuilder)
            studio.OpenBatchBuilder();
        else
            await studio.ActivateDatabaseTargetAsync(
                target.Kind, target.RelativePath, target.RecordId, target.FieldKey, _lifetimeCancellation.Token).ConfigureAwait(true);
        _windows.Activate(window);
    }

    private async Task OpenRawAsync(string relativePath, MvDocumentKind kind, int? recordId)
    {
        DocumentRequest document = new(
            EditorDocumentKind.RawJson,
            "file:" + relativePath,
            relativePath,
            kind,
            recordId,
            DisplayTitle: Path.GetFileName(relativePath));
        WindowRequest request = new(
            EditorWindowKind.RawJson,
            "file:" + relativePath,
            document.DisplayTitle,
            relativePath,
            WindowPresentation.Modeless,
            WindowScope.Project,
            OwnerKey: Key,
            PlacementId: "RawJson",
            Document: document,
            Location: new NavigationLocation(document, RecordId: recordId));
        await _windows.OpenAsync(request, _lifetimeCancellation.Token).ConfigureAwait(true);
    }

    private async Task OpenCurrentRawAsync()
    {
        if (_activeDescriptor is null) return;
        int? recordId = _activePane?.CaptureState().SelectedRecordId;
        await OpenRawAsync(_activeDescriptor.RelativePath, _activeDescriptor.DocumentKind, recordId).ConfigureAwait(true);
    }

    private async Task SaveProjectSafelyAsync()
    {
        try
        {
            IDatabaseCategoryPane[] pending = _panes.Values.Distinct().Where(pane => pane.HasPendingLocalChanges).ToArray();
            if (pending.Length > 0)
            {
                DialogResult dialogResult = MessageBox.Show(
                    this,
                    "Some Database sections contain unapplied local changes. Apply those section changes before saving the project?",
                    "Apply Database Changes",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);
                if (dialogResult != DialogResult.OK) return;
                foreach (IDatabaseCategoryPane pane in pending)
                {
                    await pane.ApplyPendingAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
                }
            }
            ProjectSaveResult saveResult = await _workspace.SaveAsync(cancellationToken: _lifetimeCancellation.Token).ConfigureAwait(true);
            if (!saveResult.Succeeded)
            {
                _notifications.Warning("Save not completed", "The project save was blocked. Review the validation and output windows for details.");
            }
            UpdateWindowState();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not save project", exception.Message, exception);
            MessageBox.Show(this, exception.Message, "Could not save project", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task UndoSafelyAsync()
    {
        try
        {
            if (_session?.Undo.CanUndo == true)
            {
                await _session.Undo.UndoAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            _notifications.Error("Undo failed", exception.Message, exception);
        }
        UpdateWindowState();
    }

    private async Task RedoSafelyAsync()
    {
        try
        {
            if (_session?.Undo.CanRedo == true)
            {
                await _session.Undo.RedoAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            _notifications.Error("Redo failed", exception.Message, exception);
        }
        UpdateWindowState();
    }

    private async Task NavigateHistoryAsync(bool back)
    {
        DatabaseNavigationTarget? target = back ? _history.GoBack() : _history.GoForward();
        if (target is null) return;
        await NavigateAsync(target, recordHistory: false, _lifetimeCancellation.Token).ConfigureAwait(true);
    }

    private async ValueTask<bool> ResolvePendingChangesAsync(string message, CancellationToken cancellationToken)
    {
        IDatabaseCategoryPane[] pending = _panes.Values.Distinct().Where(pane => pane.HasPendingLocalChanges).ToArray();
        if (pending.Length == 0) return true;
        DialogResult result = MessageBox.Show(
            this,
            message,
            "Unapplied Database Changes",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button1);
        if (result == DialogResult.Cancel) return false;
        foreach (IDatabaseCategoryPane pane in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result == DialogResult.Yes)
            {
                await pane.ApplyPendingAsync(cancellationToken).ConfigureAwait(true);
            }
            else
            {
                await pane.DiscardPendingAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        return true;
    }

    private void RestoreSavedState()
    {
        DatabaseWindowLayoutSettings layout = _settings.Current.EffectiveDatabaseWindowLayout;
        ControlHelpers.SetSplitterDistanceSafe(_mainSplit, Math.Min(layout.CategoryPanelWidth, 360));
        _advancedButton.Checked = layout.AdvancedVisible;
        foreach (DatabaseCategoryViewSettings item in layout.EffectiveCategories)
        {
            _paneStates[item.CategoryKey] = new DatabasePaneState(
                item.CategoryKey,
                item.SelectedRecordId,
                item.SearchText,
                item.ActiveSection,
                item.RecordPanelWidth,
                item.AdvancedVisible);
        }
    }

    private void CaptureSettings()
    {
        if (_registry is null) return;
        if (_activeDescriptor is not null && _activePane is not null)
        {
            _paneStates[_activeDescriptor.Key] = _activePane.CaptureState() with { CategoryKey = _activeDescriptor.Key };
        }
        List<DatabaseCategoryViewSettings> categories = [];
        foreach (DatabaseCategoryDescriptor descriptor in _registry.Categories)
        {
            DatabasePaneState? state = GetLiveOrSavedState(descriptor);
            if (state is null) continue;
            categories.Add(new DatabaseCategoryViewSettings(
                descriptor.Key,
                state.SelectedRecordId,
                state.SearchText,
                state.ActiveSection,
                state.RecordPanelWidth,
                state.AdvancedVisible));
        }
        DatabaseWindowLayoutSettings layout = new(
            SelectedCategory: _activeDescriptor?.Key ?? _settings.Current.EffectiveDatabaseWindowLayout.SelectedCategory,
            Categories: categories,
            CategoryPanelWidth: _mainSplit.SplitterDistance,
            AdvancedVisible: _advancedButton.Checked,
            Version: 1);
        _settings.Update(current => current with { DatabaseWindowLayout = layout, Version = EditorSettingsMigrator.CurrentVersion });
    }

    private DatabasePaneState? GetLiveOrSavedState(DatabaseCategoryDescriptor descriptor)
    {
        if (_activeDescriptor?.Key == descriptor.Key && _activePane is not null)
        {
            return _activePane.CaptureState() with { CategoryKey = descriptor.Key };
        }
        if (_paneStates.TryGetValue(descriptor.Key, out DatabasePaneState? saved)) return saved;
        if (_panes.TryGetValue(descriptor.EffectivePaneKey, out IDatabaseCategoryPane? pane) && pane.IsLoaded)
        {
            return pane.CaptureState() with { CategoryKey = descriptor.Key };
        }
        return null;
    }

    private DatabasePaneState? GetSavedPaneState(string categoryKey) =>
        _paneStates.TryGetValue(categoryKey, out DatabasePaneState? state) ? state : null;

    private void SetAdvancedVisible(bool visible)
    {
        if (_advancedButton.Checked != visible) _advancedButton.Checked = visible;
        foreach (IDatabaseCategoryPane pane in _panes.Values.Distinct()) pane.SetAdvancedVisible(visible);
        CaptureSettings();
    }

    private void GoToRecordId()
    {
        if (_activePane?.View is not DatabaseDocumentControl database) return;
        using Form prompt = new()
        {
            Text = "Go to Record ID",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(330, 118),
            AutoScaleMode = AutoScaleMode.Dpi
        };
        NumericUpDown id = new()
        {
            Minimum = 1,
            Maximum = 999999,
            Value = Math.Clamp(database.SelectedRecordId ?? 1, 1, 999999),
            Width = 150,
            Location = new Point(145, 17),
            ThousandsSeparator = true
        };
        Label label = new() { Text = "Record ID", AutoSize = true, Location = new Point(18, 21) };
        Button ok = new() { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(145, 68), Size = new Size(78, 28) };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(231, 68), Size = new Size(78, 28) };
        prompt.Controls.AddRange([label, id, ok, cancel]);
        prompt.AcceptButton = ok;
        prompt.CancelButton = cancel;
        if (prompt.ShowDialog(this) == DialogResult.OK) database.JumpToId(decimal.ToInt32(id.Value));
    }

    private void FocusActiveSearch()
    {
        if (_activePane?.View is DatabaseDocumentControl database)
        {
            database.FocusSearch();
            return;
        }
        Control? search = FindControl<TextBox>(_activePane?.View, box =>
            string.Equals(box.PlaceholderText, "Search records…", StringComparison.OrdinalIgnoreCase));
        search?.Focus();
    }

    private static T? FindControl<T>(Control? root, Func<T, bool> predicate) where T : Control
    {
        if (root is null) return null;
        if (root is T candidate && predicate(candidate)) return candidate;
        foreach (Control child in root.Controls)
        {
            T? found = FindControl(child, predicate);
            if (found is not null) return found;
        }
        return null;
    }

    private void SelectAdjacentCategory(int delta)
    {
        if (_categories.Items.Count == 0) return;
        int current = Math.Max(0, _categories.SelectedIndex);
        int next = (current + delta + _categories.Items.Count) % _categories.Items.Count;
        _categories.SelectedIndex = next;
    }

    private void SelectCategory(DatabaseCategoryDescriptor descriptor)
    {
        _suppressCategorySelection = true;
        try
        {
            for (int index = 0; index < _categories.Items.Count; index++)
            {
                if (_categories.Items[index] is CategoryListItem item
                    && string.Equals(item.Descriptor.Key, descriptor.Key, StringComparison.OrdinalIgnoreCase))
                {
                    _categories.SelectedIndex = index;
                    break;
                }
            }
        }
        finally
        {
            _suppressCategorySelection = false;
        }
    }

    private void ShowPane(Control view)
    {
        _paneHost.SuspendLayout();
        try
        {
            foreach (Control control in _paneHost.Controls.Cast<Control>().ToArray())
            {
                if (ReferenceEquals(control, _loading)) continue;
                control.Visible = false;
            }
            if (!_paneHost.Controls.Contains(view)) _paneHost.Controls.Add(view);
            view.Dock = DockStyle.Fill;
            view.Visible = true;
            view.BringToFront();
            _loading.BringToFront();
        }
        finally
        {
            _paneHost.ResumeLayout(true);
        }
    }

    private void ShowLoading(string text)
    {
        _loadingText.Text = text;
        _loading.Visible = true;
        _loading.BringToFront();
    }

    private void HideLoading() => _loading.Visible = false;

    private void UpdateWindowState()
    {
        Text = Title;
        NotifyStateChanged();
        _undo.Enabled = _session?.Undo.CanUndo == true;
        _redo.Enabled = _session?.Undo.CanRedo == true;
        _undo.Text = _session?.Undo.UndoDescription is { Length: > 0 } undo ? "Undo " + undo : "Undo";
        _redo.Text = _session?.Undo.RedoDescription is { Length: > 0 } redo ? "Redo " + redo : "Redo";
        _save.Enabled = _session?.State.CanSave == true;
        _locationStatus.Text = _activeDescriptor is null
            ? "Database"
            : _activeTarget?.RecordId is int id
                ? $"{_activeDescriptor.DisplayName} — ID {id:D4}"
                : _activeDescriptor.DisplayName;
        int pending = _panes.Values.Distinct().Count(pane => pane.HasPendingLocalChanges);
        _pendingStatus.Text = pending == 0 ? string.Empty : $"{pending} unapplied section{(pending == 1 ? string.Empty : "s")}";
        _projectStatus.Text = _session?.State.HasUnsavedChanges == true ? "Project modified" : "Saved";
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        _backButton.Enabled = _history.CanGoBack;
        _forwardButton.Enabled = _history.CanGoForward;
    }

    private async void OnCategorySelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressCategorySelection || _categories.SelectedItem is not CategoryListItem item || _session is null) return;
        DatabasePaneState? saved = GetSavedPaneState(item.Descriptor.Key);
        DatabaseNavigationTarget target = new(
            item.Descriptor.Key,
            item.Descriptor.DocumentKind,
            saved?.SelectedRecordId,
            item.Descriptor.Section,
            saved?.ActiveSection);
        await NavigateAsync(target, recordHistory: true, _lifetimeCancellation.Token).ConfigureAwait(true);
    }

    private void OnPanePendingStateChanged(object? sender, EventArgs e) => UpdateWindowState();

    private void OnPaneNavigationStateChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _activePane) || _activeDescriptor is null || _activeTarget is null) return;
        DatabasePaneState state = _activePane.CaptureState();
        _paneStates[_activeDescriptor.Key] = state with { CategoryKey = _activeDescriptor.Key };
        _activeTarget = _activeTarget with
        {
            RecordId = state.SelectedRecordId,
            SelectionPath = string.IsNullOrWhiteSpace(state.ActiveSection) ? _activeTarget.SelectionPath : state.ActiveSection
        };
        _history.Visit(_activeTarget, recordHistory: true);
        UpdateWindowState();
        CaptureSettings();
    }

    private void OnWorkspaceStateChanged(object? sender, WorkspaceStateChangedEventArgs e) => Ui(UpdateWindowState);
    private void OnSessionStateChanged(object? sender, EventArgs e) => Ui(UpdateWindowState);
    private void OnUndoHistoryChanged(object? sender, EventArgs e) => Ui(UpdateWindowState);

    private void Ui(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }
        action();
    }

    private sealed record CategoryListItem(DatabaseCategoryDescriptor Descriptor)
    {
        public override string ToString() => Descriptor.DisplayName;
    }
}
