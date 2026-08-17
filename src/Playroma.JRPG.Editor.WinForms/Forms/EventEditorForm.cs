using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Accessibility;
using Playroma.JRPG.Editor.WinForms.Application;
using Playroma.JRPG.Editor.WinForms.Eventing;
using Playroma.JRPG.Editor.WinForms.Eventing.CommandEditing;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.JRPG.Eventing;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.Editor.WinForms.PluginTags;
using Playroma.JRPG.Workspaces;
using Playroma.Text.RpgMakerMV;
using Playroma.Text.Core.Dialogue;
using Playroma.JRPG.Editor.WinForms.Dialogs;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>
/// Dedicated RPG Maker-style Event Editor. All edits are staged in EventEditSession and become
/// one targeted project transaction only when Apply or OK is selected.
/// </summary>
public sealed class EventEditorForm : EditorToolForm, IShutdownParticipant
{
    private const string CommandClipboardFormat = "Playroma.JRPG.EventCommands";
    private const string PageClipboardFormat = "Playroma.JRPG.EventPage";

    private readonly IWorkspaceService _workspace;
    private readonly IWinFormsWindowService _windows;
    private readonly INotificationService? _notifications;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly EventCommandRegistry _registry;
    private readonly EventStructureProjector _projector = new();
    private readonly MenuStrip _menu = new() { Dock = DockStyle.Top };
    private readonly ToolStrip _commandToolbar = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill, AccessibleName = "Event name" };
    private readonly TextBox _note = new() { Dock = DockStyle.Fill, AccessibleName = "Event note" };
    private readonly Label _identity = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3, 7, 8, 3) };
    private readonly TabControl _pageTabs = new() { Dock = DockStyle.Fill, Height = 30, Multiline = false, AccessibleName = "Event pages" };
    private readonly FlowLayoutPanel _pageButtons = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
    private readonly Panel _settingsHost = new() { Dock = DockStyle.Fill, Padding = new Padding(4), AccessibleName = "Event page settings" };
    private readonly VirtualEventCommandListControl _commands = new()
    {
        AccessibleName = "Event contents",
        AccessibleDescription = "Event commands. Insert adds a command, Enter edits, Delete removes, and Shift+F10 opens the command menu."
    };
    private readonly TextBox _search = new() { Width = 220, PlaceholderText = "Search this event…", AccessibleName = "Search event contents" };
    private readonly TextBox _rawOwner = RawBox();
    private readonly TextBox _rawCommand = RawBox();
    private readonly ListBox _diagnostics = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly SplitContainer _rightSplit = ControlHelpers.CreateSplitContainer(
        Orientation.Horizontal, 430, 0, 0, splitterWidth: 6);
    private readonly Panel _conflictBanner = new() { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 5, 8, 5), Visible = false };
    private readonly Label _conflictText = new() { Text = "This event changed outside this window.", AutoSize = true, Margin = new Padding(3, 7, 12, 3) };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _ok = new() { Text = "&OK", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _cancel = new() { Text = "&Cancel", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _apply = new() { Text = "&Apply", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly ToolStripButton _undoButton = new("Undo");
    private readonly ToolStripButton _redoButton = new("Redo");

    private IProjectDocumentLease? _lease;
    private MvProjectSession? _project;
    private MutableJsonProjectDocument? _document;
    private EventEditSession? _editSession;
    private ProjectRecordLookup? _recordLookup;
    private EventCommandFormatter? _formatter;
    private EventListAnalyzer? _analyzer;
    private EventCommandProjectCatalog? _commandCatalog;
    private EventCommandUsageService? _commandUsage;
    private EventCommandWorkflowCoordinator? _commandWorkflow;
    private EventStructureDocument? _structure;
    private EventPageSettingsControl? _mapSettings;
    private TroopEventPageSettingsControl? _troopSettings;
    private CommonEventSettingsPanel? _commonSettings;
    private bool _loadingUi;
    private bool _updatingSessionFromUi;
    private bool _closeApproved;
    private bool _closePromptRunning;
    private bool _advancedVisible;
    private int? _selectedRawIndex;

    // Backward-compatible constructor retained for older Phase 10 smoke-test call sites.
    // Production code should pass the real IWinFormsWindowService overload below.
    public EventEditorForm(
        WindowRequest request,
        IWorkspaceService workspace,
        INotificationService? notifications)
        : this(request, workspace, LegacyStandaloneWindowService.Instance, notifications)
    {
    }

    public EventEditorForm(
        WindowRequest request,
        IWorkspaceService workspace,
        IWinFormsWindowService windows,
        INotificationService? notifications = null)
        : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _notifications = notifications;
        _registry = EventCommandRegistry.CreateBuiltIn(FindRepositoryRoot());

        Name = nameof(EventEditorForm);
        MinimumSize = new Size(940, 650);
        Size = new Size(1200, 790);
        ShowInTaskbar = false;
        KeyPreview = true;
        _rightSplit.FixedPanel = FixedPanel.Panel2;
        _rightSplit.Panel2Collapsed = true;

        BuildMenu();
        BuildPageButtons();
        BuildCommandToolbar();
        BuildLayout();
        WireEvents();
        Shown += (_, _) =>
        {
            AccessibilityMetadata.ApplyFormDefaults(this);
            _commands.Focus();
        };
    }

    public override string Title
    {
        get
        {
            EventEditSession? session = _editSession;
            if (session is null) return Request.DisplayTitle ?? "Event Editor";
            string name = string.IsNullOrWhiteSpace(session.Name) ? EventLabel(session) : session.Name;
            return session.Surface switch
            {
                EventSurfaceKind.MapEvent => $"ID:{session.OwnerId:D3} — {name} — Event Editor",
                EventSurfaceKind.CommonEvent => $"Common Event {session.OwnerId:D4} — {name}",
                EventSurfaceKind.TroopEvent => $"Troop {session.OwnerId:D4} — Battle Event Editor",
                _ => "Event Editor"
            };
        }
    }

    public override bool IsDirty => _editSession?.IsDirty == true;

    public override async ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await ConfirmCloseAsync(cancellationToken).ConfigureAwait(true);
    }

    public ValueTask<ShutdownContribution?> PrepareShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_editSession?.IsDirty != true)
        {
            return ValueTask.FromResult<ShutdownContribution?>(null);
        }
        ShutdownContribution contribution = new(
            Key,
            Title,
            $"Event page {(_editSession.ActivePageIndex + 1)} contains unapplied changes.",
            CanApply: !_editSession.HasConflict,
            CanDiscard: true,
            CanRecover: true);
        return ValueTask.FromResult<ShutdownContribution?>(contribution);
    }

    public async ValueTask<bool> CommitShutdownAsync(
        ShutdownLocalAction action,
        CancellationToken cancellationToken = default)
    {
        EventEditSession? session = _editSession;
        if (session is null || !session.IsDirty) return true;
        switch (action)
        {
            case ShutdownLocalAction.Apply:
                if (session.HasConflict) return false;
                FlushCurrentControls();
                try
                {
                    await session.CommitAsync(cancellationToken).ConfigureAwait(true);
                    if (_recordLookup is not null) await _recordLookup.RefreshAsync(cancellationToken).ConfigureAwait(true);
                    if (_commandCatalog is not null) await _commandCatalog.RefreshAsync(cancellationToken).ConfigureAwait(true);
                    RefreshAll(_selectedRawIndex);
                    NotifyStateChanged();
                    return !session.IsDirty && !session.HasConflict;
                }
                catch (Exception exception)
                {
                    _notifications?.Error("Could not apply event changes", exception.Message, exception);
                    return false;
                }
            case ShutdownLocalAction.Discard:
                session.RevertToBaseline();
                RefreshAll(_selectedRawIndex);
                NotifyStateChanged();
                return true;
            case ShutdownLocalAction.PreserveRecovery:
                return true;
            default:
                return false;
        }
    }

    public ValueTask<PrivateSessionRecoveryState?> CaptureRecoveryStateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EventEditSession? session = _editSession;
        if (session is null || !session.IsDirty)
        {
            return ValueTask.FromResult<PrivateSessionRecoveryState?>(null);
        }
        FlushCurrentControls();
        EventEditorRecoveryPayload payload = new(
            session.WorkingOwnerJson,
            session.ActivePageIndex,
            _selectedRawIndex,
            Version: 1);
        PrivateSessionRecoveryState state = new(
            "event-editor",
            Request with { Location = CaptureLocation() },
            Title,
            JsonSerializer.Serialize(payload),
            session.BaselineFingerprint,
            DateTimeOffset.UtcNow,
            Version: 1);
        return ValueTask.FromResult<PrivateSessionRecoveryState?>(state);
    }

    public ValueTask RestoreRecoveryStateAsync(
        PrivateSessionRecoveryState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        if (_editSession is null || !string.Equals(state.SessionType, "event-editor", StringComparison.Ordinal))
        {
            return ValueTask.CompletedTask;
        }
        EventEditorRecoveryPayload? payload = JsonSerializer.Deserialize<EventEditorRecoveryPayload>(state.DataJson);
        if (payload is null || payload.Version > 1) return ValueTask.CompletedTask;
        _editSession.RestoreRecoveredWorkingState(
            payload.WorkingOwnerJson,
            payload.ActivePageIndex,
            state.BaselineFingerprint);
        _selectedRawIndex = payload.SelectedRawIndex;
        RefreshAll(_selectedRawIndex);
        NotifyStateChanged();
        return ValueTask.CompletedTask;
    }

    public override NavigationLocation? CaptureLocation()
    {
        EventEditSession? session = _editSession;
        DocumentRequest? request = Request.Document;
        if (session is null || request is null) return base.CaptureLocation();
        DocumentRequest current = EventWindowIdentity.WithAddress(request, session.CurrentAddress) with
        {
            Identity = Request.Identity,
            DisplayTitle = Title
        };
        return new NavigationLocation(
            current,
            SelectionPath: _selectedRawIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RecordId: session.Surface == EventSurfaceKind.MapEvent ? null : session.OwnerId,
            MapId: session.MapId);
    }

    public override ValueTask RestoreLocationAsync(
        NavigationLocation location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        if (_editSession is null) return ValueTask.CompletedTask;
        if (location.Document.EventPageIndex.HasValue)
        {
            _editSession.SetActivePage(location.Document.EventPageIndex.Value);
        }
        if (int.TryParse(location.SelectionPath, out int rawIndex))
        {
            _selectedRawIndex = rawIndex;
            _commands.SelectRawIndex(rawIndex);
        }
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        DocumentRequest request = Request.Document
            ?? throw new InvalidOperationException("The Event Editor requires a document request.");
        MvProjectSession project = _workspace.CurrentSession
            ?? throw new InvalidOperationException("No project is open.");
        EventListAddress address = EventWindowIdentity.CreateAddress(request);
        IProjectDocumentLease lease = await project.Workspace
            .AcquireDocumentAsync(address.NormalizedRelativePath, cancellationToken)
            .ConfigureAwait(true);
        if (lease.Document is not MutableJsonProjectDocument document)
        {
            await lease.DisposeAsync().ConfigureAwait(true);
            throw new NotSupportedException("The dedicated Event Editor requires a mutable JSON document.");
        }

        _lease = lease;
        _project = project;
        _document = document;
        _recordLookup = new ProjectRecordLookup(project);
        await _recordLookup.RefreshAsync(cancellationToken).ConfigureAwait(true);
        _commandCatalog = new EventCommandProjectCatalog(project, address);
        await _commandCatalog.RefreshAsync(cancellationToken).ConfigureAwait(true);
        _commandUsage = new EventCommandUsageService();
        _commandWorkflow = new EventCommandWorkflowCoordinator(_registry, project, _commandCatalog, _commandUsage);
        _formatter = new EventCommandFormatter(_registry, _recordLookup.Resolve);
        _analyzer = new EventListAnalyzer(_registry);
        _editSession = new EventEditSession(project, document, address);
        SubscribeSession(_editSession);
        ConfigureSurface(_editSession.Surface);
        RefreshAll(selectRawIndex: null);
        NotifyStateChanged();
    }

    protected override async ValueTask OnDisposeWindowAsync()
    {
        if (_editSession is not null)
        {
            UnsubscribeSession(_editSession);
            await _editSession.DisposeAsync().ConfigureAwait(true);
            _editSession = null;
        }
        if (_lease is not null)
        {
            await _lease.DisposeAsync().ConfigureAwait(true);
            _lease = null;
        }
        _operationGate.Dispose();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!IsServiceCloseApproved && !_closeApproved && e.CloseReason == CloseReason.UserClosing && IsDirty)
        {
            e.Cancel = true;
            if (!_closePromptRunning) _ = CloseFromChromeAsync();
            return;
        }
        base.OnFormClosing(e);
    }

    public override EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context)
    {
        EventEditSession? session = _editSession;
        if (command == EditorCommandIds.EditUndo)
            return session?.CanUndo == true ? EditorCommandStatus.Enabled("Undo Event Edit") : EditorCommandStatus.Disabled("Undo Event Edit");
        if (command == EditorCommandIds.EditRedo)
            return session?.CanRedo == true ? EditorCommandStatus.Enabled("Redo Event Edit") : EditorCommandStatus.Disabled("Redo Event Edit");
        if (command == EditorCommandIds.EditFind) return EditorCommandStatus.Enabled("Find Event Command");
        if (command == EditorCommandIds.SessionApply)
            return session?.IsDirty == true && session.HasConflict == false
                ? EditorCommandStatus.Enabled("Apply Event Changes")
                : EditorCommandStatus.Disabled("Apply Event Changes", session?.HasConflict == true ? "The event changed externally." : "There are no unapplied event changes.");
        if (command == EditorCommandIds.SessionApplyAndClose)
            return session?.HasConflict == false ? EditorCommandStatus.Enabled("Apply and Close Event") : EditorCommandStatus.Disabled("Apply and Close Event", "The event changed externally.");
        if (command == EditorCommandIds.SessionRevert)
            return session?.IsDirty == true ? EditorCommandStatus.Enabled("Revert Event Changes") : EditorCommandStatus.Disabled("Revert Event Changes");
        return base.QueryCommand(command, context);
    }

    public override async ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.EditUndo) UndoLocal();
        else if (command == EditorCommandIds.EditRedo) RedoLocal();
        else if (command == EditorCommandIds.EditFind) _search.Focus();
        else if (command == EditorCommandIds.SessionApply) await ApplyAsync(context.CancellationToken).ConfigureAwait(true);
        else if (command == EditorCommandIds.SessionApplyAndClose) await ApplyAndCloseAsync(context.CancellationToken).ConfigureAwait(true);
        else if (command == EditorCommandIds.SessionRevert)
        {
            _editSession?.RevertToBaseline();
            RefreshAll(_selectedRawIndex);
        }
        else await base.ExecuteCommandAsync(command, context).ConfigureAwait(true);
        NotifyCommandStateChanged();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter))
        {
            _ = RunOperationAsync(async token => await ApplyAsync(token).ConfigureAwait(true));
            return true;
        }
        if (keyData == (Keys.Control | Keys.Z))
        {
            UndoLocal();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Y))
        {
            RedoLocal();
            return true;
        }
        if (keyData == (Keys.Control | Keys.F))
        {
            _search.Focus();
            _search.SelectAll();
            return true;
        }
        if (keyData == Keys.F6)
        {
            if (_commands.ContainsFocus) FocusFirstSettingsControl();
            else _commands.Focus();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildMenu()
    {
        ToolStripMenuItem file = new("&File");
        file.DropDownItems.Add(Menu("&Apply", (_, _) => _ = RunOperationAsync(ApplyAsync), Keys.Control | Keys.Enter));
        file.DropDownItems.Add(Menu("Apply and &Close", (_, _) => _ = RunOperationAsync(ApplyAndCloseAsync), Keys.Control | Keys.Shift | Keys.Enter));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Menu("&Cancel", (_, _) => CancelAndClose(), Keys.None, "Esc"));

        ToolStripMenuItem edit = new("&Edit");
        edit.DropDownItems.Add(Menu("&Undo", (_, _) => UndoLocal(), Keys.Control | Keys.Z));
        edit.DropDownItems.Add(Menu("&Redo", (_, _) => RedoLocal(), Keys.Control | Keys.Y));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Menu("Insert &Before Selected…", (_, _) => InsertCommand(EventCommandInsertionMode.BeforeSelected), Keys.Insert));
        edit.DropDownItems.Add(Menu("Insert &Inside Selected Branch…", (_, _) => InsertCommand(EventCommandInsertionMode.InsideSelectedBranch), Keys.Control | Keys.Shift | Keys.Insert));
        edit.DropDownItems.Add(Menu("Insert &After Selected Block…", (_, _) => InsertCommand(EventCommandInsertionMode.AfterSelectedBlock), Keys.Control | Keys.Insert));
        edit.DropDownItems.Add(Menu("Insert at &End…", (_, _) => InsertCommand(EventCommandInsertionMode.AtEnd), Keys.None));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Menu("Cu&t", (_, _) => CutSelected(), Keys.Control | Keys.X));
        edit.DropDownItems.Add(Menu("&Copy", (_, _) => CopySelected(), Keys.Control | Keys.C));
        edit.DropDownItems.Add(Menu("&Paste", (_, _) => PasteCommands(), Keys.Control | Keys.V));
        edit.DropDownItems.Add(Menu("&Delete", (_, _) => DeleteSelected(), Keys.Delete));

        ToolStripMenuItem page = new("&Page");
        page.DropDownItems.Add(Menu("&New Page", (_, _) => AddPage(false), Keys.Control | Keys.N));
        page.DropDownItems.Add(Menu("&Duplicate Page", (_, _) => AddPage(true), Keys.Control | Keys.Shift | Keys.D));
        page.DropDownItems.Add(Menu("&Copy Page", (_, _) => CopyPage(), Keys.Control | Keys.Shift | Keys.C));
        page.DropDownItems.Add(Menu("&Paste Page", (_, _) => PastePage(), Keys.Control | Keys.Shift | Keys.V));
        page.DropDownItems.Add(Menu("&Delete Page", (_, _) => DeletePage(), Keys.Control | Keys.Shift | Keys.Delete));
        page.DropDownItems.Add(Menu("C&lear Page", (_, _) => ClearPage(), Keys.None));
        page.DropDownItems.Add(new ToolStripSeparator());
        page.DropDownItems.Add(Menu("Move Page &Left", (_, _) => MovePage(-1), Keys.Control | Keys.Alt | Keys.Left));
        page.DropDownItems.Add(Menu("Move Page &Right", (_, _) => MovePage(1), Keys.Control | Keys.Alt | Keys.Right));

        ToolStripMenuItem pluginTags = new("&Plugin Tags");
        pluginTags.DropDownItems.Add(Menu("Edit &Event Note Tags…", (_, _) => _ = RunOperationAsync(EditEventPluginTagsAsync), Keys.None));
        pluginTags.DropDownItems.Add(Menu("Edit &Page Comment Tags…", (_, _) => _ = RunOperationAsync(EditPageCommentTagsAsync), Keys.None));
        pluginTags.DropDownItems.Add(new ToolStripSeparator());
        pluginTags.DropDownItems.Add(Menu("Insert &YEP Plugin Command…", (_, _) => _ = RunOperationAsync(InsertYanflyPluginCommandAsync), Keys.None));

        ToolStripMenuItem textStudio = new("&Text Studio");
        textStudio.DropDownItems.Add(Menu("Generate for &Selected Text Command…", (_, _) => _ = RunOperationAsync(OpenTextStudioForSelectedAsync), Keys.None));
        textStudio.DropDownItems.Add(Menu("Generate &Character Dialogue…", (_, _) => _ = RunOperationAsync(token => OpenCharacterVoiceStudioAsync(false, token)), Keys.None));
        textStudio.DropDownItems.Add(Menu("Generate Character &Responses…", (_, _) => _ = RunOperationAsync(token => OpenCharacterVoiceStudioAsync(true, token)), Keys.None));
        textStudio.DropDownItems.Add(Menu("Generate &Storylet / Narrative Beat…", (_, _) => _ = RunOperationAsync(OpenStoryletStudioAsync), Keys.None));
        textStudio.DropDownItems.Add(Menu("Generate &Quest / Narrative Plan…", (_, _) => _ = RunOperationAsync(OpenQuestStudioAsync), Keys.None));
        textStudio.DropDownItems.Add(new ToolStripSeparator());
        textStudio.DropDownItems.Add(Menu("Insert &Show Text…", (_, _) => _ = RunOperationAsync(token => OpenTextStudioAsync(MvGeneratedEventTextKind.ShowText, false, token)), Keys.None));
        textStudio.DropDownItems.Add(Menu("Insert Show &Choices…", (_, _) => _ = RunOperationAsync(token => OpenTextStudioAsync(MvGeneratedEventTextKind.ShowChoices, false, token)), Keys.None));
        textStudio.DropDownItems.Add(Menu("Insert &Branching Dialogue…", (_, _) => _ = RunOperationAsync(token => OpenTextStudioAsync(MvGeneratedEventTextKind.BranchingDialogue, false, token)), Keys.None));
        textStudio.DropDownItems.Add(Menu("Insert Scrolling &Text…", (_, _) => _ = RunOperationAsync(token => OpenTextStudioAsync(MvGeneratedEventTextKind.ScrollingText, false, token)), Keys.None));
        textStudio.DropDownItems.Add(Menu("Insert &Comment…", (_, _) => _ = RunOperationAsync(token => OpenTextStudioAsync(MvGeneratedEventTextKind.Comment, false, token)), Keys.None));

        ToolStripMenuItem view = new("&View");
        view.DropDownItems.Add(Menu("&Advanced Event Details", (_, _) => ToggleAdvanced(), Keys.F9));

        _menu.Items.AddRange([file, edit, page, pluginTags, textStudio, view]);
    }

    private void BuildPageButtons()
    {
        _pageButtons.Padding = new Padding(3, 0, 0, 0);
        AddPageButton("&New Page", "Create a new event page", (_, _) => AddPage(false));
        AddPageButton("&Copy Page", "Copy the active event page", (_, _) => CopyPage());
        AddPageButton("&Paste Page", "Paste an event page from the clipboard", (_, _) => PastePage());
        AddPageButton("&Delete Page", "Delete the active event page", (_, _) => DeletePage());
        AddPageButton("C&lear Page", "Remove every command from the active event page", (_, _) => ClearPage());
    }

    private void BuildCommandToolbar()
    {
        AddCommandInsertionButton();
        AddTextStudioButton();
        AddCommandButton("Event Tags", (_, _) => _ = RunOperationAsync(EditEventPluginTagsAsync));
        AddCommandButton("Page Tags", (_, _) => _ = RunOperationAsync(EditPageCommentTagsAsync));
        AddCommandButton("YEP Command", (_, _) => _ = RunOperationAsync(InsertYanflyPluginCommandAsync));
        _commandToolbar.Items.Add(new ToolStripSeparator());
        AddCommandButton("Edit", (_, _) => EditSelected());
        AddCommandButton("Delete", (_, _) => DeleteSelected());
        AddCommandButton("Duplicate", (_, _) => DuplicateSelected());
        _commandToolbar.Items.Add(new ToolStripSeparator());
        AddCommandButton("Up", (_, _) => MoveSelected(-1));
        AddCommandButton("Down", (_, _) => MoveSelected(1));
        _commandToolbar.Items.Add(new ToolStripSeparator());
        AddCommandButton("Cut", (_, _) => CutSelected());
        AddCommandButton("Copy", (_, _) => CopySelected());
        AddCommandButton("Paste", (_, _) => PasteCommands());
        _commandToolbar.Items.Add(new ToolStripSeparator());
        _undoButton.Click += (_, _) => UndoLocal();
        _redoButton.Click += (_, _) => RedoLocal();
        _commandToolbar.Items.Add(_undoButton);
        _commandToolbar.Items.Add(_redoButton);
        _commandToolbar.Items.Add(new ToolStripSeparator());
        _commandToolbar.Items.Add(new ToolStripControlHost(_search) { AutoSize = false, Width = 230 });
        AddCommandButton("Clear Search", (_, _) => _search.Clear());
    }

    private void BuildLayout()
    {
        Panel root = new() { Dock = DockStyle.Fill };

        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(10, 7, 10, 7)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        header.Controls.Add(AccessibilityMetadata.CreateLabel("&Name:", _name), 0, 0);
        header.Controls.Add(_name, 1, 0);
        Label noteLabel = AccessibilityMetadata.CreateLabel("N&ote:", _note);
        noteLabel.Name = "NoteLabel";
        noteLabel.Margin = new Padding(12, 7, 8, 3);
        header.Controls.Add(noteLabel, 2, 0);
        header.Controls.Add(_note, 3, 0);
        header.Controls.Add(_pageTabs, 0, 1);
        header.SetColumnSpan(_pageTabs, 2);
        header.Controls.Add(_pageButtons, 2, 1);
        header.SetColumnSpan(_pageButtons, 2);

        BuildConflictBanner();

        SplitContainer main = ControlHelpers.CreateSplitContainer(
            Orientation.Vertical, 390, 350, 500, splitterWidth: 6);
        main.Panel1.Controls.Add(_settingsHost);

        Panel commandPanel = new() { Dock = DockStyle.Fill };
        commandPanel.Controls.Add(_commands);
        commandPanel.Controls.Add(_commandToolbar);
        GroupBox contents = new() { Text = "Contents", Dock = DockStyle.Fill, Padding = new Padding(6) };
        contents.Controls.Add(commandPanel);
        _rightSplit.Panel1.Controls.Add(contents);
        ControlHelpers.SetSplitterDistanceSafe(_rightSplit, 430);
        _rightSplit.Panel2.Controls.Add(BuildAdvancedPanel());
        main.Panel2.Controls.Add(_rightSplit);

        TableLayoutPanel bottom = new() { Dock = DockStyle.Bottom, Height = 52, ColumnCount = 2, Padding = new Padding(9, 7, 9, 7) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        FlowLayoutPanel buttons = new() { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttons.Controls.AddRange([_apply, _cancel, _ok]);
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(buttons, 1, 0);

        root.Controls.Add(main);
        root.Controls.Add(_conflictBanner);
        root.Controls.Add(header);
        root.Controls.Add(_menu);
        root.Controls.Add(bottom);
        InstallContent(root);

        AcceptButton = _ok;
        CancelButton = _cancel;
    }

    private void BuildConflictBanner()
    {
        _conflictBanner.BackColor = SystemColors.Info;
        FlowLayoutPanel contents = new() { Dock = DockStyle.Fill, WrapContents = false };
        Button reload = new() { Text = "&Reload External", AutoSize = true };
        Button keep = new() { Text = "&Keep My Changes", AutoSize = true };
        Button compare = new() { Text = "&Compare", AutoSize = true };
        reload.Click += (_, _) => ReloadExternal();
        keep.Click += (_, _) => KeepLocalChanges();
        compare.Click += (_, _) => CompareConflict();
        contents.Controls.AddRange([_conflictText, reload, keep, compare]);
        _conflictBanner.Controls.Add(contents);
    }

    private Control BuildAdvancedPanel()
    {
        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("Event JSON") { Controls = { _rawOwner } });
        tabs.TabPages.Add(new TabPage("Selected Command JSON") { Controls = { _rawCommand } });
        tabs.TabPages.Add(new TabPage("Diagnostics") { Controls = { _diagnostics } });
        return tabs;
    }

    private void WireEvents()
    {
        _ok.Click += async (_, _) => await RunOperationAsync(ApplyAndCloseAsync).ConfigureAwait(true);
        _apply.Click += async (_, _) => await RunOperationAsync(ApplyAsync).ConfigureAwait(true);
        _cancel.Click += (_, _) => CancelAndClose();
        _name.TextChanged += (_, _) => HeaderChanged();
        _note.TextChanged += (_, _) => HeaderChanged();
        _pageTabs.SelectedIndexChanged += (_, _) => PageTabChanged();
        _search.TextChanged += (_, _) => RefreshCommandProjection(_selectedRawIndex);
        _commands.CommandActivated += (_, _) => EditSelected();
        _commands.EditRequested += (_, _) => EditSelected();
        _commands.InsertRequested += (_, e) => InsertCommand(e.Mode);
        _commands.DeleteRequested += (_, _) => DeleteSelected();
        _commands.DuplicateRequested += (_, _) => DuplicateSelected();
        _commands.CopyRequested += (_, _) => CopySelected();
        _commands.CutRequested += (_, _) => CutSelected();
        _commands.PasteRequested += (_, _) => PasteCommands();
        _commands.MoveRequested += (_, e) => MoveSelected(e.Direction);
        _commands.SelectionChanged += (_, _) => CommandSelectionChanged();
        _diagnostics.DoubleClick += (_, _) =>
        {
            if (_diagnostics.SelectedItem is DiagnosticItem item && item.RawIndex.HasValue)
            {
                _commands.SelectRawIndex(item.RawIndex.Value);
            }
        };
    }

    private void ConfigureSurface(EventSurfaceKind surface)
    {
        _settingsHost.Controls.Clear();
        Control? noteLabel = FindControl(this, "NoteLabel");
        bool pages = surface != EventSurfaceKind.CommonEvent;
        _pageTabs.Visible = pages;
        _pageButtons.Visible = pages;
        _note.Visible = surface == EventSurfaceKind.MapEvent;
        if (noteLabel is not null) noteLabel.Visible = surface == EventSurfaceKind.MapEvent;

        switch (surface)
        {
            case EventSurfaceKind.MapEvent:
                _mapSettings = new EventPageSettingsControl(_registry, _project) { ShowApplyButton = false };
                _mapSettings.SnapshotChanged += OnMapSettingsChanged;
                _settingsHost.Controls.Add(_mapSettings);
                break;
            case EventSurfaceKind.CommonEvent:
                _commonSettings = new CommonEventSettingsPanel();
                _commonSettings.SettingsChanged += OnCommonSettingsChanged;
                _settingsHost.Controls.Add(_commonSettings);
                break;
            case EventSurfaceKind.TroopEvent:
                _troopSettings = new TroopEventPageSettingsControl { ShowApplyButton = false };
                _troopSettings.SnapshotChanged += OnTroopSettingsChanged;
                _settingsHost.Controls.Add(_troopSettings);
                break;
        }
    }

    private void SubscribeSession(EventEditSession session)
    {
        session.Changed += OnEditSessionChanged;
        session.DirtyStateChanged += OnDirtyStateChanged;
        session.ConflictChanged += OnConflictChanged;
        session.ActivePageChanged += OnActivePageChanged;
    }

    private void UnsubscribeSession(EventEditSession session)
    {
        session.Changed -= OnEditSessionChanged;
        session.DirtyStateChanged -= OnDirtyStateChanged;
        session.ConflictChanged -= OnConflictChanged;
        session.ActivePageChanged -= OnActivePageChanged;
    }

    private void OnEditSessionChanged(object? sender, EventArgs e)
    {
        if (_updatingSessionFromUi)
        {
            UpdateStateOnly();
            return;
        }
        RefreshAll(_selectedRawIndex);
    }

    private void OnDirtyStateChanged(object? sender, EventArgs e) => UpdateStateOnly();
    private void OnConflictChanged(object? sender, EventArgs e) => UpdateConflictState();
    private void OnActivePageChanged(object? sender, EventArgs e) => RefreshAll(_selectedRawIndex);

    private void RefreshAll(int? selectRawIndex)
    {
        EventEditSession? session = _editSession;
        if (session is null || _formatter is null || _analyzer is null) return;
        _loadingUi = true;
        try
        {
            _identity.Text = session.Surface switch
            {
                EventSurfaceKind.MapEvent => $"Map {(session.MapId ?? 0):D3}  |  Event {session.OwnerId:D3}  |  Position {session.X}, {session.Y}",
                EventSurfaceKind.CommonEvent => $"Common Event {session.OwnerId:D4}",
                EventSurfaceKind.TroopEvent => $"Troop {session.OwnerId:D4}  |  Battle Event",
                _ => string.Empty
            };
            _name.Text = session.Name;
            _note.Text = session.Note;
            RebuildPageTabs();
            LoadSettings();
            RefreshCommandProjection(selectRawIndex);
            UpdateAdvancedDetails();
            UpdateConflictState();
            UpdateStateOnly();
        }
        finally
        {
            _loadingUi = false;
        }
    }

    private void RebuildPageTabs()
    {
        EventEditSession? session = _editSession;
        if (session is null || session.Surface == EventSurfaceKind.CommonEvent) return;
        _pageTabs.SuspendLayout();
        try
        {
            _pageTabs.TabPages.Clear();
            for (int index = 0; index < session.PageCount; index++)
            {
                _pageTabs.TabPages.Add(new TabPage((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            if (_pageTabs.TabPages.Count > 0)
            {
                _pageTabs.SelectedIndex = Math.Clamp(session.ActivePageIndex, 0, _pageTabs.TabPages.Count - 1);
            }
        }
        finally
        {
            _pageTabs.ResumeLayout(performLayout: true);
        }
    }

    private void LoadSettings()
    {
        EventEditSession? session = _editSession;
        if (session is null) return;
        switch (session.Surface)
        {
            case EventSurfaceKind.MapEvent:
                _mapSettings?.LoadSnapshot(session.CaptureMapPageSettings());
                break;
            case EventSurfaceKind.CommonEvent:
                _commonSettings?.LoadSnapshot(session.CaptureCommonSettings());
                break;
            case EventSurfaceKind.TroopEvent:
                _troopSettings?.LoadSnapshot(session.CaptureTroopPageSettings());
                break;
        }
    }

    private void RefreshCommandProjection(int? selectRawIndex, bool selectInsertionRow = false)
    {
        EventEditSession? session = _editSession;
        if (session is null || _formatter is null || _analyzer is null) return;
        EventListSnapshot snapshot = session.CaptureCommandList();
        _structure = _projector.Project(snapshot);
        IReadOnlyList<ProjectedEventRow> rows = EventRowProjector.Build(_structure);
        if (!string.IsNullOrWhiteSpace(_search.Text))
        {
            string query = _search.Text.Trim();
            rows = rows.Where(row =>
            {
                EventCommandSummary summary = _formatter.Format(row.Command);
                return summary.PrimaryText.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (summary.SecondaryText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || row.Command.Code.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase);
            }).ToArray();
        }
        _commands.SetRows(rows, _registry, _formatter, selectRawIndex, selectInsertionRow);
        LoadDiagnostics(snapshot);
        UpdateAdvancedDetails();
    }

    private void LoadDiagnostics(EventListSnapshot snapshot)
    {
        if (_analyzer is null) return;
        _diagnostics.BeginUpdate();
        try
        {
            _diagnostics.Items.Clear();
            foreach (EventStructureDiagnostic diagnostic in _analyzer.Analyze(snapshot))
            {
                _diagnostics.Items.Add(new DiagnosticItem(diagnostic.Severity, diagnostic.Code, diagnostic.Message, diagnostic.RawIndex));
            }
            if (_diagnostics.Items.Count == 0)
            {
                _diagnostics.Items.Add(new DiagnosticItem(EventDiagnosticSeverity.Information, "OK", "No event diagnostics.", null));
            }
        }
        finally
        {
            _diagnostics.EndUpdate();
        }
    }

    private void HeaderChanged()
    {
        EventEditSession? session = _editSession;
        if (_loadingUi || session is null) return;
        if (session.Surface == EventSurfaceKind.MapEvent)
        {
            RunUiMutation(() => session.UpdateMapHeader(_name.Text, _note.Text));
        }
        else if (session.Surface == EventSurfaceKind.CommonEvent)
        {
            CommonEventSettingsSnapshot current = session.CaptureCommonSettings() with { Name = _name.Text };
            _commonSettings?.SetName(_name.Text);
            RunUiMutation(() => session.UpdateCommonSettings(current));
        }
    }

    private void PageTabChanged()
    {
        if (_loadingUi || _editSession is null || _pageTabs.SelectedIndex < 0) return;
        _selectedRawIndex = null;
        _editSession.SetActivePage(_pageTabs.SelectedIndex);
    }

    private void OnMapSettingsChanged(object? sender, EventPageSettingsSnapshot snapshot)
    {
        if (_loadingUi || _editSession is null) return;
        RunUiMutation(() => _editSession.UpdateMapPageSettings(snapshot));
    }

    private void OnTroopSettingsChanged(object? sender, TroopEventPageSettingsSnapshot snapshot)
    {
        if (_loadingUi || _editSession is null) return;
        RunUiMutation(() => _editSession.UpdateTroopPageSettings(snapshot));
    }

    private void OnCommonSettingsChanged(object? sender, CommonEventSettingsSnapshot snapshot)
    {
        if (_loadingUi || _editSession is null) return;
        RunUiMutation(() => _editSession.UpdateCommonSettings(snapshot));
        _loadingUi = true;
        try { _name.Text = snapshot.Name; }
        finally { _loadingUi = false; }
    }

    private void RunUiMutation(Action mutation)
    {
        _updatingSessionFromUi = true;
        try
        {
            mutation();
            UpdateAdvancedDetails();
            UpdateStateOnly();
        }
        finally
        {
            _updatingSessionFromUi = false;
        }
    }

    private async Task<PluginTagProjectIntelligence> GetPluginTagIntelligenceAsync(CancellationToken cancellationToken)
    {
        MvProjectSession project = _project ?? throw new InvalidOperationException("No project is open.");
        return await YanflyPluginTagProjectCache
            .GetAsync(project.Workspace.Snapshot.Identity.ProjectRoot, PluginTagCatalogHost.Catalog)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task EditEventPluginTagsAsync(CancellationToken cancellationToken)
    {
        EventEditSession? session = _editSession;
        MvProjectSession? project = _project;
        if (session is null || project is null) return;
        if (session.Surface != EventSurfaceKind.MapEvent)
        {
            MessageBox.Show(this, "Event-note Plugin Tags are available for map events. Use Page Comment Tags or YEP Plugin Commands for this event surface.",
                "Event Plugin Tags", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        FlushCurrentControls();
        PluginTagContext context = new(
            PluginAuthoringSurface.EventNote,
            PluginDocumentKind.Event,
            project.Workspace.Snapshot.Identity.ProjectRoot,
            session.RelativePath,
            EventId: session.OwnerId,
            EventPageIndex: session.ActivePageIndex);
        PluginTagProjectIntelligence intelligence = await GetPluginTagIntelligenceAsync(cancellationToken).ConfigureAwait(true);
        using PluginSurfaceEditorDialog dialog = new(project, PluginTagCatalogHost.Catalog, context,
            session.Note, intelligence, "Event Plugin Tags", "Raw Event Note");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        RunUiMutation(() => session.UpdateMapHeader(_name.Text, dialog.ResultText));
        RefreshAll(_selectedRawIndex);
        _status.Text = "Event plugin tags updated in the working event note.";
    }

    private async Task EditPageCommentTagsAsync(CancellationToken cancellationToken)
    {
        EventEditSession? session = _editSession;
        MvProjectSession? project = _project;
        if (session is null || project is null) return;
        FlushCurrentControls();
        EventListSnapshot snapshot = session.CaptureCommandList();
        PluginEventCommandLine[] lines = snapshot.Commands.Select(command => new PluginEventCommandLine(
            command.Code,
            command.Indent,
            command.Code is 108 or 408
                ? JsonParameterReader.GetString(JsonParameterReader.ParseArray(command.ParametersRawJson), 0)
                : string.Empty)).ToArray();
        EventPageCommentBlock current = EventPageCommentSynchronizer.ExtractLeading(lines);
        PluginTagContext context = new(
            PluginAuthoringSurface.EventPageComment,
            PluginDocumentKind.EventPage,
            project.Workspace.Snapshot.Identity.ProjectRoot,
            session.RelativePath,
            EventId: session.OwnerId,
            EventPageIndex: session.ActivePageIndex);
        PluginTagProjectIntelligence intelligence = await GetPluginTagIntelligenceAsync(cancellationToken).ConfigureAwait(true);
        using PluginSurfaceEditorDialog dialog = new(project, PluginTagCatalogHost.Catalog, context,
            current.Text, intelligence, "Event Page Comment Tags", "Raw Leading Comments");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        IReadOnlyList<PluginEventCommandLine> commentRows = EventPageCommentSynchronizer.BuildCommentRows(dialog.ResultText, current.Indent);
        IReadOnlyList<string> replacement = commentRows.Select(row =>
            RawEventCommand.CreateJson(row.Code, row.Indent, JsonSerializer.Serialize(new object?[] { row.Text }))).ToArray();
        session.ReplaceCommandSpan(new EventCommandSpan(0, current.Length), replacement, "Edit YEP event-page comment tags");
        _selectedRawIndex = replacement.Count > 0 ? 0 : null;
        RefreshCommandProjection(_selectedRawIndex);
        _status.Text = "Event-page comments updated as RPG Maker commands 108/408.";
    }

    private async Task InsertYanflyPluginCommandAsync(CancellationToken cancellationToken)
    {
        EventEditSession? session = _editSession;
        MvProjectSession? project = _project;
        if (session is null || project is null) return;
        PluginTagProjectIntelligence intelligence = await GetPluginTagIntelligenceAsync(cancellationToken).ConfigureAwait(true);
        using YanflyCommandPickerDialog dialog = new(project, PluginTagCatalogHost.Catalog,
            PluginAuthoringSurface.PluginCommand, intelligence.Availability);
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.ResultText)) return;
        EventCommandInsertionTarget target = ResolveInsertionTarget(EventCommandInsertionMode.BeforeSelected);
        string raw = RawEventCommand.CreateJson(356, target.Indent,
            JsonSerializer.Serialize(new object?[] { dialog.ResultText }));
        session.InsertCommands(target.RawIndex, target.Indent, [raw], "Insert YEP plugin command");
        _selectedRawIndex = target.RawIndex;
        RefreshCommandProjection(_selectedRawIndex);
        _status.Text = "YEP plugin command inserted as RPG Maker command 356.";
    }

    private void InsertCommand(EventCommandInsertionMode mode = EventCommandInsertionMode.BeforeSelected)
    {
        // Branch marker rows represent the start of a conditional arm.  Treat the
        // ordinary Insert action as "insert into this arm" so users do not have to
        // know the structural shortcut just to add the first command to Win/Escape/
        // Lose, Choice, or Else branches.
        if (mode == EventCommandInsertionMode.BeforeSelected
            && _commands.SelectedRow is { IsContinuation: true, BranchLabel: not null })
        {
            mode = EventCommandInsertionMode.InsideSelectedBranch;
        }
        _ = RunOperationAsync(token => InsertCommandAsync(mode, token));
    }

    private async Task InsertCommandAsync(EventCommandInsertionMode mode, CancellationToken cancellationToken)
    {
        EventEditSession? session = _editSession;
        EventCommandWorkflowCoordinator? workflow = _commandWorkflow;
        if (session is null || workflow is null) return;
        EventCommandInsertionTarget target = ResolveInsertionTarget(mode);
        EventCommandEditResult? result = await workflow
            .CreateAsync(this, target, session.CurrentAddress, cancellationToken)
            .ConfigureAwait(true);
        if (result is null) return;
        session.InsertCommands(target.RawIndex, target.Indent, result.RawCommands, "Insert event command");
        _selectedRawIndex = target.RawIndex + result.PreferredSelectionOffset;
        RefreshCommandProjection(_selectedRawIndex);
        _status.Text = "Event command inserted.";
    }

    private void EditSelected()
    {
        _ = RunOperationAsync(EditSelectedAsync);
    }

    private async Task EditSelectedAsync(CancellationToken cancellationToken)
    {
        EventEditSession? session = _editSession;
        EventCommandWorkflowCoordinator? workflow = _commandWorkflow;
        if (session is null || workflow is null || _structure is null) return;
        if (_commands.SelectedRow is not { } row)
        {
            await InsertCommandAsync(EventCommandInsertionMode.AtEnd, cancellationToken).ConfigureAwait(true);
            return;
        }
        EventNode? node = _structure.FindNodeContaining(row.RawIndex);
        if (node is null) return;
        if (CommandCode(node) == 0)
        {
            await InsertCommandAsync(EventCommandInsertionMode.AtEnd, cancellationToken).ConfigureAwait(true);
            return;
        }
        EventCommandEditResult? result = await workflow
            .EditAsync(this, node, session.CurrentAddress, cancellationToken)
            .ConfigureAwait(true);
        if (result is null) return;
        session.ReplaceCommandSpan(node.OriginalSpan, result.RawCommands, "Edit event command");
        _selectedRawIndex = node.OriginalSpan.StartIndex + result.PreferredSelectionOffset;
        RefreshCommandProjection(_selectedRawIndex);
        _status.Text = "Event command updated.";
    }

    private EventCommandInsertionTarget ResolveInsertionTarget(EventCommandInsertionMode mode)
    {
        EventEditSession? session = _editSession;
        EventListSnapshot snapshot = session?.CaptureCommandList()
            ?? throw new InvalidOperationException("No event command list is loaded.");
        int endIndex = Math.Max(0, snapshot.Count - 1);
        ProjectedEventRow? row = _commands.SelectedRow;
        if (mode == EventCommandInsertionMode.AtEnd || row is null || _commands.IsInsertionRowSelected)
            return new EventCommandInsertionTarget(endIndex, 0, EventCommandInsertionMode.AtEnd);

        EventNode? node = _structure?.FindNodeContaining(row.RawIndex);
        if (node is null)
            return new EventCommandInsertionTarget(Math.Min(row.RawIndex, endIndex), Math.Max(0, row.Command.Indent), mode);

        if (mode == EventCommandInsertionMode.AfterSelectedBlock)
            return new EventCommandInsertionTarget(Math.Min(node.OriginalSpan.EndExclusive, endIndex), Math.Max(0, node.OriginalIndent), mode);

        if (mode == EventCommandInsertionMode.InsideSelectedBranch
            && _structure is not null
            && EventCommandInsertionPlanner.TryResolveInsideBranch(_structure.Nodes, row.RawIndex, endIndex, out EventCommandInsertionTarget branchTarget))
        {
            return branchTarget;
        }

        return new EventCommandInsertionTarget(Math.Min(node.OriginalSpan.StartIndex, endIndex), Math.Max(0, node.OriginalIndent), EventCommandInsertionMode.BeforeSelected);
    }

    private async Task OpenTextStudioForSelectedAsync(CancellationToken cancellationToken)
    {
        EventNode? node = SelectedNode();
        if (node is null || !TextStudioEventIntegration.TryKindForNode(node, out MvGeneratedEventTextKind kind))
        {
            MessageBox.Show(this, "Select a Show Text, Show Choices, Scrolling Text, or Comment command first.\r\n\r\nYou can also use the Text Studio menu to insert a new generated command block.",
                "Generate Event Text", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await OpenTextStudioAsync(kind, replaceSelected: true, cancellationToken).ConfigureAwait(true);
    }

    private async Task OpenTextStudioAsync(MvGeneratedEventTextKind kind, bool replaceSelected, CancellationToken cancellationToken)
    {
        EventEditSession session = _editSession ?? throw new InvalidOperationException("No event is loaded.");
        TextStudioEventBinding binding;
        if (replaceSelected)
        {
            EventNode node = SelectedNode() ?? throw new InvalidOperationException("No event command is selected.");
            binding = TextStudioEventIntegration.CreateReplacement(session, node, kind);
        }
        else
        {
            EventCommandInsertionTarget target = ResolveInsertionTarget(EventCommandInsertionMode.BeforeSelected);
            binding = TextStudioEventIntegration.CreateInsertion(session, kind, target.RawIndex, target.Indent);
        }

        IEditorWindow window = await _windows.OpenAsync(new WindowRequest(
            EditorWindowKind.TextStudio, "text-studio", "Playroma Text Studio",
            Scope: WindowScope.Project, OwnerKey: Key, PlacementId: "PlayromaTextStudio"), cancellationToken).ConfigureAwait(true);
        if (window is not TextStudioForm studio) throw new InvalidOperationException("Text Studio window could not be opened.");
        studio.ActivateEventTarget(binding);
        _status.Text = replaceSelected ? $"Text Studio targeting selected {kind}." : $"Text Studio ready to insert {kind}.";
    }

    private async Task OpenCharacterVoiceStudioAsync(bool branchingResponses, CancellationToken cancellationToken)
    {
        EventEditSession session = _editSession ?? throw new InvalidOperationException("No event is loaded.");
        MvGeneratedEventTextKind kind = branchingResponses ? MvGeneratedEventTextKind.BranchingDialogue : MvGeneratedEventTextKind.ShowText;
        bool replace = false;
        TextStudioEventBinding binding;
        if (!branchingResponses && SelectedNode() is { } selected && TextStudioEventIntegration.TryKindForNode(selected, out MvGeneratedEventTextKind selectedKind) && selectedKind == MvGeneratedEventTextKind.ShowText)
        {
            binding = TextStudioEventIntegration.CreateReplacement(session, selected, MvGeneratedEventTextKind.ShowText);
            replace = true;
        }
        else
        {
            EventCommandInsertionTarget target = ResolveInsertionTarget(EventCommandInsertionMode.BeforeSelected);
            binding = TextStudioEventIntegration.CreateInsertion(session, kind, target.RawIndex, target.Indent);
        }

        string projectRoot = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectRoot
            ?? throw new InvalidOperationException("No project is open.");
        DialogueAct defaultAct = branchingResponses ? DialogueAct.Answer : DialogueAct.SmallTalk;
        using var voiceDialog = new DialogueSpeakerContextDialog(projectRoot, binding.Target.RecordId, session.Name, defaultAct);
        if (voiceDialog.ShowDialog(this) != DialogResult.OK || voiceDialog.Context is not { } voice) return;
        string recipeId = branchingResponses ? "phase10.dialogue.branching" : RecipeForAct(voice.Act ?? DialogueAct.SmallTalk);

        IEditorWindow window = await _windows.OpenAsync(new WindowRequest(
            EditorWindowKind.TextStudio, "text-studio", "Playroma Text Studio",
            Scope: WindowScope.Project, OwnerKey: Key, PlacementId: "PlayromaTextStudio"), cancellationToken).ConfigureAwait(true);
        if (window is not TextStudioForm studio) throw new InvalidOperationException("Text Studio window could not be opened.");
        studio.ActivateEventTarget(binding, voice, recipeId);
        _status.Text = $"Text Studio: {voice.SpeakerName ?? session.Name} / {voice.ProfileId} / {voice.Act}" + (replace ? " — replacing selected Show Text." : ".");
    }

    private async Task OpenStoryletStudioAsync(CancellationToken cancellationToken)
    {
        EventEditSession session = _editSession ?? throw new InvalidOperationException("No event is loaded.");
        EventCommandInsertionTarget target = ResolveInsertionTarget(EventCommandInsertionMode.BeforeSelected);
        TextStudioEventBinding binding = TextStudioEventIntegration.CreateInsertion(session, MvGeneratedEventTextKind.ShowText, target.RawIndex, target.Indent);
        IEditorWindow window = await _windows.OpenAsync(new WindowRequest(
            EditorWindowKind.TextStudio, "text-studio", "Playroma Text Studio",
            Scope: WindowScope.Project, OwnerKey: Key, PlacementId: "PlayromaTextStudio"), cancellationToken).ConfigureAwait(true);
        if (window is not TextStudioForm studio) throw new InvalidOperationException("Text Studio window could not be opened.");
        studio.ActivateEventTarget(binding, new DialogueVoiceContext(SpeakerName: session.Name));
        studio.OpenStoryletLab();
        _status.Text = "Storylet Lab ready — realized narrative beats will insert as Show Text unless you change the target.";
    }

    private async Task OpenQuestStudioAsync(CancellationToken cancellationToken)
    {
        EventEditSession session = _editSession ?? throw new InvalidOperationException("No event is loaded.");
        EventCommandInsertionTarget target = ResolveInsertionTarget(EventCommandInsertionMode.BeforeSelected);
        TextStudioEventBinding binding = TextStudioEventIntegration.CreateInsertion(session, MvGeneratedEventTextKind.ShowText, target.RawIndex, target.Indent);
        IEditorWindow window = await _windows.OpenAsync(new WindowRequest(
            EditorWindowKind.TextStudio, "text-studio", "Playroma Text Studio",
            Scope: WindowScope.Project, OwnerKey: Key, PlacementId: "PlayromaTextStudio"), cancellationToken).ConfigureAwait(true);
        if (window is not TextStudioForm studio) throw new InvalidOperationException("Text Studio window could not be opened.");
        studio.ActivateEventTarget(binding, new DialogueVoiceContext(SpeakerName: session.Name));
        studio.OpenQuestLab();
        _status.Text = "Quest Lab ready — realized quest summaries can be inserted as Show Text; MV Common Event suggestions are preview-only until reviewed.";
    }

    private static string RecipeForAct(DialogueAct act) => act switch
    {
        DialogueAct.Greeting => "phase10.dialogue.greeting",
        DialogueAct.Warning => "phase10.dialogue.warning",
        DialogueAct.Threat => "phase10.dialogue.threat",
        DialogueAct.QuestOffer => "phase10.dialogue.quest-offer",
        DialogueAct.QuestComplete => "phase10.dialogue.quest-complete",
        DialogueAct.Rumor => "phase10.dialogue.rumor",
        DialogueAct.ShopGreeting => "phase10.dialogue.shop-greeting",
        DialogueAct.BattleBark => "phase10.dialogue.battle-bark",
        DialogueAct.Victory => "phase10.dialogue.victory",
        DialogueAct.Farewell => "phase10.dialogue.farewell",
        _ => "phase10.dialogue.small-talk"
    };

    private void DeleteSelected()
    {
        EventEditSession? session = _editSession;
        EventNode? node = SelectedNode();
        if (session is null || node is null || CommandCode(node) == 0) return;
        if (MessageBox.Show(this,
                $"Delete the selected {node.OriginalSpan.Length}-row command block?",
                "Delete Event Command",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
        int select = Math.Max(0, node.OriginalSpan.StartIndex - 1);
        session.ReplaceCommandSpan(node.OriginalSpan, [], "Delete event command");
        _selectedRawIndex = select;
        RefreshCommandProjection(select);
        _status.Text = "Event command deleted.";
    }

    private void DuplicateSelected()
    {
        EventEditSession? session = _editSession;
        EventNode? node = SelectedNode();
        if (session is null || node is null || CommandCode(node) == 0) return;
        int insert = node.OriginalSpan.EndExclusive;
        session.ReplaceCommandSpan(new EventCommandSpan(insert, 0), node.RawCommands.Select(command => command.RawJson).ToArray(), "Duplicate event command");
        _selectedRawIndex = insert;
        RefreshCommandProjection(insert);
        _status.Text = "Event command duplicated.";
    }

    private void MoveSelected(int direction)
    {
        EventEditSession? session = _editSession;
        EventNode? node = SelectedNode();
        if (session is null || node is null || _structure is null || CommandCode(node) == 0) return;
        if (!TryFindSiblingList(_structure.Nodes, node, out EventNode[] siblings, out int index)) return;
        int target = index + Math.Sign(direction);
        if (target < 0 || target >= siblings.Length) return;
        EventNode other = siblings[target];
        if (CommandCode(other) == 0) return;
        int start = Math.Min(node.OriginalSpan.StartIndex, other.OriginalSpan.StartIndex);
        int end = Math.Max(node.OriginalSpan.EndExclusive, other.OriginalSpan.EndExclusive);
        IReadOnlyList<EventNode> order = direction < 0 ? [node, other] : [other, node];
        List<string> replacement = [];
        foreach (EventNode item in order) replacement.AddRange(item.RawCommands.Select(command => command.RawJson));
        session.ReplaceCommandSpan(EventCommandSpan.FromBounds(start, end), replacement, "Move event command");
        _selectedRawIndex = start;
        RefreshCommandProjection(start);
        _status.Text = "Event command moved.";
    }

    private EventNode? SelectedNode()
    {
        if (_structure is null || _commands.SelectedRow is not { } row) return null;
        return _structure.FindNodeContaining(row.RawIndex);
    }

    private void CopySelected()
    {
        EventNode? node = SelectedNode();
        if (node is null || _project is null || _formatter is null) return;
        EventClipboardPackage package = EventClipboardCodec.Create(
            node.RawCommands,
            _project.Workspace.Snapshot.Identity.ProjectRoot,
            _registry,
            _formatter);
        DataObject data = new();
        data.SetData(CommandClipboardFormat, EventClipboardCodec.Serialize(package));
        data.SetText(package.ReadableText);
        Clipboard.SetDataObject(data, true);
        _status.Text = $"Copied {node.RawCommands.Count} raw command row(s).";
    }

    private void CutSelected()
    {
        EventNode? node = SelectedNode();
        if (node is null || CommandCode(node) == 0) return;
        CopySelected();
        EventEditSession? session = _editSession;
        if (session is null) return;
        int select = Math.Max(0, node.OriginalSpan.StartIndex - 1);
        session.ReplaceCommandSpan(node.OriginalSpan, [], "Cut event command");
        _selectedRawIndex = select;
        RefreshCommandProjection(select);
        _status.Text = "Event command cut.";
    }

    private void PasteCommands()
    {
        EventEditSession? session = _editSession;
        if (session is null || _formatter is null) return;
        try
        {
            EventClipboardPackage package;
            IDataObject? data = Clipboard.GetDataObject();
            if (data?.GetDataPresent(CommandClipboardFormat) == true)
            {
                package = EventClipboardCodec.Deserialize(
                    Convert.ToString(data.GetData(CommandClipboardFormat), System.Globalization.CultureInfo.InvariantCulture)
                    ?? string.Empty);
            }
            else
            {
                IReadOnlyList<string> rows = EventClipboardCodec.ParseReadableText(Clipboard.GetText());
                package = EventClipboardCodec.Create(
                    rows.Select((raw, index) => RawEventCommand.Parse(index, raw)),
                    string.Empty,
                    _registry,
                    _formatter);
            }
            EventPastePlan plan = EventPastePlanner.Create(package);
            if (plan.RequiresReview && MessageBox.Show(this,
                    "This paste contains script, plugin-command, or heuristic references. They will be preserved exactly and will not be rewritten. Continue?",
                    "Review Event Dependencies",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;
            EventListSnapshot snapshot = session.CaptureCommandList();
            int rawIndex = _commands.SelectedRow?.StructuralSpan.StartIndex ?? Math.Max(0, snapshot.Count - 1);
            int indent = _commands.SelectedRow?.Command.Indent ?? 0;
            session.InsertCommands(rawIndex, indent, plan.Commands, "Paste event commands");
            _selectedRawIndex = rawIndex;
            RefreshCommandProjection(rawIndex);
            _status.Text = "Event commands pasted.";
        }
        catch (Exception exception)
        {
            ShowError("Paste failed", exception);
        }
    }

    private void AddPage(bool duplicate)
    {
        EventEditSession? session = _editSession;
        if (session is null || session.Surface == EventSurfaceKind.CommonEvent) return;
        session.AddPage(duplicate);
        _selectedRawIndex = null;
        RefreshAll(null);
        _status.Text = duplicate ? "Event page duplicated." : "Event page added.";
    }

    private void DeletePage()
    {
        EventEditSession? session = _editSession;
        if (session is null || session.Surface == EventSurfaceKind.CommonEvent || session.PageCount <= 1) return;
        if (MessageBox.Show(this,
                $"Delete page {session.ActivePageIndex + 1}?",
                "Delete Event Page",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
        session.DeleteCurrentPage();
        _selectedRawIndex = null;
        RefreshAll(null);
        _status.Text = "Event page deleted.";
    }

    private void ClearPage()
    {
        EventEditSession? session = _editSession;
        if (session is null) return;
        if (MessageBox.Show(this,
                session.Surface == EventSurfaceKind.CommonEvent
                    ? "Clear all commands in this common event?"
                    : $"Reset page {session.ActivePageIndex + 1} to its default settings and clear its commands?",
                "Clear Event Page",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
        session.ClearCurrentPage();
        _selectedRawIndex = null;
        RefreshAll(null);
        _status.Text = "Event page cleared.";
    }

    private void MovePage(int direction)
    {
        EventEditSession? session = _editSession;
        if (session is null || session.Surface == EventSurfaceKind.CommonEvent) return;
        int before = session.ActivePageIndex;
        session.MoveCurrentPage(direction);
        if (before == session.ActivePageIndex) return;
        _selectedRawIndex = null;
        RefreshAll(null);
        _status.Text = "Event page moved.";
    }

    private void CopyPage()
    {
        EventEditSession? session = _editSession;
        if (session is null || session.Surface == EventSurfaceKind.CommonEvent) return;
        DataObject data = new();
        data.SetData(PageClipboardFormat, session.CopyCurrentPageJson());
        data.SetText(session.CopyCurrentPageJson());
        Clipboard.SetDataObject(data, true);
        _status.Text = $"Copied page {session.ActivePageIndex + 1}.";
    }

    private void PastePage()
    {
        EventEditSession? session = _editSession;
        if (session is null || session.Surface == EventSurfaceKind.CommonEvent) return;
        try
        {
            IDataObject? data = Clipboard.GetDataObject();
            string raw = data?.GetDataPresent(PageClipboardFormat) == true
                ? Convert.ToString(data.GetData(PageClipboardFormat), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
                : Clipboard.GetText();
            session.PasteCurrentPageJson(raw);
            _selectedRawIndex = null;
            RefreshAll(null);
            _status.Text = "Event page pasted.";
        }
        catch (Exception exception)
        {
            ShowError("Paste Page failed", exception);
        }
    }

    private void UndoLocal()
    {
        EventEditSession? session = _editSession;
        if (session is null || !session.CanUndo) return;
        session.Undo();
        RefreshAll(_selectedRawIndex);
        _status.Text = "Undid local event edit.";
    }

    private void RedoLocal()
    {
        EventEditSession? session = _editSession;
        if (session is null || !session.CanRedo) return;
        session.Redo();
        RefreshAll(_selectedRawIndex);
        _status.Text = "Redid local event edit.";
    }

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        EventEditSession? session = _editSession;
        if (session is null || !session.IsDirty) return;
        if (session.HasConflict)
        {
            MessageBox.Show(this,
                "Resolve the external-change conflict before applying this event.",
                "Event Changed Externally",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        FlushCurrentControls();
        bool committed = await session.CommitAsync(cancellationToken).ConfigureAwait(true);
        if (_recordLookup is not null) await _recordLookup.RefreshAsync(cancellationToken).ConfigureAwait(true);
        if (_commandCatalog is not null) await _commandCatalog.RefreshAsync(cancellationToken).ConfigureAwait(true);
        RefreshAll(_selectedRawIndex);
        _status.Text = committed ? "Event changes applied to the project." : "No event changes to apply.";
        NotifyStateChanged();
    }

    private async Task ApplyAndCloseAsync(CancellationToken cancellationToken)
    {
        await ApplyAsync(cancellationToken).ConfigureAwait(true);
        if (_editSession?.HasConflict == true || IsDirty) return;
        _closeApproved = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CancelAndClose()
    {
        _editSession?.RevertToBaseline();
        _closeApproved = true;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private async Task CloseFromChromeAsync()
    {
        _closePromptRunning = true;
        try
        {
            if (!await ConfirmCloseAsync(CancellationToken.None).ConfigureAwait(true)) return;
            _closeApproved = true;
            DialogResult = DialogResult.Cancel;
            Close();
        }
        catch (Exception exception)
        {
            ShowError("Could not close Event Editor", exception);
        }
        finally
        {
            _closePromptRunning = false;
        }
    }

    private async ValueTask<bool> ConfirmCloseAsync(CancellationToken cancellationToken)
    {
        EventEditSession? session = _editSession;
        if (session is null || !session.IsDirty) return true;
        DialogResult result = MessageBox.Show(this,
            "Apply changes to this event before closing?",
            "Unsaved Event Changes",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);
        if (result == DialogResult.Cancel) return false;
        if (result == DialogResult.No)
        {
            session.RevertToBaseline();
            return true;
        }
        await ApplyAsync(cancellationToken).ConfigureAwait(true);
        return !session.IsDirty && !session.HasConflict;
    }

    private void FlushCurrentControls()
    {
        EventEditSession? session = _editSession;
        if (session is null) return;
        _updatingSessionFromUi = true;
        try
        {
            switch (session.Surface)
            {
                case EventSurfaceKind.MapEvent:
                    session.UpdateMapHeader(_name.Text, _note.Text);
                    if (_mapSettings?.CurrentSnapshot is { } map) session.UpdateMapPageSettings(map);
                    break;
                case EventSurfaceKind.CommonEvent:
                    if (_commonSettings?.CurrentSnapshot is { } common) session.UpdateCommonSettings(common);
                    break;
                case EventSurfaceKind.TroopEvent:
                    if (_troopSettings?.CurrentSnapshot is { } troop) session.UpdateTroopPageSettings(troop);
                    break;
            }
        }
        finally
        {
            _updatingSessionFromUi = false;
        }
    }

    private void ReloadExternal()
    {
        _editSession?.ReloadExternal();
        _selectedRawIndex = null;
        RefreshAll(null);
        _status.Text = "Reloaded the externally changed event.";
    }

    private void KeepLocalChanges()
    {
        _editSession?.KeepLocalChanges();
        RefreshAll(_selectedRawIndex);
        _status.Text = "Kept local event edits and merged unrelated external fields.";
    }

    private void CompareConflict()
    {
        if (_editSession?.HasConflict != true) return;
        using EventConflictCompareDialog dialog = new(_editSession.CreateConflictComparison());
        dialog.ShowDialog(this);
    }

    private void ToggleAdvanced()
    {
        _advancedVisible = !_advancedVisible;
        _rightSplit.Panel2Collapsed = !_advancedVisible;
        if (_advancedVisible)
        {
            ControlHelpers.SetSplitterDistanceSafe(_rightSplit, Math.Max(260, _rightSplit.Height - 230));
            UpdateAdvancedDetails();
        }
    }

    private void CommandSelectionChanged()
    {
        _selectedRawIndex = _commands.SelectedRow?.RawIndex;
        UpdateAdvancedDetails();
    }

    private void UpdateAdvancedDetails()
    {
        EventEditSession? session = _editSession;
        if (session is null) return;
        _rawOwner.Text = session.WorkingOwnerJson;
        _rawCommand.Text = _commands.SelectedRow is { } row ? session.CurrentCommandJson(row.RawIndex) : string.Empty;
    }

    private void UpdateConflictState()
    {
        bool conflict = _editSession?.HasConflict == true;
        _conflictBanner.Visible = conflict;
        _apply.Enabled = !conflict && IsDirty;
        NotifyStateChanged();
    }

    private void UpdateStateOnly()
    {
        EventEditSession? session = _editSession;
        bool dirty = session?.IsDirty == true;
        bool conflict = session?.HasConflict == true;
        _apply.Enabled = dirty && !conflict;
        _ok.Enabled = !conflict;
        _undoButton.Enabled = session?.CanUndo == true;
        _redoButton.Enabled = session?.CanRedo == true;
        _undoButton.ToolTipText = session?.UndoDescription is { } undo ? $"Undo: {undo}" : "Undo unavailable";
        _redoButton.ToolTipText = session?.RedoDescription is { } redo ? $"Redo: {redo}" : "Redo unavailable";
        Text = Title + (dirty ? " *" : string.Empty);
        NotifyStateChanged();
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (!await _operationGate.WaitAsync(0).ConfigureAwait(true)) return;
        try
        {
            await operation(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ShowError("Event operation failed", exception);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void ShowError(string title, Exception exception)
    {
        _status.Text = exception.Message;
        _notifications?.Error(title, exception.Message, exception);
        MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static bool TryFindSiblingList(
        IReadOnlyList<EventNode> nodes,
        EventNode target,
        out EventNode[] siblings,
        out int index)
    {
        siblings = nodes.OrderBy(item => item.OriginalSpan.StartIndex).ToArray();
        index = Array.IndexOf(siblings, target);
        if (index >= 0) return true;
        foreach (EventNode node in nodes)
        {
            if (node is not EventBlockNode block) continue;
            foreach (EventBranchNode branch in block.Branches)
            {
                if (TryFindSiblingList(branch.Children, target, out siblings, out index)) return true;
            }
        }
        siblings = [];
        index = -1;
        return false;
    }

    private static int CommandCode(EventNode node) => node switch
    {
        EventCommandNode value => value.Command.Code,
        EventContinuationGroupNode value => value.Opening.Code,
        EventBlockNode value => value.Opening.Code,
        EventOpaqueNode value => value.Command.Code,
        _ => -1
    };

    private static string EventLabel(EventEditSession session) => session.Surface switch
    {
        EventSurfaceKind.MapEvent => $"EV{session.OwnerId:D3}",
        EventSurfaceKind.CommonEvent => $"Common Event {session.OwnerId:D4}",
        EventSurfaceKind.TroopEvent => $"Troop {session.OwnerId:D4}",
        _ => "Event"
    };

    private void FocusFirstSettingsControl()
    {
        Control? first = AccessibilityMetadata.Enumerate(_settingsHost)
            .FirstOrDefault(control => control.TabStop && control.CanFocus && control.Visible && control.Enabled);
        first?.Focus();
    }

    private static ToolStripMenuItem Menu(
        string text,
        EventHandler handler,
        Keys shortcut,
        string? shortcutDisplay = null)
    {
        ToolStripMenuItem item = new(text)
        {
            ShowShortcutKeys = shortcut != Keys.None || !string.IsNullOrWhiteSpace(shortcutDisplay)
        };
        if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
        if (!string.IsNullOrWhiteSpace(shortcutDisplay)) item.ShortcutKeyDisplayString = shortcutDisplay;
        item.Click += handler;
        return item;
    }

    private void AddPageButton(string text, string description, EventHandler handler)
    {
        Button button = new()
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(88, 30),
            Margin = new Padding(3, 2, 0, 0),
            AccessibleName = AccessibilityMetadata.StripMnemonic(text),
            AccessibleDescription = description
        };
        button.Click += handler;
        _pageButtons.Controls.Add(button);
    }

    private void AddCommandInsertionButton()
    {
        ToolStripSplitButton insert = new("Insert")
        {
            ToolTipText = "Insert before the selection. On a branch marker, inserts inside that branch."
        };
        insert.ButtonClick += (_, _) => InsertCommand();
        insert.DropDownItems.Add(Menu("Insert &Before Selected…", (_, _) => InsertCommand(EventCommandInsertionMode.BeforeSelected), Keys.Insert));
        insert.DropDownItems.Add(Menu("Insert &Inside Selected Branch…", (_, _) => InsertCommand(EventCommandInsertionMode.InsideSelectedBranch), Keys.Control | Keys.Shift | Keys.Insert));
        insert.DropDownItems.Add(Menu("Insert &After Selected Block…", (_, _) => InsertCommand(EventCommandInsertionMode.AfterSelectedBlock), Keys.Control | Keys.Insert));
        insert.DropDownItems.Add(Menu("Insert at &End…", (_, _) => InsertCommand(EventCommandInsertionMode.AtEnd), Keys.None));
        _commandToolbar.Items.Add(insert);
    }

    private void AddTextStudioButton()
    {
        ToolStripSplitButton text = new("✨ Text") { ToolTipText = "Generate RPG Maker MV event text with Playroma Text Studio" };
        text.ButtonClick += (_, _) => _ = RunOperationAsync(OpenTextStudioForSelectedAsync);
        text.DropDownItems.Add(Menu("Generate for selected text command…", (_, _) => _ = RunOperationAsync(OpenTextStudioForSelectedAsync), Keys.None));
        text.DropDownItems.Add(Menu("Generate Character Dialogue…", (_, _) => _ = RunOperationAsync(token => OpenCharacterVoiceStudioAsync(false, token)), Keys.None));
        text.DropDownItems.Add(Menu("Generate Character Responses…", (_, _) => _ = RunOperationAsync(token => OpenCharacterVoiceStudioAsync(true, token)), Keys.None));
        text.DropDownItems.Add(Menu("Generate Storylet / Narrative Beat…", (_, _) => _ = RunOperationAsync(OpenStoryletStudioAsync), Keys.None));
        text.DropDownItems.Add(Menu("Generate Quest / Narrative Plan…", (_, _) => _ = RunOperationAsync(OpenQuestStudioAsync), Keys.None));
        text.DropDownItems.Add(new ToolStripSeparator());
        text.DropDownItems.Add(Menu("Insert Show Text…", (_, _) => _ = RunOperationAsync(token => OpenTextStudioAsync(MvGeneratedEventTextKind.ShowText, false, token)), Keys.None));
        text.DropDownItems.Add(Menu("Insert Show Choices…", (_, _) => _ = RunOperationAsync(token => OpenTextStudioAsync(MvGeneratedEventTextKind.ShowChoices, false, token)), Keys.None));
        text.DropDownItems.Add(Menu("Insert Branching Dialogue…", (_, _) => _ = RunOperationAsync(token => OpenTextStudioAsync(MvGeneratedEventTextKind.BranchingDialogue, false, token)), Keys.None));
        text.DropDownItems.Add(Menu("Insert Scrolling Text…", (_, _) => _ = RunOperationAsync(token => OpenTextStudioAsync(MvGeneratedEventTextKind.ScrollingText, false, token)), Keys.None));
        text.DropDownItems.Add(Menu("Insert Comment…", (_, _) => _ = RunOperationAsync(token => OpenTextStudioAsync(MvGeneratedEventTextKind.Comment, false, token)), Keys.None));
        _commandToolbar.Items.Add(text);
    }

    private void AddCommandButton(string text, EventHandler handler)
    {
        ToolStripButton button = new(text);
        button.Click += handler;
        _commandToolbar.Items.Add(button);
    }

    private static TextBox RawBox() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        AcceptsReturn = true,
        AcceptsTab = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9)
    };

    private static Control? FindControl(Control root, string name)
    {
        if (string.Equals(root.Name, name, StringComparison.Ordinal)) return root;
        foreach (Control child in root.Controls)
        {
            Control? found = FindControl(child, name);
            if (found is not null) return found;
        }
        return null;
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        for (int index = 0; index < 8 && current is not null; index++, current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "specifications"))) return current.FullName;
        }
        return null;
    }

    private sealed record DiagnosticItem(
        EventDiagnosticSeverity Severity,
        string Code,
        string Message,
        int? RawIndex)
    {
        public override string ToString() => $"{Severity}: {Code} — {Message}";
    }

    private sealed class CommonEventSettingsPanel : UserControl
    {
        private readonly ComboBox _trigger = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        private readonly NumericUpDown _switchId = new() { Minimum = 1, Maximum = 999999, Width = 100 };
        private readonly Label _name = new() { AutoSize = true, AutoEllipsis = true, MaximumSize = new Size(260, 0) };
        private CommonEventSettingsSnapshot? _snapshot;
        private bool _loading;

        public CommonEventSettingsPanel()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(8);
            _trigger.Items.AddRange(["None", "Autorun", "Parallel"]);
            TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Add(table, "Common Event", _name);
            Add(table, "Trigger", _trigger);
            Add(table, "Condition Switch", _switchId);
            GroupBox group = new() { Text = "Common Event Settings", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
            group.Controls.Add(table);
            Controls.Add(group);
            _trigger.SelectedIndexChanged += (_, _) => RaiseChanged();
            _switchId.ValueChanged += (_, _) => RaiseChanged();
        }

        public CommonEventSettingsSnapshot? CurrentSnapshot => _snapshot is null ? null : BuildSnapshot();
        public event EventHandler<CommonEventSettingsSnapshot>? SettingsChanged;

        public void LoadSnapshot(CommonEventSettingsSnapshot snapshot)
        {
            _loading = true;
            try
            {
                _snapshot = snapshot;
                _name.Text = snapshot.Name;
                _trigger.SelectedIndex = Math.Clamp(snapshot.Trigger, 0, _trigger.Items.Count - 1);
                _switchId.Value = Math.Clamp(snapshot.SwitchId, (int)_switchId.Minimum, (int)_switchId.Maximum);
                _switchId.Enabled = _trigger.SelectedIndex > 0;
            }
            finally
            {
                _loading = false;
            }
        }

        public void SetName(string name)
        {
            _name.Text = name;
            if (_snapshot is not null) _snapshot = _snapshot with { Name = name };
        }

        private CommonEventSettingsSnapshot BuildSnapshot()
        {
            CommonEventSettingsSnapshot source = _snapshot
                ?? throw new InvalidOperationException("No common event is loaded.");
            return source with
            {
                Name = _name.Text,
                Trigger = Math.Max(0, _trigger.SelectedIndex),
                SwitchId = (int)_switchId.Value
            };
        }

        private void RaiseChanged()
        {
            _switchId.Enabled = _trigger.SelectedIndex > 0;
            if (_loading || _snapshot is null) return;
            SettingsChanged?.Invoke(this, BuildSnapshot());
        }

        private static void Add(TableLayoutPanel table, string label, Control value)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, row);
            table.Controls.Add(value, 1, row);
        }
    }

    private sealed record EventEditorRecoveryPayload(
        string WorkingOwnerJson,
        int ActivePageIndex,
        int? SelectedRawIndex,
        int Version = 1);

    /// <summary>
    /// Minimal compatibility window service used only by the legacy three-argument constructor.
    /// It keeps old smoke tests/source trees buildable without weakening the primary constructor's
    /// requirement for a real window service. Window-launching actions are unavailable in this mode.
    /// </summary>
    private sealed class LegacyStandaloneWindowService : IWinFormsWindowService
    {
        public static LegacyStandaloneWindowService Instance { get; } = new();

        private LegacyStandaloneWindowService()
        {
        }

        public WinFormsWindowFactoryRegistry Factories { get; } = new();
        public IReadOnlyList<IEditorWindow> OpenWindows => Array.Empty<IEditorWindow>();
        public IEditorWindow? ActiveWindow => null;

        public event EventHandler? WindowsChanged { add { } remove { } }
        public event EventHandler<EditorWindowEventArgs>? WindowOpened { add { } remove { } }
        public event EventHandler<EditorWindowEventArgs>? WindowClosed { add { } remove { } }
        public event EventHandler<ActiveEditorWindowChangedEventArgs>? ActiveWindowChanged { add { } remove { } }

        public bool TryGet(WindowKey key, out IEditorWindow? window)
        {
            window = null;
            return false;
        }

        public ValueTask<IEditorWindow> OpenAsync(
            WindowRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<IEditorWindow>(Task.FromException<IEditorWindow>(CreateUnavailableException()));
        }

        public ValueTask<DialogResult> ShowDialogAsync(
            WindowRequest request,
            IWin32Window? owner = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(DialogResult.Cancel);
        }

        public ValueTask<DialogResult> ShowDialogPreparedAsync(
            WindowRequest request,
            Func<IWinFormsEditorWindow, CancellationToken, ValueTask> prepare,
            IWin32Window? owner = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(DialogResult.Cancel);
        }

        public ValueTask<bool> CloseAsync(IEditorWindow window, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> CloseAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> CloseScopeAsync(WindowScope scope, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> CanCloseAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> CanCloseScopeAsync(WindowScope scope, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public ValueTask CloseAllForcedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseScopeForcedAsync(WindowScope scope, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public void Activate(IEditorWindow window)
        {
        }

        private static InvalidOperationException CreateUnavailableException() => new(
            "This EventEditorForm was created through the legacy compatibility constructor. " +
            "Pass an IWinFormsWindowService to enable child editor windows.");
    }

}
