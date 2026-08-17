using System.Globalization;
using System.Text;
using Playroma.JRPG.Application;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Accessibility;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.Editor.WinForms.PluginTags;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Validation;
using Playroma.Text.RpgMakerMV;
using Playroma.JRPG.Editor.WinForms.Dialogs;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class DatabaseEditorHostControl : UserControl
{
    private readonly MvProjectSession _session;
    private readonly MutableJsonProjectDocument _document;
    private readonly MvDocumentKind _kind;
    private readonly ISelectionService _selection;
    private readonly IDatabaseEditorDefinition _definition;
    private readonly DatabaseRecordIndex _index;
    private readonly DatabaseMutationService _mutations;
    private readonly IDatabaseEditorBody? _customBody;
    private readonly Func<DocumentRequest, Task>? _documentOpener;
    private CancellationTokenSource? _bodyBinding;
    private readonly DatabaseHistoryIndex _history = new();
    private readonly RecordListControl _recordList = new();
    private readonly SplitContainer _split;
    private readonly List<TabPage> _advancedTabs = [];
    private readonly FlowLayoutPanel _propertyFlow = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };
    private readonly TabControl _details = new() { Dock = DockStyle.Fill, AccessibleName = "Database record editor sections" };
    private readonly NoteEditor _noteEditor = new();
    private readonly FormulaEditor _formulaEditor = new();
    private PluginTagsEditorControl? _pluginTagsEditor;
    private TabPage? _notesTab;
    private PluginTextEditingSession? _pluginTextSession;
    private PluginTagProjectIntelligence? _pluginProjectIntelligence;
    private int _pluginIntelligenceRevision;
    private bool _syncingPluginText;
    private readonly TraitListEditor _traits = new();
    private readonly EffectListEditor _effects = new();
    private readonly DataGridView _validation = ReadOnlyGrid("Severity", "Code", "Message", "Path");
    private readonly DataGridView _references = ReadOnlyGrid("Direction", "Confidence", "Target/Source", "Location");
    private readonly ListBox _historyList = new() { Dock = DockStyle.Fill };
    private readonly CodeTextEditorControl _raw = new();
    private readonly Label _selectionSummary = new() { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(6, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "Database selection summary" };
    private readonly Dictionary<DatabaseFieldDescriptor, Control> _fieldControls = [];
    private IReadOnlyList<DatabaseRecordHandle> _selected = [];
    private bool _loading;
    private bool _suppressRecordSelection;

    public DatabaseEditorHostControl(
        MvProjectSession session,
        IMutableProjectDocument document,
        MvDocumentKind kind,
        ISelectionService selection,
        Func<DocumentRequest, Task>? documentOpener = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = document as MutableJsonProjectDocument
            ?? throw new NotSupportedException("The reusable database editor requires a mutable JSON document.");
        _kind = kind;
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _definition = DatabaseEditorRegistry.CreateBuiltIn().Get(kind);
        _index = new DatabaseRecordIndex(_document);
        _mutations = new DatabaseMutationService(_session.Editing);
        _documentOpener = documentOpener;
        _customBody = DatabaseEditorBodyFactory.Create(_session, _document, _kind, _definition, documentOpener);
        if (_customBody is IDatabasePendingEditSource pending) pending.PendingChangesChanged += OnPendingChangesChanged;
        Dock = DockStyle.Fill;
        AccessibilityMetadata.Name(this, $"{_definition.DisplayName} database editor", $"Browse and edit {_definition.DisplayName.ToLowerInvariant()} records.", AccessibleRole.Pane);

        ToolStrip toolbar = BuildToolbar();
        _split = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 330, 220, 420);
        Panel left = new() { Dock = DockStyle.Fill };
        left.Controls.Add(_recordList);
        left.Controls.Add(_selectionSummary);
        _split.Panel1.Controls.Add(left);
        BuildDetailsTabs();
        _split.Panel2.Controls.Add(_details);
        Controls.Add(_split);
        Controls.Add(toolbar);

        _recordList.SetSource(_index);
        _recordList.SelectionChanged += OnRecordSelectionRequested;
        _recordList.RecordActivated += (_, id) => RecordActivated?.Invoke(this, id);
        _noteEditor.CommitRequested += async (_, _) => await CommitNoteAsync().ConfigureAwait(true);
        _noteEditor.ContentChanged += OnNoteContentChanged;
        _formulaEditor.CommitRequested += async (_, _) => await CommitFormulaAsync().ConfigureAwait(true);
        if (_pluginTagsEditor is not null)
        {
            _pluginTagsEditor.ApplyRequested += OnPluginTagsApplyRequested;
            _pluginTagsEditor.OpenNotesRequested += OnPluginTagsOpenNotesRequested;
            _pluginTagsEditor.PendingChangesChanged += OnPluginTagsPendingChangesChanged;
            _ = LoadPluginTagProjectIntelligenceAsync();
        }
        _session.DocumentChanged += OnDocumentChanged;
        _session.ValidationCompleted += OnValidationCompleted;
        _session.Editing.Context.CommandApplied += OnCommandHistory;
        RefreshDiagnostics();
        if (_index.Query(new DatabaseRecordFilter(Populated: true)).FirstOrDefault() is { } first) _recordList.SelectRecord(first.Id);
    }

    public event EventHandler<int>? RawRecordRequested;
    public event EventHandler<int>? RecordActivated;
    public event EventHandler? SelectionChanged;
    public event EventHandler? PendingChangesChanged;
    public event EventHandler<DatabaseTextGenerationRequestedEventArgs>? TextGenerationRequested;

    public MvDocumentKind Kind => _kind;
    public int? SelectedRecordId => _recordList.SelectedRecordId;
    public string SearchText
    {
        get => _recordList.SearchText;
        set => _recordList.SearchText = value;
    }
    public int RecordPanelWidth
    {
        get => _split.SplitterDistance;
        set
        {
            ControlHelpers.SetSplitterDistanceSafe(_split, value);
        }
    }
    public bool AdvancedVisible { get; private set; }
    public bool HasPendingChanges =>
        (_customBody is IDatabasePendingEditSource source && source.HasPendingChanges) ||
        _pluginTextSession?.IsDirty == true ||
        (_pluginTextSession is null && _noteEditor.IsModified) ||
        _formulaEditor.IsModified;

    public void SelectRecord(int recordId) => _recordList.SelectRecord(recordId);
    public void JumpToId(int recordId) => _recordList.JumpToId(recordId);
    public void FocusSearch() => _recordList.FocusSearch();
    public void FocusEditor()
    {
        Control root = _customBody?.View ?? _details;
        FindFirstEditable(root)?.Focus();
    }

    public void SetAdvancedVisible(bool visible)
    {
        if (AdvancedVisible == visible) return;
        AdvancedVisible = visible;
        if (visible)
        {
            foreach (TabPage page in _advancedTabs)
            {
                if (!_details.TabPages.Contains(page)) _details.TabPages.Add(page);
            }
        }
        else
        {
            foreach (TabPage page in _advancedTabs)
            {
                if (_details.TabPages.Contains(page)) _details.TabPages.Remove(page);
            }
        }
    }

    public async ValueTask ApplyPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pluginTextSession?.IsDirty == true)
            await CommitPluginTextAsync(cancellationToken).ConfigureAwait(true);
        else if (_pluginTextSession is null && _noteEditor.IsModified)
            await CommitNoteAsync().ConfigureAwait(true);
        if (_formulaEditor.IsModified)
            await CommitFormulaAsync().ConfigureAwait(true);
        if (_customBody is IDatabasePendingEditSource source)
        {
            await source.ApplyPendingAsync(cancellationToken).ConfigureAwait(true);
            if (!source.HasPendingChanges) QueueCustomBodyBinding();
        }
    }

    public async ValueTask DiscardPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool reloadTextEditors = (_pluginTextSession is null && _noteEditor.IsModified) || _formulaEditor.IsModified;
        if (_pluginTextSession?.IsDirty == true)
        {
            _pluginTextSession.Revert();
            SynchronizeNoteEditorFromSession();
        }
        if (_customBody is IDatabasePendingEditSource source)
        {
            await source.DiscardPendingAsync(cancellationToken).ConfigureAwait(true);
            QueueCustomBodyBinding();
        }
        if (reloadTextEditors) LoadTextEditors();
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bodyBinding?.Cancel();
            _bodyBinding?.Dispose();
            _session.DocumentChanged -= OnDocumentChanged;
            _session.ValidationCompleted -= OnValidationCompleted;
            _session.Editing.Context.CommandApplied -= OnCommandHistory;
            _noteEditor.ContentChanged -= OnNoteContentChanged;
            DetachPluginTextSession();
            if (_pluginTagsEditor is not null)
            {
                _pluginTagsEditor.ApplyRequested -= OnPluginTagsApplyRequested;
                _pluginTagsEditor.OpenNotesRequested -= OnPluginTagsOpenNotesRequested;
                _pluginTagsEditor.PendingChangesChanged -= OnPluginTagsPendingChangesChanged;
            }
            if (_customBody is IDatabasePendingEditSource pending) pending.PendingChangesChanged -= OnPendingChangesChanged;
        }
        base.Dispose(disposing);
    }

    private ToolStrip BuildToolbar()
    {
        ToolStrip toolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, AccessibleName = "Database record commands" };
        ToolStripButton add = new("&Add") { AccessibleName = "Add record", ToolTipText = "Add a new database record" };
        ToolStripButton duplicate = new("D&uplicate") { AccessibleName = "Duplicate record", ToolTipText = "Duplicate the selected database record" };
        ToolStripButton delete = new("&Delete") { AccessibleName = "Delete record", ToolTipText = "Clear the selected database record" };
        ToolStripButton maximum = new("Change &Maximum") { AccessibleName = "Change maximum records", ToolTipText = "Change the number of record slots" };
        ToolStripButton copy = new("&Copy") { AccessibleName = "Copy records", ToolTipText = "Copy the selected records" };
        ToolStripButton paste = new("&Paste") { AccessibleName = "Paste records", ToolTipText = "Paste copied records" };
        ToolStripButton raw = new("Open &Raw") { AccessibleName = "Open raw JSON", ToolTipText = "Open the selected record in the advanced raw JSON editor" };
        ToolStripButton batchPluginTags = new("Batch Plugin &Tags…")
        {
            Visible = PluginTagDatabaseContextMapper.Supports(_kind) &&
                _definition.RecordSchema.Fields.Any(field => field.ValueKind == DatabaseFieldValueKind.Note),
            AccessibleName = "Batch edit plugin tags",
            ToolTipText = "Preview and apply one typed plugin tag across all selected records"
        };
        ToolStripButton editCommands = new("Edit Event &Commands…") { Visible = _kind == MvDocumentKind.CommonEvents && _documentOpener is not null, AccessibleName = "Edit common event commands" };
        ToolStripButton generateText = new("✨ Generate &Text…") { AccessibleName = "Generate text for selected record", ToolTipText = "Open Playroma Text Studio for a text field on the selected record", Enabled = false };
        ToolStripButton batchText = new("✨ Batch &Text…") { AccessibleName = "Batch generate text", ToolTipText = "Open Text Studio Batch Content Builder for this database", Enabled = MvDatabaseTextTargetCatalog.SupportsKind(_kind.ToString()) };
        ToolStripButton validate = new("&Validate") { AccessibleName = "Validate project", ToolTipText = "Run project validation" };
        add.Click += async (_, _) => await AddRecordAsync().ConfigureAwait(true);
        duplicate.Click += async (_, _) => await DuplicateAsync().ConfigureAwait(true);
        delete.Click += async (_, _) => await DeleteAsync().ConfigureAwait(true);
        maximum.Click += async (_, _) => await ChangeMaximumAsync().ConfigureAwait(true);
        copy.Click += (_, _) => CopySelection();
        paste.Click += async (_, _) => await PasteAsync().ConfigureAwait(true);
        raw.Click += (_, _) => { if (_selected.Count == 1) RawRecordRequested?.Invoke(this, _selected[0].Id); };
        batchPluginTags.Click += async (_, _) => await OpenBatchPluginTagsAsync().ConfigureAwait(true);
        editCommands.Click += async (_, _) => await OpenCommonEventCommandsAsync().ConfigureAwait(true);
        generateText.Click += (_, _) => RequestTextGeneration();
        batchText.Click += async (_, _) => await RequestBatchTextGenerationAsync().ConfigureAwait(true);
        validate.Click += async (_, _) => await _session.ValidateAsync().ConfigureAwait(true);
        toolbar.Items.AddRange([add, duplicate, delete, new ToolStripSeparator(), maximum, new ToolStripSeparator(), copy, paste, new ToolStripSeparator(), generateText, batchText, batchPluginTags, editCommands, raw, validate]);
        generateText.Enabled = true;
        return toolbar;
    }

    private void BuildDetailsTabs()
    {
        if (_customBody is null)
        {
            BuildPropertyForm();
            _details.TabPages.Add(new TabPage("Properties") { Controls = { _propertyFlow } });
        }
        else
        {
            _customBody.View.Dock = DockStyle.Fill;
            _details.TabPages.Add(new TabPage("Properties") { Controls = { _customBody.View } });
        }

        Panel notePanel = new() { Dock = DockStyle.Fill };
        FlowLayoutPanel noteButtons = new() { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(3), WrapContents = false };
        Button commitNote = new() { Text = "Apply Notes (Ctrl+Enter)", AutoSize = true };
        Button generateNote = new() { Text = "✨ Generate Note…", AutoSize = true, Visible = MvDatabaseTextTargetCatalog.Find(_kind.ToString(), "note") is not null };
        commitNote.Click += async (_, _) => await CommitNoteAsync().ConfigureAwait(true);
        generateNote.Click += (_, _) => RequestTextGeneration("note");
        noteButtons.Controls.AddRange([commitNote, generateNote]);
        notePanel.Controls.Add(_noteEditor);
        notePanel.Controls.Add(noteButtons);
        _notesTab = new TabPage("Notes") { Controls = { notePanel } };
        _details.TabPages.Add(_notesTab);

        if (PluginTagDatabaseContextMapper.Supports(_kind) &&
            _definition.RecordSchema.Fields.Any(field => field.ValueKind == DatabaseFieldValueKind.Note))
        {
            _pluginTagsEditor = new PluginTagsEditorControl(PluginTagCatalogHost.Catalog, _session);
            _details.TabPages.Add(new TabPage("Plugin Tags") { Controls = { _pluginTagsEditor } });
        }

        if (_customBody?.OwnsFormula != true && _definition.RecordSchema.Fields.Any(field => field.ValueKind == DatabaseFieldValueKind.Formula))
        {
            Panel formulaPanel = new() { Dock = DockStyle.Fill };
            Button commitFormula = new() { Text = "Apply Formula (Ctrl+Enter)", Dock = DockStyle.Bottom, Height = 32 };
            commitFormula.Click += async (_, _) => await CommitFormulaAsync().ConfigureAwait(true);
            formulaPanel.Controls.Add(_formulaEditor);
            formulaPanel.Controls.Add(commitFormula);
            _details.TabPages.Add(new TabPage("Formula") { Controls = { formulaPanel } });
        }
        if (SupportsTraits(_kind) && _customBody?.OwnsTraits != true) _details.TabPages.Add(new TabPage("Traits") { Controls = { _traits } });
        if (SupportsEffects(_kind) && _customBody?.OwnsEffects != true) _details.TabPages.Add(new TabPage("Effects") { Controls = { _effects } });
        _advancedTabs.Add(new TabPage("Validation") { Controls = { _validation } });
        _advancedTabs.Add(new TabPage("References") { Controls = { _references } });
        _advancedTabs.Add(new TabPage("History") { Controls = { _historyList } });
        _raw.SetLanguage(CodeEditorLanguage.PlainText);
        _advancedTabs.Add(new TabPage("Raw") { Controls = { _raw } });
    }

    private void BuildPropertyForm()
    {
        foreach (DatabaseEditorSection section in _definition.RecordSchema.Sections)
        {
            DatabaseFieldDescriptor[] visibleFields = section.Fields.Where(field => field.ValueKind is not DatabaseFieldValueKind.Note and not DatabaseFieldValueKind.Formula).ToArray();
            if (visibleFields.Length == 0) continue;
            GroupBox group = new() { Text = section.DisplayName, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Width = 760, Padding = new Padding(10) };
            TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
            int row = 0;
            foreach (DatabaseFieldDescriptor field in visibleFields)
            {
                Label label = new() { Text = field.DisplayName, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) };
                Control editor = CreateFieldEditor(field);
                editor.Dock = DockStyle.Top;
                editor.Margin = new Padding(3, 3, 3, 7);
                if (!string.IsNullOrWhiteSpace(field.HelpText)) editor.AccessibleDescription = field.HelpText;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.Controls.Add(label, 0, row);
                table.Controls.Add(editor, 1, row);
                if (IsTextStudioField(field))
                {
                    Button generate = new() { Text = "✨", Width = 34, Height = 26, Margin = new Padding(3), AccessibleName = $"Generate {field.DisplayName}" };
                    generate.Click += (_, _) => RequestTextGeneration(field.Key);
                    table.Controls.Add(generate, 2, row);
                }
                _fieldControls[field] = editor;
                row++;
            }
            group.Controls.Add(table);
            _propertyFlow.Controls.Add(group);
        }
    }

    private Control CreateFieldEditor(DatabaseFieldDescriptor field)
    {
        switch (field.ValueKind)
        {
            case DatabaseFieldValueKind.Boolean:
            {
                CheckBox control = new() { AutoSize = true, ThreeState = true };
                control.CheckStateChanged += async (_, _) =>
                {
                    if (_loading || control.CheckState == CheckState.Indeterminate) return;
                    await CommitFieldAsync(field, control.Checked).ConfigureAwait(true);
                };
                return control;
            }
            case DatabaseFieldValueKind.Enum:
            {
                ComboBox control = new() { DropDownStyle = ComboBoxStyle.DropDownList };
                if (field.Options is not null) foreach ((int id, string name) in field.Options) control.Items.Add(new OptionItem(id, name));
                control.SelectedIndexChanged += async (_, _) =>
                {
                    if (_loading || control.SelectedItem is not OptionItem item) return;
                    await CommitFieldAsync(field, item.Id).ConfigureAwait(true);
                };
                return control;
            }
            case DatabaseFieldValueKind.DatabaseReference when field.ReferenceKind.HasValue:
            {
                DatabaseIdPicker control = new(_session, field.ReferenceKind.Value) { AllowNone = (field.Minimum ?? 0) == 0 };
                control.ValueChanged += async (_, _) => { if (!_loading) await CommitFieldAsync(field, control.SelectedId).ConfigureAwait(true); };
                return control;
            }
            case DatabaseFieldValueKind.AssetReference when field.AssetCategory.HasValue:
            {
                AssetPicker control = new(_session, field.AssetCategory.Value);
                control.ValueChanged += async (_, _) => { if (!_loading) await CommitFieldAsync(field, control.Value).ConfigureAwait(true); };
                return control;
            }
            case DatabaseFieldValueKind.Icon:
            {
                IconPicker control = new(_session);
                control.ValueChanged += async (_, _) => { if (!_loading) await CommitFieldAsync(field, control.Value).ConfigureAwait(true); };
                return control;
            }
            case DatabaseFieldValueKind.Integer:
            case DatabaseFieldValueKind.Animation:
            {
                NumericUpDown control = new() { ThousandsSeparator = true };
                int minimum = field.Minimum ?? -999999999;
                int maximum = field.Maximum ?? 999999999;
                control.Minimum = minimum;
                control.Maximum = maximum;
                control.ValueChanged += async (_, _) => { if (!_loading) await CommitFieldAsync(field, decimal.ToInt32(control.Value)).ConfigureAwait(true); };
                return control;
            }
            default:
            {
                TextBox control = new() { Multiline = field.ValueKind == DatabaseFieldValueKind.MultilineString, Height = field.ValueKind == DatabaseFieldValueKind.MultilineString ? 70 : 24, ScrollBars = field.ValueKind == DatabaseFieldValueKind.MultilineString ? ScrollBars.Vertical : ScrollBars.None };
                control.Validated += async (_, _) => { if (!_loading && control.Tag as string != control.Text) await CommitFieldAsync(field, control.Text).ConfigureAwait(true); };
                control.KeyDown += async (_, e) =>
                {
                    if (e.KeyCode == Keys.Enter && e.Control)
                    {
                        await CommitFieldAsync(field, control.Text).ConfigureAwait(true);
                        e.SuppressKeyPress = true;
                    }
                };
                return control;
            }
        }
    }

    private bool IsTextStudioField(DatabaseFieldDescriptor field) =>
        (field.ValueKind is DatabaseFieldValueKind.String or DatabaseFieldValueKind.MultilineString or DatabaseFieldValueKind.Note)
        && MvDatabaseTextTargetCatalog.Find(_kind.ToString(), field.Key) is not null;

    private async void RequestTextGeneration(string? fieldKey = null)
    {
        if (_selected.Count != 1)
        {
            MessageBox.Show(this, "Select exactly one database record before opening Text Studio.", "Generate Text", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!MvDatabaseTextTargetCatalog.SupportsKind(_kind.ToString()))
        {
            MessageBox.Show(this, $"Text Studio does not expose text targets for {_definition.DisplayName} in the current editor.", "Generate Text", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (HasPendingChanges)
        {
            DialogResult pending = MessageBox.Show(
                this,
                "This record has unapplied local edits. Apply them before opening Text Studio so generation uses the values currently shown in the Database editor?",
                "Apply edits before Text Studio",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (pending != DialogResult.Yes) return;
            await ApplyPendingAsync().ConfigureAwait(true);
            if (HasPendingChanges) return;
        }
        string? selectedField = fieldKey;
        if (string.IsNullOrWhiteSpace(selectedField))
        {
            using var picker = new TextGenerationTargetPickerDialog(_kind);
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            selectedField = picker.SelectedFieldKey;
        }
        TextGenerationRequested?.Invoke(this, new DatabaseTextGenerationRequestedEventArgs(_kind, _document.RelativePath, _selected[0].Id, selectedField));
    }

    private async Task RequestBatchTextGenerationAsync()
    {
        if (!MvDatabaseTextTargetCatalog.SupportsKind(_kind.ToString()))
        {
            MessageBox.Show(this, $"Text Studio does not expose batch text targets for {_definition.DisplayName}.", "Batch Text", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (HasPendingChanges)
        {
            DialogResult pending = MessageBox.Show(this,
                "This database has unapplied local edits. Apply them before opening Batch Content Builder so its project snapshot is current?",
                "Apply edits before Batch Content Builder", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (pending != DialogResult.Yes) return;
            await ApplyPendingAsync().ConfigureAwait(true);
            if (HasPendingChanges) return;
        }
        int recordId = _selected.Count > 0 ? _selected[0].Id : 1;
        TextGenerationRequested?.Invoke(this, new DatabaseTextGenerationRequestedEventArgs(_kind, _document.RelativePath, recordId, OpenBatchBuilder: true));
    }

    private async void OnRecordSelectionRequested(object? sender, DatabaseRecordSelectionChangedEventArgs e)
    {
        if (_suppressRecordSelection) return;
        int[] currentIds = _selected.Select(record => record.Id).ToArray();
        bool sameSelection = currentIds.SequenceEqual(e.RecordIds);
        if (!sameSelection && HasPendingChanges)
        {
            DialogResult result = MessageBox.Show(
                this,
                "This record has unapplied Notes, Plugin Tags, or section changes.\n\n" +
                "Yes: Apply the pending changes and select the new record.\n" +
                "No: Discard the pending changes and select the new record.\n" +
                "Cancel: Keep editing the current record.",
                "Unapplied Record Changes",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1);
            if (result == DialogResult.Cancel)
            {
                RestoreRecordListSelection(currentIds);
                return;
            }
            if (result == DialogResult.Yes)
            {
                await ApplyPendingAsync().ConfigureAwait(true);
                if (HasPendingChanges)
                {
                    RestoreRecordListSelection(currentIds);
                    return;
                }
            }
            else
            {
                await DiscardPendingAsync().ConfigureAwait(true);
            }
        }
        SelectRecords(e.RecordIds);
    }

    private void RestoreRecordListSelection(IReadOnlyList<int> ids)
    {
        _suppressRecordSelection = true;
        try
        {
            _recordList.SelectRecords(ids);
        }
        finally
        {
            _suppressRecordSelection = false;
        }
    }

    private void SelectRecords(IReadOnlyList<int> ids)
    {
        _selected = ids.Select(id => new DatabaseRecordHandle(_kind, _document.RelativePath, id)).ToArray();
        _selectionSummary.Text = _selected.Count switch { 0 => "No record selected", 1 => $"{_definition.SingularName} {_selected[0].Id:D4}", _ => $"{_selected.Count:N0} records selected" };
        if (_selected.Count == 1) _selection.Select(new DatabaseRecordSelection(_kind, _selected[0].Id, _document.RelativePath));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        LoadSelection();
    }

    private void LoadSelection()
    {
        _loading = true;
        try
        {
            foreach ((DatabaseFieldDescriptor field, Control control) in _fieldControls)
            {
                AggregateValue<object?> aggregate = DatabaseValueAccessor.ReadAggregate(_document, _selected, field);
                ApplyAggregate(control, aggregate);
                control.Enabled = _selected.Count > 0 && (_selected.Count == 1 || field.SupportsBatchEditing);
            }
            LoadTextEditors();
            LoadStructuredEditors();
            QueueCustomBodyBinding();
            RefreshContextPanels();
        }
        finally { _loading = false; }
    }


    private void QueueCustomBodyBinding()
    {
        if (_customBody is null) return;
        if (_customBody is IDatabasePendingEditSource pending && pending.HasPendingChanges)
        {
            const string deferred = " — external refresh deferred while this section has unapplied changes";
            if (!_selectionSummary.Text.EndsWith(deferred, StringComparison.Ordinal)) _selectionSummary.Text += deferred;
            return;
        }

        _bodyBinding?.Cancel();
        _bodyBinding?.Dispose();
        _bodyBinding = new CancellationTokenSource();
        CancellationToken token = _bodyBinding.Token;
        IReadOnlyList<DatabaseRecordHandle> selection = _selected.ToArray();
        _ = BindCustomBodyAsync(selection, token);
    }

    private async Task BindCustomBodyAsync(
        IReadOnlyList<DatabaseRecordHandle> selection,
        CancellationToken cancellationToken)
    {
        try
        {
            await _customBody!.BindAsync(selection, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer record selection superseded this binding operation.
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                MessageBox.Show(this, exception.Message, "Database editor failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private static void ApplyAggregate(Control control, AggregateValue<object?> aggregate)
    {
        switch (control)
        {
            case TextBox text:
                text.Text = aggregate.Kind == AggregateValueKind.Mixed ? string.Empty : Convert.ToString(aggregate.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                text.PlaceholderText = aggregate.Kind == AggregateValueKind.Mixed ? "— Multiple values —" : string.Empty;
                text.Tag = text.Text;
                break;
            case NumericUpDown number:
                number.Text = aggregate.Kind == AggregateValueKind.Mixed ? string.Empty : Convert.ToString(aggregate.Value, CultureInfo.InvariantCulture) ?? "0";
                if (aggregate.Kind == AggregateValueKind.Uniform)
                {
                    decimal value = Convert.ToDecimal(aggregate.Value ?? 0, CultureInfo.InvariantCulture);
                    number.Value = Math.Clamp(value, number.Minimum, number.Maximum);
                }
                break;
            case CheckBox check:
                check.CheckState = aggregate.Kind == AggregateValueKind.Mixed ? CheckState.Indeterminate : Convert.ToBoolean(aggregate.Value ?? false, CultureInfo.InvariantCulture) ? CheckState.Checked : CheckState.Unchecked;
                break;
            case ComboBox combo:
                combo.SelectedIndex = aggregate.Kind == AggregateValueKind.Mixed ? -1 : combo.Items.Cast<OptionItem>().ToList().FindIndex(item => item.Id == Convert.ToInt32(aggregate.Value ?? 0, CultureInfo.InvariantCulture));
                break;
            case DatabaseIdPicker picker:
                picker.SelectedId = aggregate.Kind == AggregateValueKind.Mixed ? 0 : Convert.ToInt32(aggregate.Value ?? 0, CultureInfo.InvariantCulture);
                break;
            case AssetPicker picker:
                picker.Value = aggregate.Kind == AggregateValueKind.Mixed ? string.Empty : Convert.ToString(aggregate.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                break;
            case IconPicker icon:
                icon.Value = aggregate.Kind == AggregateValueKind.Mixed ? 0 : Convert.ToInt32(aggregate.Value ?? 0, CultureInfo.InvariantCulture);
                break;
        }
    }

    private void LoadTextEditors()
    {
        DatabaseFieldDescriptor? note = _definition.RecordSchema.Fields.FirstOrDefault(field => field.ValueKind == DatabaseFieldValueKind.Note);
        DetachPluginTextSession();
        _noteEditor.Enabled = _selected.Count > 0;
        if (note is null || _selected.Count == 0)
        {
            _noteEditor.Text = string.Empty;
            _pluginTagsEditor?.ClearBinding("Select one supported database record to edit its plugin tags.");
        }
        else
        {
            AggregateValue<object?> value = DatabaseValueAccessor.ReadAggregate(_document, _selected, note);
            string noteText = value.Kind == AggregateValueKind.Mixed ? string.Empty : Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            _noteEditor.Text = noteText;
            if (_selected.Count == 1 && _pluginTagsEditor is not null)
            {
                PluginTagContext context = PluginTagDatabaseContextMapper.Create(
                    _kind,
                    _session.Workspace.Snapshot.Identity.ProjectRoot,
                    _document.RelativePath,
                    _selected[0].Id);
                _pluginTextSession = new PluginTextEditingSession(PluginTagCatalogHost.Catalog, context, noteText);
                _pluginTextSession.WorkingTextChanged += OnPluginWorkingTextChanged;
                _pluginTextSession.DirtyStateChanged += OnPluginTextDirtyStateChanged;
                _pluginTagsEditor.Bind(
                    _pluginTextSession,
                    _pluginProjectIntelligence?.Analysis,
                    _pluginProjectIntelligence?.Availability,
                    _pluginProjectIntelligence?.ErrorMessage);
            }
            else if (_pluginTagsEditor is not null)
            {
                _pluginTagsEditor.ClearBinding(value.Kind == AggregateValueKind.Mixed
                    ? "Plugin Tags require one record at a time. The selected records contain different Notes values."
                    : "Plugin Tags require one record at a time.");
            }
        }

        DatabaseFieldDescriptor? formula = _definition.RecordSchema.Fields.FirstOrDefault(field => field.ValueKind == DatabaseFieldValueKind.Formula);
        _formulaEditor.Enabled = _selected.Count > 0;
        if (formula is null || _selected.Count == 0) _formulaEditor.Text = string.Empty;
        else
        {
            AggregateValue<object?> value = DatabaseValueAccessor.ReadAggregate(_document, _selected, formula);
            _formulaEditor.Text = value.Kind == AggregateValueKind.Mixed ? string.Empty : Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    private void LoadStructuredEditors()
    {
        if (_selected.Count != 1)
        {
            _traits.ClearBinding();
            _effects.ClearBinding();
            return;
        }
        JsonPath record = _selected[0].Path;
        if (SupportsTraits(_kind) && _customBody?.OwnsTraits != true && _document.Syntax.Resolve(record.Append("traits")) is JsonArraySyntax) _traits.Bind(_session, _document, record.Append("traits"));
        if (SupportsEffects(_kind) && _customBody?.OwnsEffects != true && _document.Syntax.Resolve(record.Append("effects")) is JsonArraySyntax) _effects.Bind(_session, _document, record.Append("effects"));
    }

    private async ValueTask CommitFieldAsync(DatabaseFieldDescriptor field, object? value)
    {
        if (_selected.Count == 0) return;
        try
        {
            await _mutations.SetFieldAsync(_selected, field, value, allowHistoryMerge: field.ValueKind is DatabaseFieldValueKind.String or DatabaseFieldValueKind.MultilineString).ConfigureAwait(true);
            foreach (DatabaseRecordHandle record in _selected) _index.UpdateRecord(record.Id);
            LoadSelection();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Database edit failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            LoadSelection();
        }
    }

    private async ValueTask CommitNoteAsync()
    {
        if (_pluginTextSession is not null)
        {
            if (_pluginTextSession.IsDirty || _noteEditor.IsModified)
                await CommitPluginTextAsync().ConfigureAwait(true);
            return;
        }
        DatabaseFieldDescriptor? field = _definition.RecordSchema.Fields.FirstOrDefault(candidate => candidate.ValueKind == DatabaseFieldValueKind.Note);
        if (field is null || !_noteEditor.IsModified) return;
        await CommitFieldAsync(field, _noteEditor.Text).ConfigureAwait(true);
        _noteEditor.MarkCommitted();
    }

    private async ValueTask CommitPluginTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DatabaseFieldDescriptor? field = _definition.RecordSchema.Fields.FirstOrDefault(candidate => candidate.ValueKind == DatabaseFieldValueKind.Note);
        if (field is null || _pluginTextSession is null || !_pluginTextSession.IsDirty) return;
        string value = _pluginTextSession.WorkingText;
        await CommitFieldAsync(field, value).ConfigureAwait(true);
        _noteEditor.MarkCommitted();
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnNoteContentChanged(object? sender, EventArgs e)
    {
        if (_loading || _syncingPluginText || _pluginTextSession is null) return;
        _pluginTextSession.ReplaceText(_noteEditor.Text);
    }

    private void OnPluginWorkingTextChanged(object? sender, PluginTextChangedEventArgs e) =>
        SynchronizeNoteEditorFromSession();

    private void OnPluginTextDirtyStateChanged(object? sender, EventArgs e)
    {
        SynchronizeNoteEditorFromSession();
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeNoteEditorFromSession()
    {
        if (_pluginTextSession is null) return;
        _syncingPluginText = true;
        try
        {
            if (!string.Equals(_noteEditor.Text, _pluginTextSession.WorkingText, StringComparison.Ordinal) ||
                _noteEditor.IsModified != _pluginTextSession.IsDirty)
            {
                _noteEditor.SetWorkingText(_pluginTextSession.WorkingText, _pluginTextSession.IsDirty);
            }
        }
        finally { _syncingPluginText = false; }
    }

    private void DetachPluginTextSession()
    {
        if (_pluginTextSession is null) return;
        _pluginTextSession.WorkingTextChanged -= OnPluginWorkingTextChanged;
        _pluginTextSession.DirtyStateChanged -= OnPluginTextDirtyStateChanged;
        _pluginTextSession = null;
    }

    private async void OnPluginTagsApplyRequested(object? sender, EventArgs e) =>
        await CommitPluginTextAsync().ConfigureAwait(true);

    private void OnPluginTagsOpenNotesRequested(object? sender, EventArgs e)
    {
        if (_notesTab is not null) _details.SelectedTab = _notesTab;
        _noteEditor.Focus();
    }

    private void OnPluginTagsPendingChangesChanged(object? sender, EventArgs e) =>
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);

    private async Task LoadPluginTagProjectIntelligenceAsync()
    {
        if (_pluginTagsEditor is null) return;
        int revision = Interlocked.Increment(ref _pluginIntelligenceRevision);
        string projectRoot = _session.Workspace.Snapshot.Identity.ProjectRoot;
        PluginTagProjectIntelligence intelligence = await YanflyPluginTagProjectCache
            .GetAsync(projectRoot, PluginTagCatalogHost.Catalog)
            .ConfigureAwait(true);
        if (IsDisposed || revision != _pluginIntelligenceRevision) return;
        _pluginProjectIntelligence = intelligence;
        _pluginTagsEditor.SetProjectIntelligence(intelligence);
    }

    private async ValueTask CommitFormulaAsync()
    {
        DatabaseFieldDescriptor? field = _definition.RecordSchema.Fields.FirstOrDefault(candidate => candidate.ValueKind == DatabaseFieldValueKind.Formula);
        if (field is null || !_formulaEditor.IsModified) return;
        await CommitFieldAsync(field, _formulaEditor.Text).ConfigureAwait(true);
        _formulaEditor.MarkCommitted();
    }


    private async Task OpenBatchPluginTagsAsync()
    {
        DatabaseFieldDescriptor? noteField = _definition.RecordSchema.Fields
            .FirstOrDefault(field => field.ValueKind == DatabaseFieldValueKind.Note);
        if (noteField is null || _selected.Count == 0 || !PluginTagDatabaseContextMapper.Supports(_kind)) return;

        if (HasPendingChanges)
        {
            DialogResult pendingResult = MessageBox.Show(
                this,
                "The current record has unapplied changes. Apply them before opening the batch Plugin Tags editor?",
                "Apply Pending Changes",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (pendingResult != DialogResult.OK) return;
            await ApplyPendingAsync().ConfigureAwait(true);
            if (HasPendingChanges) return;
        }

        PluginTagCatalog catalog = PluginTagCatalogHost.Catalog;
        Dictionary<string, DatabaseRecordHandle> recordsByKey = new(StringComparer.Ordinal);
        List<PluginTagBatchRecord> batchRecords = [];
        foreach (DatabaseRecordHandle record in _selected)
        {
            string key = record.Id.ToString(CultureInfo.InvariantCulture);
            string noteText = Convert.ToString(DatabaseValueAccessor.Read(_document, record, noteField), CultureInfo.InvariantCulture) ?? string.Empty;
            PluginTagContext recordContext = PluginTagDatabaseContextMapper.Create(
                _kind,
                _session.Workspace.Snapshot.Identity.ProjectRoot,
                _document.RelativePath,
                record.Id);
            recordsByKey.Add(key, record);
            batchRecords.Add(new PluginTagBatchRecord(key, new PluginTextEditingSession(catalog, recordContext, noteText)));
        }

        PluginTagContext commonContext = batchRecords[0].Session.Context with { RecordId = null };
        using PluginTagBatchEditorDialog dialog = new(
            _session,
            catalog,
            commonContext,
            batchRecords,
            _pluginProjectIntelligence?.Availability);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        PluginTagBatchPlan plan = dialog.Plan;
        if (!plan.CanApply || plan.ChangedCount == 0) return;
        List<IEditorCommand> commands = [];
        foreach (PluginTagBatchRecordChange change in plan.Records.Where(change => change.Changed))
        {
            DatabaseRecordHandle record = recordsByKey[change.RecordKey];
            JsonPath target = DatabaseValueAccessor.Combine(record.Path, noteField.RelativePath);
            commands.Add(new SetJsonValueCommand(
                record.RelativePath,
                target,
                noteField.EncodeObject(change.After),
                $"Set plugin tags on {_definition.SingularName} {record.Id}"));
        }
        if (commands.Count == 0) return;

        try
        {
            await _mutations.ExecuteAsync(
                new CompositeEditorCommand($"Batch edit Plugin Tags on {commands.Count} {_definition.DisplayName.ToLowerInvariant()} record(s)", commands))
                .ConfigureAwait(true);
            foreach (PluginTagBatchRecordChange change in plan.Records.Where(change => change.Changed))
                _index.UpdateRecord(recordsByKey[change.RecordKey].Id);
            LoadSelection();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Batch Plugin Tags failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            LoadSelection();
        }
    }

    private async Task OpenCommonEventCommandsAsync()
    {
        if (_kind != MvDocumentKind.CommonEvents || _documentOpener is null || _selected.Count != 1) return;
        DatabaseRecordHandle record = _selected[0];
        string name = _document.GetString(record.Path.Append("name")) ?? $"Common Event {record.Id:D4}";
        DocumentRequest request = new(
            EditorDocumentKind.EventEditor,
            $"event:common:{record.Id}",
            _document.RelativePath,
            MvDocumentKind.CommonEvents,
            RecordId: record.Id,
            DisplayTitle: $"Common Event {record.Id:D4} — {name}",
            EventSurface: "CommonEvent",
            EventPageIndex: 0);
        await _documentOpener(request).ConfigureAwait(true);
    }

    private async ValueTask AddRecordAsync()
    {
        int id = FindNextFreeId();
        await _mutations.CreateRecordAsync(_definition, _document.RelativePath, id).ConfigureAwait(true);
        _index.Rebuild();
        _recordList.SelectRecord(id);
    }

    private async ValueTask DuplicateAsync()
    {
        if (_selected.Count != 1) return;
        int id = FindNextFreeId();
        await _mutations.DuplicateRecordAsync(_definition, _document, _selected[0].Id, id).ConfigureAwait(true);
        _index.Rebuild();
        _recordList.SelectRecord(id);
    }

    private async ValueTask DeleteAsync()
    {
        if (_selected.Count == 0) return;
        List<ProjectReference> exact = [];
        ReferenceTargetKind targetKind = DatabaseMaximumChangePlanner.ToReferenceTargetKind(_kind);
        if (targetKind != ReferenceTargetKind.Unknown)
        {
            foreach (DatabaseRecordHandle record in _selected) exact.AddRange(_session.References.FindInbound(new ReferenceTarget(targetKind, record.Id), includeHeuristic: false));
        }
        if (exact.Count > 0)
        {
            MessageBox.Show(this, $"Deletion is blocked by {exact.Count:N0} exact reference(s). Open the References tab to review them.", "Referenced records", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this, $"Replace {_selected.Count:N0} selected record slot(s) with null? IDs will not shift.", "Delete records", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        int[] ids = _selected.Select(record => record.Id).ToArray();
        await _mutations.DeleteRecordsAsync(_document.RelativePath, ids).ConfigureAwait(true);
        _index.Rebuild();
    }

    private async ValueTask ChangeMaximumAsync()
    {
        DatabaseMaximumChangePlanner planner = new();
        using DatabaseMaximumDialog dialog = new(_index.HighestId, maximum => planner.CreatePlan(_document, _kind, maximum, _session.References));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Plan is null) return;
        if (dialog.Plan.HasPopulatedRemovals && MessageBox.Show(this, "This removes populated records. Continue?", "Confirm maximum reduction", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        await using ProjectTransaction transaction = _session.Editing.BeginTransaction("Change database maximum");
        HashSet<(string Path, string JsonPath)> remappedLocations = [];
        foreach (ProjectReference reference in dialog.Plan.ExactReferences)
        {
            if (!reference.Target.Id.HasValue || !dialog.Remappings.TryGetValue(reference.Target.Id.Value, out int destinationId))
            {
                throw new InvalidOperationException($"Exact reference to removed ID {reference.Target.Id?.ToString(CultureInfo.InvariantCulture) ?? "?"} has no remapping.");
            }
            string relativePath = reference.Source.RelativePath.Replace('\\', '/');
            if (!remappedLocations.Add((relativePath, reference.Source.JsonPath))) continue;
            transaction.Add(new SetJsonValueCommand(
                relativePath,
                new JsonPath(reference.Source.JsonPath),
                destinationId.ToString(CultureInfo.InvariantCulture),
                $"Remap {_definition.SingularName} {reference.Target.Id.Value} to {destinationId}"));
        }
        transaction.Add(new ResizeDatabaseArrayCommand(_document.RelativePath, dialog.Plan.RequestedMaximum));
        await transaction.CommitAsync().ConfigureAwait(true);
        _index.Rebuild();
    }

    private void CopySelection()
    {
        if (_selected.Count == 0) return;
        List<ClipboardDatabaseRecord> records = [];
        List<ClipboardReference> references = [];
        HashSet<string> assets = [];
        foreach (DatabaseRecordHandle record in _selected)
        {
            string raw = _document.GetRawJson(record.Path) ?? "null";
            records.Add(new ClipboardDatabaseRecord(record.Id, raw));
            foreach (ProjectReference reference in _session.References.FindFromRecord(_document.RelativePath, record.Id))
            {
                references.Add(new ClipboardReference(reference.Source.JsonPath, reference.Target, reference.Confidence, reference.Context));
                if (reference.Target.Kind == ReferenceTargetKind.Asset && !string.IsNullOrWhiteSpace(reference.Target.Name)) assets.Add(reference.Target.Name);
            }
        }
        DatabaseClipboardPayload payload = new()
        {
            FormatVersion = DatabaseClipboardPayload.CurrentFormatVersion,
            SourceProjectId = CreateProjectIdentity(_session.Workspace.Snapshot.Identity.ProjectRoot),
            SourceKind = _kind,
            Records = records,
            References = references,
            Assets = assets.ToArray()
        };
        DataObject data = new();
        data.SetData(DatabaseClipboardPayload.MimeType, payload.Serialize());
        data.SetText(payload.Serialize());
        Clipboard.SetDataObject(data, true);
    }

    private async ValueTask PasteAsync()
    {
        IDataObject? data = Clipboard.GetDataObject();
        string? json = data?.GetData(DatabaseClipboardPayload.MimeType) as string;
        if (string.IsNullOrWhiteSpace(json)) return;
        DatabaseClipboardPayload payload;
        try { payload = DatabaseClipboardPayload.Deserialize(json); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Invalid clipboard", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        HashSet<int> occupied = _index.Records.Where(entry => entry.IsPopulated).Select(entry => entry.Id).ToHashSet();
        DatabasePastePlan plan = DatabasePastePlanner.Create(payload, _kind, occupied, availableAssets: _session.Assets.Assets.Select(asset => asset.Key.LogicalName).ToArray());
        if (!plan.IsKindCompatible)
        {
            MessageBox.Show(this, $"The clipboard contains {plan.SourceKind} records and cannot be pasted into {_kind}.", "Incompatible records", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (plan.RequiresReview && MessageBox.Show(this, $"Paste requires review: {plan.HeuristicReferences.Count} heuristic reference(s), {plan.MissingAssets.Count} missing asset(s). Continue without rewriting them?", "Paste review", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await using ProjectTransaction transaction = _session.BeginTransaction($"Paste {plan.Assignments.Count} {_definition.DisplayName}");
        foreach (DatabasePasteAssignment assignment in plan.Assignments) transaction.Add(new InsertDatabaseRecordCommand(_document.RelativePath, assignment.DestinationId, assignment.RawJson));
        await transaction.CommitAsync().ConfigureAwait(true);
        _index.Rebuild();
        _recordList.SelectRecords(plan.Assignments.Select(assignment => assignment.DestinationId));
    }

    private int FindNextFreeId()
    {
        foreach (DatabaseRecordIndexEntry entry in _index.Records) if (!entry.IsPopulated) return entry.Id;
        return _index.HighestId + 1;
    }

    private void RefreshContextPanels()
    {
        RefreshValidationRows();
        RefreshReferenceRows();
        RefreshHistoryRows();
        _raw.Text = _selected.Count == 1 ? _document.GetRawJson(_selected[0].Path) ?? "null" : _selected.Count == 0 ? string.Empty : $"{_selected.Count} records selected";
    }

    private void RefreshDiagnostics()
    {
        if (_session.LastValidation is not null) _index.SetDiagnostics(_session.LastValidation.Diagnostics.Select(item => item.Diagnostic));
        RefreshValidationRows();
    }

    private void RefreshValidationRows()
    {
        _validation.Rows.Clear();
        if (_session.LastValidation is null) return;
        HashSet<int> ids = _selected.Select(record => record.Id).ToHashSet();
        foreach (ProjectValidationDiagnostic item in _session.LastValidation.Diagnostics.Where(item => string.Equals(item.Diagnostic.Location.RelativePath, _document.RelativePath, PathComparison) && (!item.Diagnostic.Location.RecordId.HasValue || ids.Contains(item.Diagnostic.Location.RecordId.Value))))
        {
            _validation.Rows.Add(item.Severity, item.Code, item.Diagnostic.Message, item.Diagnostic.Location.JsonPath ?? string.Empty);
        }
    }

    private void RefreshReferenceRows()
    {
        _references.Rows.Clear();
        if (_selected.Count == 0) return;
        ReferenceTargetKind targetKind = DatabaseMaximumChangePlanner.ToReferenceTargetKind(_kind);
        foreach (DatabaseRecordHandle record in _selected)
        {
            foreach (ProjectReference reference in _session.References.FindFromRecord(_document.RelativePath, record.Id)) _references.Rows.Add("Outgoing", reference.Confidence, reference.Target, reference.Source.JsonPath);
            if (targetKind == ReferenceTargetKind.Unknown) continue;
            foreach (ProjectReference reference in _session.References.FindInbound(new ReferenceTarget(targetKind, record.Id))) _references.Rows.Add("Incoming", reference.Confidence, reference.Source.Label ?? reference.Source.RelativePath, reference.Source.JsonPath);
        }
    }

    private void RefreshHistoryRows()
    {
        _historyList.Items.Clear();
        if (_selected.Count != 1) return;
        foreach (DatabaseHistoryEntry entry in _history.ForRecord(_document.RelativePath, _selected[0].Id)) _historyList.Items.Add($"{entry.TimestampUtc.ToLocalTime():g}  {entry.Description}");
        if (_historyList.Items.Count == 0) _historyList.Items.Add("No edits recorded in this session.");
    }

    private void OnDocumentChanged(object? sender, EditorCommandEvent e)
    {
        if (_pluginTagsEditor is not null && e.Targets.Any(target =>
                target.NormalizedRelativePath.EndsWith("js/plugins.js", StringComparison.OrdinalIgnoreCase)))
        {
            string projectRoot = _session.Workspace.Snapshot.Identity.ProjectRoot;
            YanflyPluginTagProjectCache.Invalidate(projectRoot);
            _ = LoadPluginTagProjectIntelligenceAsync();
        }

        EditorMutationTarget[] targets = e.Targets
            .Where(target => string.Equals(target.NormalizedRelativePath, _document.RelativePath, PathComparison))
            .ToArray();
        if (targets.Length == 0) return;

        void Refresh()
        {
            int[] selected = _selected.Select(record => record.Id).ToArray();
            bool requiresRebuild = targets.Any(target =>
                target.Kind == EditorMutationTargetKind.File
                || target.Path == JsonPath.Root
                || target.Path.Segments().Count == 0);
            int[] changedIds = targets
                .Select(target => target.Path.Segments().FirstOrDefault())
                .Where(segment => int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                .Select(segment => int.Parse(segment!, CultureInfo.InvariantCulture))
                .Where(id => id > 0)
                .Distinct()
                .ToArray();

            int currentPhysicalLength = (_document.Syntax.Root as JsonArraySyntax)?.Elements.Count ?? -1;
            if (requiresRebuild || changedIds.Length == 0 || _index.PhysicalLength != currentPhysicalLength)
            {
                _index.Rebuild();
            }
            else
            {
                _index.UpdateRecords(changedIds);
            }
            _recordList.SelectRecords(selected);
            if (HasPendingChanges)
            {
                const string deferred = " — external refresh deferred while Notes or Plugin Tags have unapplied changes";
                if (!_selectionSummary.Text.EndsWith(deferred, StringComparison.Ordinal)) _selectionSummary.Text += deferred;
                return;
            }
            LoadSelection();
        }
        if (InvokeRequired) BeginInvoke((Action)Refresh); else Refresh();
    }

    private void OnValidationCompleted(object? sender, ProjectValidationResult e)
    {
        if (InvokeRequired) { BeginInvoke(new Action(RefreshDiagnostics)); return; }
        RefreshDiagnostics();
    }

    private void OnCommandHistory(object? sender, EditorCommandEvent e)
    {
        _history.Observe(e, _kind);
        if (InvokeRequired) BeginInvoke(new Action(RefreshHistoryRows)); else RefreshHistoryRows();
    }


    private void OnPendingChangesChanged(object? sender, EventArgs e) => PendingChangesChanged?.Invoke(this, EventArgs.Empty);

    private static Control? FindFirstEditable(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child.Visible && child.Enabled && child.TabStop && child is TextBoxBase or ComboBox or NumericUpDown or DataGridView)
            {
                return child;
            }
            Control? nested = FindFirstEditable(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static Guid CreateProjectIdentity(string projectRoot)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(projectRoot)));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static bool SupportsTraits(MvDocumentKind kind) => kind is MvDocumentKind.Actors or MvDocumentKind.Classes or MvDocumentKind.Weapons or MvDocumentKind.Armors or MvDocumentKind.Enemies or MvDocumentKind.States;
    private static bool SupportsEffects(MvDocumentKind kind) => kind is MvDocumentKind.Skills or MvDocumentKind.Items;
    private static DataGridView ReadOnlyGrid(params string[] columns)
    {
        DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        foreach (string column in columns) grid.Columns.Add(column, column);
        return grid;
    }
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private sealed record OptionItem(int Id, string Name) { public override string ToString() => Name; }
}
