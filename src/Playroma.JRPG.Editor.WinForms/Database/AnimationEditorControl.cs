using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.AnimationEditing;
using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Assets.Windows;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class AnimationEditorControl : UserControl, IDatabaseEditorBody
{
    private readonly MvProjectSession _session;
    private readonly MutableJsonProjectDocument _document;
    private readonly AnimationMutationService _mutations;
    private readonly Label _selectionMessage = Phase9BEditorUi.SelectionLabel();
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly AssetIndexSelectionControl _sheet1;
    private readonly AssetIndexSelectionControl _sheet2;
    private readonly NumericUpDown _hue1 = Phase9BEditorUi.Number(0, 360);
    private readonly NumericUpDown _hue2 = Phase9BEditorUi.Number(0, 360);
    private readonly ComboBox _position = Phase9BEditorUi.OptionCombo(new Dictionary<int, string>
    {
        [0] = "Head", [1] = "Center", [2] = "Feet", [3] = "Screen"
    });
    private readonly AnimationPatternPalette _palette = new() { Dock = DockStyle.Fill };
    private readonly AnimationCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly AnimationTimelineControl _timeline = new() { Dock = DockStyle.Fill };
    private readonly ListBox _frameList = new() { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Animation frames" };
    private readonly Label _canvasHelp = new() { Dock = DockStyle.Bottom, Height = 24, Text = "Drag cells to move them. Double-click a cell row to edit its pattern, scale, rotation, opacity, mirror, and blend.", AutoEllipsis = true, Padding = new Padding(6, 2, 6, 0) };
    private readonly DataGridView _cells = Grid("Lane", "Pattern", "X", "Y", "Scale", "Rotation", "Mirror", "Opacity", "Blend", "Runtime");
    private readonly DataGridView _timings = Grid("#", "Frame", "Sound", "Flash", "Duration", "Extensions");
    private readonly TextBox _extensions = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font(FontFamily.GenericMonospace, 9), WordWrap = false };
    private readonly Label _frameStatus = new() { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(6), TextAlign = ContentAlignment.MiddleLeft };
    private readonly CheckBox _onionPrevious = new() { Text = "Previous", AutoSize = true };
    private readonly CheckBox _onionNext = new() { Text = "Next", AutoSize = true };
    private readonly CheckBox _loop = new() { Text = "Loop", Checked = true, AutoSize = true };
    private readonly ComboBox _speed = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly System.Windows.Forms.Timer _playback = new();
    private DateTime _playbackStarted;
    private TimeSpan _playbackOffset;
    private bool _playing;
    private int _recordId;
    private MvAnimationSnapshot? _snapshot;
    private List<MvAnimationFrameSnapshot> _frames = [];
    private List<MvAnimationTimingSnapshot> _timingItems = [];
    private Bitmap? _sheet1Image;
    private Bitmap? _sheet2Image;
    private int _selectedPattern = -1;
    private bool _loading;
    private bool _syncingCellSelection;
    private bool _syncingFrameSelection;
    private CancellationTokenSource? _sheetLoadCancellation;

    public AnimationEditorControl(
        MvProjectSession session,
        MutableJsonProjectDocument document,
        IDatabaseEditorDefinition definition)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        ArgumentNullException.ThrowIfNull(definition);
        _mutations = new AnimationMutationService(session.Editing);
        _sheet1 = new AssetIndexSelectionControl(session, AssetCategory.Animations, false);
        _sheet2 = new AssetIndexSelectionControl(session, AssetCategory.Animations, false);
        Dock = DockStyle.Fill;
        _speed.Items.AddRange(["25%", "50%", "100%", "200%"]);
        _speed.SelectedItem = "100%";
        _playback.Interval = 16;
        _playback.Tick += (_, _) => AdvancePlayback();

        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("Studio") { Controls = { BuildStudio() } });
        tabs.TabPages.Add(new TabPage("Extensions") { Controls = { BuildExtensionsPage() } });
        Controls.Add(tabs);
        Controls.Add(_selectionMessage);

        _sheet1.SelectionChanged += async (_, _) => await ReloadSheetsAsync().ConfigureAwait(true);
        _sheet2.SelectionChanged += async (_, _) => await ReloadSheetsAsync().ConfigureAwait(true);
        _hue1.ValueChanged += async (_, _) => { if (!_loading) await ReloadSheetsAsync().ConfigureAwait(true); };
        _hue2.ValueChanged += async (_, _) => { if (!_loading) await ReloadSheetsAsync().ConfigureAwait(true); };
        _palette.PatternSelected += (_, e) => _selectedPattern = e.Pattern;
        _palette.PatternActivated += async (_, e) => await AddPatternCellAsync(e.Pattern).ConfigureAwait(true);
        _canvas.SelectionChanged += (_, _) => SyncCellGridSelectionFromCanvas();
        _canvas.CellsDragCompleted += async (_, e) => await MoveSelectedCellsAsync(e).ConfigureAwait(true);
        _timeline.FrameSelected += (_, e) => SetCurrentFrame(e.FrameIndex);
        _frameList.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _syncingFrameSelection || _frameList.SelectedIndex < 0) return;
            SetCurrentFrame(_frameList.SelectedIndex);
        };
        _frameList.DoubleClick += async (_, _) => await DuplicateFrameAsync().ConfigureAwait(true);
        _cells.SelectionChanged += (_, _) => SyncCanvasSelectionFromGrid();
        _cells.CellDoubleClick += async (_, _) => await EditSelectedCellAsync().ConfigureAwait(true);
        _timings.CellDoubleClick += async (_, _) => await EditTimingAsync().ConfigureAwait(true);
        _onionPrevious.CheckedChanged += (_, _) => { _canvas.ShowPreviousOnion = _onionPrevious.Checked; _canvas.Invalidate(); };
        _onionNext.CheckedChanged += (_, _) => { _canvas.ShowNextOnion = _onionNext.Checked; _canvas.Invalidate(); };
    }

    public Control View => this;
    public bool OwnsTraits => false;
    public bool OwnsEffects => false;
    public bool OwnsFormula => false;

    public async ValueTask BindAsync(IReadOnlyList<DatabaseRecordHandle> records, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _loading = true;
        try
        {
            StopPlayback();
            if (records.Count != 1 || _document.Syntax.Resolve(records[0].Path) is not JsonObjectSyntax)
            {
                _recordId = 0;
                _snapshot = null;
                Enabled = false;
                _selectionMessage.Text = records.Count == 0 ? "Select an animation to edit it." : "The animation studio requires one populated record.";
                RefreshAll();
                return;
            }

            Enabled = true;
            _recordId = records[0].Id;
            _snapshot = MvAnimationReader.Read(_document, _recordId);
            _frames = _snapshot.Frames.ToList();
            _timingItems = _snapshot.Timings.ToList();
            _name.Text = _snapshot.Name;
            _sheet1.Selection = new AssetIndexSelection(_snapshot.Animation1Name, 0);
            _sheet2.Selection = new AssetIndexSelection(_snapshot.Animation2Name, 0);
            Phase9BEditorUi.Set(_hue1, _snapshot.Animation1Hue);
            Phase9BEditorUi.Set(_hue2, _snapshot.Animation2Hue);
            Phase9BEditorUi.SelectOption(_position, _snapshot.Position);
            _selectionMessage.Text = $"Animation {_recordId:D4} — {_snapshot.FrameCount} frame(s), {_snapshot.StoredCellCount} stored cell row(s), {_snapshot.TimingCount} timing(s). Raw extension data is preserved.";
            await ReloadSheetsAsync(cancellationToken).ConfigureAwait(true);
            SetCurrentFrame(Math.Min(_timeline.CurrentFrame, Math.Max(0, _frames.Count - 1)));
            RefreshAll();
        }
        finally
        {
            _loading = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopPlayback();
            _playback.Dispose();
            _sheetLoadCancellation?.Cancel();
            _sheetLoadCancellation?.Dispose();
            _sheet1Image?.Dispose();
            _sheet2Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    private Control BuildStudio()
    {
        // The old layout stacked the pattern browser underneath the preview. At normal
        // database-window heights that left only a thin strip for both the canvas and
        // animation patterns. The editor is much wider than it is tall, so use that
        // space: settings remain across the top and the preview/pattern browser share
        // the lower workspace side-by-side behind a user-adjustable splitter.
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(6)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(BuildTopStudioArea(), 0, 0);
        root.Controls.Add(BuildFrameStudioArea(), 0, 1);
        return root;
    }

    private Control BuildTopStudioArea()
    {
        TableLayoutPanel top = new() { Dock = DockStyle.Fill, ColumnCount = 2 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        top.Controls.Add(BuildGeneralSettingsGroup(), 0, 0);
        top.Controls.Add(BuildTimingSummaryGroup(), 1, 0);
        return top;
    }

    private Control BuildGeneralSettingsGroup()
    {
        GroupBox group = new() { Text = "General Settings", Dock = DockStyle.Fill, Padding = new Padding(8) };
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        AddGeneralRow(table, 0, "Name:", _name, null);
        AddGeneralRow(table, 1, "Image 1:", _sheet1, _hue1);
        AddGeneralRow(table, 2, "Image 2:", _sheet2, _hue2);
        AddGeneralRow(table, 3, "Position:", _position, null);
        NumericUpDown frameCount = new() { Minimum = 1, Maximum = 9999, Dock = DockStyle.Fill, ReadOnly = true, ThousandsSeparator = true, AccessibleName = "Animation frame count" };
        frameCount.Name = "AnimationFrameCountDisplay";
        table.Controls.Add(new Label { Text = "Frames:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 4);
        table.Controls.Add(frameCount, 1, 4);
        Button apply = new() { Text = "Apply", Dock = DockStyle.Fill };
        apply.Click += async (_, _) => await ApplyGeneralAsync().ConfigureAwait(true);
        table.Controls.Add(apply, 2, 4);
        group.Controls.Add(table);
        group.Tag = frameCount;
        return group;
    }

    private static void AddGeneralRow(TableLayoutPanel table, int row, string label, Control value, Control? extra)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        table.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
        value.Dock = DockStyle.Fill;
        table.Controls.Add(value, 1, row);
        if (extra is not null)
        {
            extra.Dock = DockStyle.Fill;
            table.Controls.Add(extra, 2, row);
        }
    }

    private Control BuildTimingSummaryGroup()
    {
        GroupBox group = new() { Text = "SE and Flash Timing", Dock = DockStyle.Fill, Padding = new Padding(8) };
        ToolStrip tools = new() { Dock = DockStyle.Bottom, GripStyle = ToolStripGripStyle.Hidden };
        ToolStripButton add = new("Add");
        ToolStripButton edit = new("Edit");
        ToolStripButton remove = new("Delete");
        add.Click += async (_, _) => await AddTimingAsync().ConfigureAwait(true);
        edit.Click += async (_, _) => await EditTimingAsync().ConfigureAwait(true);
        remove.Click += async (_, _) => await DeleteTimingAsync().ConfigureAwait(true);
        tools.Items.AddRange([add, edit, remove]);
        group.Controls.Add(_timings);
        group.Controls.Add(tools);
        return group;
    }

    private Control BuildFrameStudioArea()
    {
        TableLayoutPanel area = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        GroupBox framesGroup = new() { Text = "Frames", Dock = DockStyle.Fill, Padding = new Padding(6) };
        Panel frames = new() { Dock = DockStyle.Fill };
        ToolStrip frameTools = new()
        {
            Dock = DockStyle.Bottom,
            GripStyle = ToolStripGripStyle.Hidden,
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow
        };
        ToolStripButton add = new("+") { ToolTipText = "Insert frame" };
        ToolStripButton duplicate = new("Copy") { ToolTipText = "Duplicate selected frame" };
        ToolStripButton delete = new("−") { ToolTipText = "Delete selected frame" };
        add.Click += async (_, _) => await InsertFrameAsync().ConfigureAwait(true);
        duplicate.Click += async (_, _) => await DuplicateFrameAsync().ConfigureAwait(true);
        delete.Click += async (_, _) => await DeleteFramesAsync().ConfigureAwait(true);
        frameTools.Items.AddRange([add, duplicate, delete]);
        frames.Controls.Add(_frameList);
        frames.Controls.Add(frameTools);
        framesGroup.Controls.Add(frames);

        GroupBox previewGroup = new() { Text = "Animation Preview", Dock = DockStyle.Fill, Padding = new Padding(6) };
        Panel canvasPanel = new() { Dock = DockStyle.Fill };
        canvasPanel.Controls.Add(_canvas);
        canvasPanel.Controls.Add(_canvasHelp);
        canvasPanel.Controls.Add(BuildPlaybackToolbar());
        previewGroup.Controls.Add(canvasPanel);

        SplitContainer workspace = ControlHelpers.CreateSplitContainer(
            Orientation.Vertical,
            splitterDistance: 520,
            panel1MinSize: 300,
            panel2MinSize: 320,
            splitterWidth: 6);
        workspace.AccessibleName = "Animation preview and pattern browser splitter";
        workspace.Panel1.Padding = new Padding(0, 0, 3, 0);
        workspace.Panel2.Padding = new Padding(3, 0, 0, 0);
        workspace.Panel1.Controls.Add(previewGroup);
        workspace.Panel2.Controls.Add(BuildPatternStrip());

        area.Controls.Add(framesGroup, 0, 0);
        area.Controls.Add(workspace, 1, 0);
        return area;
    }

    private Control BuildPatternStrip()
    {
        GroupBox group = new() { Text = "Patterns — double-click to add to the current frame", Dock = DockStyle.Fill, Padding = new Padding(6) };
        TabControl tabs = new() { Dock = DockStyle.Fill };
        TabPage first = new("Image 1") { Controls = { _palette } };
        AnimationPatternPalette secondPalette = new() { Dock = DockStyle.Fill, ActiveSheet = 1 };
        secondPalette.PatternSelected += (_, e) => { _selectedPattern = e.Pattern; _palette.SelectedPattern = e.Pattern; };
        secondPalette.PatternActivated += async (_, e) => await AddPatternCellAsync(e.Pattern).ConfigureAwait(true);
        _palette.Tag = secondPalette;
        TabPage second = new("Image 2") { Controls = { secondPalette } };
        tabs.TabPages.Add(first);
        tabs.TabPages.Add(second);
        group.Controls.Add(tabs);
        return group;
    }

    private void ShowCellEditor()
    {
        using Form dialog = new()
        {
            Text = $"Animation Cells — Frame {_timeline.CurrentFrame + 1}",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(980, 520),
            MinimumSize = new Size(760, 420)
        };
        Control panel = BuildCellPanel();
        panel.Dock = DockStyle.Fill;
        dialog.Controls.Add(panel);
        dialog.FormClosed += (_, _) =>
        {
            panel.Controls.Remove(_cells);
            Controls.Add(_cells);
            _cells.Visible = false;
        };
        dialog.ShowDialog(this);
    }

    private Control BuildGeneralToolbar()
    {
        TableLayoutPanel table = new() { Dock = DockStyle.Top, Height = 112, ColumnCount = 8, Padding = new Padding(6) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
        table.Controls.Add(new Label { Text = "Name", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        table.Controls.Add(_name, 1, 0);
        table.Controls.Add(new Label { Text = "Sheet 1", Anchor = AnchorStyles.Left, AutoSize = true }, 2, 0);
        table.Controls.Add(_sheet1, 3, 0);
        table.Controls.Add(new Label { Text = "Hue", Anchor = AnchorStyles.Left, AutoSize = true }, 4, 0);
        table.Controls.Add(_hue1, 5, 0);
        table.Controls.Add(new Label { Text = "Position", Anchor = AnchorStyles.Left, AutoSize = true }, 6, 0);
        table.Controls.Add(_position, 7, 0);
        table.Controls.Add(new Label { Text = "Sheet 2", Anchor = AnchorStyles.Left, AutoSize = true }, 2, 1);
        table.Controls.Add(_sheet2, 3, 1);
        table.Controls.Add(new Label { Text = "Hue", Anchor = AnchorStyles.Left, AutoSize = true }, 4, 1);
        table.Controls.Add(_hue2, 5, 1);
        Button apply = new() { Text = "Apply General", Dock = DockStyle.Fill };
        apply.Click += async (_, _) => await ApplyGeneralAsync().ConfigureAwait(true);
        table.Controls.Add(apply, 6, 1);
        table.SetColumnSpan(apply, 2);
        return table;
    }

    private Control BuildPlaybackToolbar()
    {
        ToolStrip tools = new()
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Height = 32,
            CanOverflow = true,
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow
        };
        ToolStripButton first = new("|<") { ToolTipText = "First frame" };
        ToolStripButton previous = new("<") { ToolTipText = "Previous frame" };
        ToolStripButton play = new("Play");
        ToolStripButton stop = new("Stop");
        ToolStripButton next = new(">") { ToolTipText = "Next frame" };
        ToolStripButton last = new(">|") { ToolTipText = "Last frame" };
        ToolStripButton previewTarget = new("Change Target…") { ToolTipText = "Configure the preview target" };
        ToolStripButton editCells = new("Edit Cells…") { ToolTipText = "Open the detailed cell editor" };
        ToolStripDropDownButton frameActions = new("Frame Actions");
        ToolStripMenuItem pasteLast = new("Paste Last");
        ToolStripMenuItem tween = new("Tween…");
        ToolStripMenuItem batch = new("Batch…");
        ToolStripMenuItem shift = new("Shift…");
        ToolStripControlHost loop = new(_loop);
        ToolStripControlHost speed = new(_speed);
        ToolStripControlHost previousOnion = new(_onionPrevious);
        ToolStripControlHost nextOnion = new(_onionNext);

        first.Click += (_, _) => SetCurrentFrame(0);
        previous.Click += (_, _) => SetCurrentFrame(_timeline.CurrentFrame - 1);
        play.Click += (_, _) => TogglePlayback();
        stop.Click += (_, _) => StopPlayback();
        next.Click += (_, _) => SetCurrentFrame(_timeline.CurrentFrame + 1);
        last.Click += (_, _) => SetCurrentFrame(Math.Max(0, _frames.Count - 1));
        previewTarget.Click += (_, _) => EditPreviewTarget();
        editCells.Click += (_, _) => ShowCellEditor();
        pasteLast.Click += async (_, _) => await CopyPreviousFrameAsync().ConfigureAwait(true);
        tween.Click += async (_, _) => await TweenAsync().ConfigureAwait(true);
        batch.Click += async (_, _) => await BatchEditAsync().ConfigureAwait(true);
        shift.Click += async (_, _) => await BatchEditAsync().ConfigureAwait(true);
        frameActions.DropDownItems.AddRange([pasteLast, tween, batch, shift]);

        tools.Items.AddRange([
            first, previous, play, stop, next, last,
            new ToolStripSeparator(),
            new ToolStripLabel("Speed"), speed, loop,
            new ToolStripSeparator(),
            previewTarget, editCells, frameActions,
            new ToolStripSeparator(),
            new ToolStripLabel("Onion"), previousOnion, nextOnion
        ]);
        return tools;
    }

    private Control BuildWorkspace()
    {
        SplitContainer vertical = ControlHelpers.CreateSplitContainer(Orientation.Horizontal, 500, 300, 130);
        SplitContainer main = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 285, 220, 500);
        TabControl paletteTabs = new() { Dock = DockStyle.Fill };
        TabPage sheet1 = new("Sheet 1") { Controls = { _palette } };
        TabPage sheet2 = new("Sheet 2");
        Label secondInfo = new() { Dock = DockStyle.Top, Height = 26, Text = "Sheet 2 patterns are stored as 100–199.", Padding = new Padding(5) };
        AnimationPatternPalette secondPalette = new() { Dock = DockStyle.Fill, ActiveSheet = 1 };
        secondPalette.PatternSelected += (_, e) => { _selectedPattern = e.Pattern; _palette.SelectedPattern = e.Pattern; };
        secondPalette.PatternActivated += async (_, e) => await AddPatternCellAsync(e.Pattern).ConfigureAwait(true);
        _palette.Tag = secondPalette;
        sheet2.Controls.Add(secondPalette);
        sheet2.Controls.Add(secondInfo);
        paletteTabs.TabPages.Add(sheet1);
        paletteTabs.TabPages.Add(sheet2);
        main.Panel1.Controls.Add(paletteTabs);

        SplitContainer editor = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 620, 380, 260);
        editor.Panel1.Controls.Add(_canvas);
        editor.Panel2.Controls.Add(BuildCellPanel());
        main.Panel2.Controls.Add(editor);
        vertical.Panel1.Controls.Add(main);
        vertical.Panel2.Controls.Add(BuildTimelinePanel());
        return vertical;
    }

    private Control BuildCellPanel()
    {
        Panel panel = new() { Dock = DockStyle.Fill, Padding = new Padding(4) };
        ToolStrip tools = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        ToolStripButton add = new("Add Pattern");
        ToolStripButton edit = new("Edit");
        ToolStripButton clear = new("Clear");
        ToolStripButton duplicate = new("Duplicate");
        ToolStripButton batch = new("Batch");
        add.Click += async (_, _) => await AddPatternCellAsync(_selectedPattern).ConfigureAwait(true);
        edit.Click += async (_, _) => await EditSelectedCellAsync().ConfigureAwait(true);
        clear.Click += async (_, _) => await ClearSelectedCellsAsync().ConfigureAwait(true);
        duplicate.Click += async (_, _) => await DuplicateSelectedCellAsync().ConfigureAwait(true);
        batch.Click += async (_, _) => await BatchEditAsync().ConfigureAwait(true);
        tools.Items.AddRange([add, edit, clear, duplicate, new ToolStripSeparator(), batch]);
        panel.Controls.Add(_cells);
        panel.Controls.Add(tools);
        return panel;
    }

    private Control BuildTimelinePanel()
    {
        Panel panel = new() { Dock = DockStyle.Fill };
        ToolStrip tools = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        ToolStripButton insert = new("Insert");
        ToolStripButton duplicate = new("Duplicate");
        ToolStripButton delete = new("Delete");
        ToolStripButton copyPrevious = new("Copy Previous");
        ToolStripButton moveLeft = new("Move Left");
        ToolStripButton moveRight = new("Move Right");
        ToolStripButton copy = new("Copy");
        ToolStripButton paste = new("Paste");
        ToolStripButton reverse = new("Reverse Selection");
        ToolStripButton tween = new("Tween");
        insert.Click += async (_, _) => await InsertFrameAsync().ConfigureAwait(true);
        duplicate.Click += async (_, _) => await DuplicateFrameAsync().ConfigureAwait(true);
        delete.Click += async (_, _) => await DeleteFramesAsync().ConfigureAwait(true);
        copyPrevious.Click += async (_, _) => await CopyPreviousFrameAsync().ConfigureAwait(true);
        moveLeft.Click += async (_, _) => await MoveFramesAsync(-1).ConfigureAwait(true);
        moveRight.Click += async (_, _) => await MoveFramesAsync(1).ConfigureAwait(true);
        copy.Click += (_, _) => CopyFrames();
        paste.Click += async (_, _) => await PasteFramesAsync().ConfigureAwait(true);
        reverse.Click += async (_, _) => await ReverseFramesAsync().ConfigureAwait(true);
        tween.Click += async (_, _) => await TweenAsync().ConfigureAwait(true);
        tools.Items.AddRange([insert, duplicate, delete, copyPrevious, moveLeft, moveRight, new ToolStripSeparator(), copy, paste, reverse, new ToolStripSeparator(), tween]);
        panel.Controls.Add(_timeline);
        panel.Controls.Add(tools);
        panel.Controls.Add(_frameStatus);
        return panel;
    }

    private Control BuildTimings()
    {
        Panel panel = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        ToolStrip tools = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        ToolStripButton add = new("Add");
        ToolStripButton edit = new("Edit");
        ToolStripButton duplicate = new("Duplicate");
        ToolStripButton remove = new("Delete");
        ToolStripButton move = new("Move to Current Frame");
        ToolStripButton sort = new("Sort by Frame");
        add.Click += async (_, _) => await AddTimingAsync().ConfigureAwait(true);
        edit.Click += async (_, _) => await EditTimingAsync().ConfigureAwait(true);
        duplicate.Click += async (_, _) => await DuplicateTimingAsync().ConfigureAwait(true);
        remove.Click += async (_, _) => await DeleteTimingAsync().ConfigureAwait(true);
        move.Click += async (_, _) => await MoveTimingToCurrentFrameAsync().ConfigureAwait(true);
        sort.Click += async (_, _) => await SortTimingsAsync().ConfigureAwait(true);
        tools.Items.AddRange([add, edit, duplicate, remove, move, new ToolStripSeparator(), sort]);
        panel.Controls.Add(_timings);
        panel.Controls.Add(tools);
        return panel;
    }

    private Control BuildExtensionsPage() => _extensions;

    private void EditPreviewTarget()
    {
        using AnimationPreviewTargetDialog dialog = new(_canvas.PreviewTarget);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _canvas.PreviewTarget = dialog.Target;
        _canvas.Invalidate();
        _timeline.Invalidate();
    }

    private void CopyFrames()
    {
        if (_snapshot is null || _frames.Count == 0) return;
        int[] selected = (_timeline.SelectedFrames.Count > 0 ? _timeline.SelectedFrames : [_timeline.CurrentFrame]).Distinct().Order().ToArray();
        Dictionary<int, int> oldToNew = selected.Select((oldIndex, newIndex) => (oldIndex, newIndex)).ToDictionary(pair => pair.oldIndex, pair => pair.newIndex);
        MvAnimationFrameSnapshot[] frames = selected.Select((oldIndex, newIndex) => _frames[oldIndex] with { Index = newIndex, Cells = _frames[oldIndex].Cells.ToArray() }).ToArray();
        MvAnimationTimingSnapshot[] timings = _timingItems.Where(timing => oldToNew.ContainsKey(timing.Frame))
            .Select((timing, index) => timing with { Index = index, Frame = oldToNew[timing.Frame] }).ToArray();
        AnimationClipboardEnvelope envelope = new(
            AnimationClipboardEnvelope.CurrentFormat,
            AnimationClipboardEnvelope.CurrentVersion,
            _session.Workspace.Snapshot.Identity.ProjectRoot,
            _recordId,
            _snapshot.Animation1Name,
            _snapshot.Animation2Name,
            frames,
            timings);
        Clipboard.SetText(MvAnimationSerializer.SerializeClipboard(envelope));
    }

    private async ValueTask PasteFramesAsync()
    {
        if (_recordId <= 0 || !Clipboard.ContainsText()) return;
        try
        {
            AnimationClipboardEnvelope envelope = MvAnimationSerializer.DeserializeClipboard(Clipboard.GetText());
            HashSet<string> sheets = _session.Assets.Assets.Where(asset => asset.Key.Category == AssetCategory.Animations).Select(asset => asset.Key.LogicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            HashSet<string> sounds = _session.Assets.Assets.Where(asset => asset.Key.Category == AssetCategory.SoundEffects).Select(asset => asset.Key.LogicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            AnimationDependencyReport report = AnimationClipboardAnalyzer.Analyze(envelope, sheets, sounds);
            if (report.HasProblems)
            {
                string warning = $"The pasted frames have unresolved dependencies.\r\n\r\nMissing sheets: {string.Join(", ", report.MissingSheets.DefaultIfEmpty("none"))}\r\nMissing sound effects: {string.Join(", ", report.MissingSoundEffects.DefaultIfEmpty("none"))}\r\nOut-of-range patterns: {string.Join(", ", report.OutOfRangePatterns.DefaultIfEmpty(-1).Where(value => value >= 0))}\r\n\r\nPaste while preserving the raw references?";
                if (MessageBox.Show(this, warning, "Animation Dependencies", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            int insertion = Math.Clamp(_timeline.CurrentFrame + 1, 0, _frames.Count);
            int count = envelope.Frames.Count;
            _timingItems = _timingItems.Select(timing => timing.Frame >= insertion ? timing with { Frame = timing.Frame + count } : timing).ToList();
            List<MvAnimationFrameSnapshot> frames = _frames.ToList();
            frames.InsertRange(insertion, envelope.Frames.Select(frame => frame with { Cells = frame.Cells.ToArray() }));
            _frames = AnimationFrameOperations.Reindex(frames).ToList();
            _timingItems.AddRange(envelope.Timings.Select(timing => timing with { Frame = insertion + timing.Frame }));
            ReindexTimings();
            await CommitFramesAndTimingsAsync("Paste animation frames").ConfigureAwait(true);
            SetCurrentFrame(insertion);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ExternalException)
        {
            Phase9BEditorUi.ShowError(this, exception);
        }
    }

    private async ValueTask MoveFramesAsync(int direction)
    {
        if (_frames.Count < 2 || direction == 0) return;
        int[] selected = (_timeline.SelectedFrames.Count > 0 ? _timeline.SelectedFrames : [_timeline.CurrentFrame]).Distinct().Order().ToArray();
        if (selected.Length == 0) return;
        if (direction < 0 && selected[0] == 0) return;
        if (direction > 0 && selected[^1] == _frames.Count - 1) return;
        List<int> order = Enumerable.Range(0, _frames.Count).ToList();
        HashSet<int> selectedSet = selected.ToHashSet();
        List<int> moving = order.Where(selectedSet.Contains).ToList();
        order.RemoveAll(selectedSet.Contains);
        int insertion = direction < 0
            ? selected[0] - 1
            : Math.Min(order.Count, selected[^1] - selected.Length + 2);
        order.InsertRange(insertion, moving);
        Dictionary<int, int> map = order.Select((oldIndex, newIndex) => (oldIndex, newIndex)).ToDictionary(pair => pair.oldIndex, pair => pair.newIndex);
        _frames = order.Select((oldIndex, newIndex) => _frames[oldIndex] with { Index = newIndex, Cells = _frames[oldIndex].Cells.ToArray() }).ToList();
        _timingItems = _timingItems.Select(timing => map.TryGetValue(timing.Frame, out int newFrame) ? timing with { Frame = newFrame } : timing).ToList();
        await CommitFramesAndTimingsAsync(direction < 0 ? "Move animation frames left" : "Move animation frames right").ConfigureAwait(true);
        SetCurrentFrame(map[_timeline.CurrentFrame]);
    }

    private async ValueTask ApplyGeneralAsync()
    {
        if (_recordId <= 0) return;
        try
        {
            await _mutations.SetGeneralAsync(
                _document.RelativePath,
                _recordId,
                _name.Text,
                _sheet1.Selection.AssetName,
                decimal.ToInt32(_hue1.Value),
                _sheet2.Selection.AssetName,
                decimal.ToInt32(_hue2.Value),
                Phase9BEditorUi.SelectedOption(_position)).ConfigureAwait(true);
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async ValueTask ReloadSheetsAsync(CancellationToken cancellationToken = default)
    {
        if (_loading && _snapshot is null) return;
        _sheetLoadCancellation?.Cancel();
        _sheetLoadCancellation?.Dispose();
        _sheetLoadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = _sheetLoadCancellation.Token;
        try
        {
            Bitmap? first = await LoadSheetAsync(_sheet1.Selection.AssetName, decimal.ToInt32(_hue1.Value), token).ConfigureAwait(true);
            Bitmap? second = await LoadSheetAsync(_sheet2.Selection.AssetName, decimal.ToInt32(_hue2.Value), token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            _sheet1Image?.Dispose();
            _sheet2Image?.Dispose();
            _sheet1Image = first;
            _sheet2Image = second;
            _palette.SetSheets(first is null ? null : new Bitmap(first), second is null ? null : new Bitmap(second));
            if (_palette.Tag is AnimationPatternPalette secondPalette)
            {
                secondPalette.SetSheets(first is null ? null : new Bitmap(first), second is null ? null : new Bitmap(second));
            }
            RefreshProjection();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            Phase9BEditorUi.ShowError(this, exception);
        }
    }

    private async ValueTask<Bitmap?> LoadSheetAsync(string name, int hue, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        AssetRecord? record = _session.Assets.Find(new AssetKey(AssetCategory.Animations, name));
        AssetVariant? variant = record?.PreferredVariant;
        if (variant is null || variant.IsEncrypted) return null;
        Bitmap source = await AssetImageLoader.LoadOwnedBitmapAsync(_session.AssetContent, variant.RelativePath, cancellationToken).ConfigureAwait(true);
        if (hue == 0) return source;
        using (source) return EnemyEditorControl.ApplyHue(source, hue);
    }

    private async ValueTask AddPatternCellAsync(int pattern)
    {
        if (_recordId <= 0 || _frames.Count == 0 || pattern < 0) return;
        int frameIndex = _timeline.CurrentFrame;
        MvAnimationFrameSnapshot frame = _frames[frameIndex];
        int lane = Enumerable.Range(0, Math.Max(MvAnimationConstants.RuntimeCellCount, frame.Cells.Count + 1))
            .FirstOrDefault(index => index >= frame.Cells.Count || !frame.Cells[index].IsVisible);
        MvAnimationCellSnapshot cell = new(pattern, 0, 0, 100, 0, false, 255, 0, "[-1,0,0,100,0,false,255,0]");
        _frames[frameIndex] = AnimationTweenEngine.SetCell(frame, lane, cell);
        await CommitFramesAsync("Add animation cell").ConfigureAwait(true);
        _canvas.SelectLane(lane);
    }

    private async ValueTask EditSelectedCellAsync()
    {
        if (_frames.Count == 0 || _cells.SelectedRows.Count == 0) return;
        int lane = Convert.ToInt32(_cells.SelectedRows[0].Cells[0].Value, CultureInfo.InvariantCulture) - 1;
        int frameIndex = _timeline.CurrentFrame;
        if (lane < 0 || lane >= _frames[frameIndex].Cells.Count) return;
        using AnimationCellDialog dialog = new(_frames[frameIndex].Cells[lane]);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _frames[frameIndex] = AnimationTweenEngine.SetCell(_frames[frameIndex], lane, dialog.Cell);
        await CommitFramesAsync("Edit animation cell").ConfigureAwait(true);
    }

    private async ValueTask ClearSelectedCellsAsync()
    {
        if (_frames.Count == 0) return;
        int frameIndex = _timeline.CurrentFrame;
        foreach (int lane in SelectedLanes())
        {
            if (lane >= _frames[frameIndex].Cells.Count) continue;
            MvAnimationCellSnapshot original = _frames[frameIndex].Cells[lane];
            _frames[frameIndex] = AnimationTweenEngine.SetCell(_frames[frameIndex], lane, original with { Pattern = -1 });
        }
        await CommitFramesAsync("Clear animation cells").ConfigureAwait(true);
    }

    private async ValueTask DuplicateSelectedCellAsync()
    {
        if (_frames.Count == 0) return;
        int sourceLane = SelectedLanes().FirstOrDefault(-1);
        if (sourceLane < 0) return;
        int frameIndex = _timeline.CurrentFrame;
        MvAnimationFrameSnapshot frame = _frames[frameIndex];
        if (sourceLane >= frame.Cells.Count) return;
        int targetLane = Enumerable.Range(0, Math.Max(MvAnimationConstants.RuntimeCellCount, frame.Cells.Count + 1)).FirstOrDefault(index => index >= frame.Cells.Count || !frame.Cells[index].IsVisible);
        _frames[frameIndex] = AnimationTweenEngine.SetCell(frame, targetLane, frame.Cells[sourceLane] with { RawJson = frame.Cells[sourceLane].RawJson });
        await CommitFramesAsync("Duplicate animation cell").ConfigureAwait(true);
        _canvas.SelectLane(targetLane);
    }

    private async ValueTask BatchEditAsync()
    {
        if (_frames.Count == 0) return;
        using AnimationBatchDialog dialog = new();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        IReadOnlyCollection<int> selectedFrames = _timeline.SelectedFrames.Count > 0 ? _timeline.SelectedFrames : [_timeline.CurrentFrame];
        IReadOnlyCollection<int> lanes = SelectedLanes();
        if (lanes.Count == 0) lanes = selectedFrames.SelectMany(frame => Enumerable.Range(0, _frames[frame].Cells.Count)).Distinct().ToArray();
        AnimationCellAddress[] addresses = selectedFrames.SelectMany(frame => lanes.Select(lane => new AnimationCellAddress(frame, lane))).ToArray();
        _frames = AnimationBatchEditor.Apply(_frames, addresses, dialog.Edit).ToList();
        await CommitFramesAsync("Batch edit animation cells").ConfigureAwait(true);
    }

    private async ValueTask MoveSelectedCellsAsync(AnimationCellsDragEventArgs e)
    {
        if (_frames.Count == 0 || e.Lanes.Count == 0) return;
        AnimationCellAddress[] addresses = e.Lanes.Select(lane => new AnimationCellAddress(_timeline.CurrentFrame, lane)).ToArray();
        _frames = AnimationBatchEditor.Apply(_frames, addresses, new AnimationBatchEdit(OffsetX: e.DeltaX, OffsetY: e.DeltaY)).ToList();
        await CommitFramesAsync("Move animation cells").ConfigureAwait(true);
    }

    private async ValueTask InsertFrameAsync()
    {
        _frames = AnimationFrameOperations.Insert(_frames, Math.Min(_frames.Count, _timeline.CurrentFrame + 1)).ToList();
        await CommitFramesAsync("Insert animation frame").ConfigureAwait(true);
        SetCurrentFrame(Math.Min(_frames.Count - 1, _timeline.CurrentFrame + 1));
    }

    private async ValueTask DuplicateFrameAsync()
    {
        if (_frames.Count == 0) { await InsertFrameAsync().ConfigureAwait(true); return; }
        int current = _timeline.CurrentFrame;
        _frames = AnimationFrameOperations.Insert(_frames, current + 1, _frames[current]).ToList();
        await CommitFramesAsync("Duplicate animation frame").ConfigureAwait(true);
        SetCurrentFrame(current + 1);
    }

    private async ValueTask DeleteFramesAsync()
    {
        if (_frames.Count == 0) return;
        HashSet<int> selected = _timeline.SelectedFrames.ToHashSet();
        if (selected.Count == 0) selected.Add(_timeline.CurrentFrame);
        _frames = AnimationFrameOperations.Delete(_frames, selected).ToList();
        if (_frames.Count == 0) _frames.Add(new MvAnimationFrameSnapshot(0, [], "[]"));
        _timingItems = _timingItems.Where(timing => !selected.Contains(timing.Frame)).Select(timing => timing with { Frame = timing.Frame - selected.Count(index => index < timing.Frame) }).ToList();
        await CommitFramesAndTimingsAsync("Delete animation frames").ConfigureAwait(true);
        SetCurrentFrame(Math.Min(_timeline.CurrentFrame, _frames.Count - 1));
    }

    private async ValueTask CopyPreviousFrameAsync()
    {
        int current = _timeline.CurrentFrame;
        if (current <= 0 || current >= _frames.Count) return;
        _frames[current] = _frames[current - 1] with { Index = current, Cells = _frames[current - 1].Cells.ToArray() };
        await CommitFramesAsync("Copy previous animation frame").ConfigureAwait(true);
    }

    private async ValueTask ReverseFramesAsync()
    {
        if (_timeline.SelectedFrames.Count < 2) return;
        int start = _timeline.SelectedFrames.Min();
        int end = _timeline.SelectedFrames.Max();
        _frames = AnimationFrameOperations.Reverse(_frames, start, end).ToList();
        foreach (int timingIndex in Enumerable.Range(0, _timingItems.Count))
        {
            MvAnimationTimingSnapshot timing = _timingItems[timingIndex];
            if (timing.Frame >= start && timing.Frame <= end) _timingItems[timingIndex] = timing with { Frame = start + end - timing.Frame };
        }
        await CommitFramesAndTimingsAsync("Reverse animation frames").ConfigureAwait(true);
    }

    private async ValueTask TweenAsync()
    {
        if (_frames.Count < 2) return;
        using AnimationTweenDialog dialog = new(_frames.Count, _timeline.CurrentFrame);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _frames = AnimationTweenEngine.Apply(_frames, dialog.Request).ToList();
            await CommitFramesAsync("Tween animation frames").ConfigureAwait(true);
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async ValueTask AddTimingAsync()
    {
        MvAnimationTimingSnapshot timing = new(_timingItems.Count, _timeline.CurrentFrame, 0, [0, 0, 0, 0], 5, null, new Dictionary<string, string>(), "{}");
        using AnimationTimingDialog dialog = new(_session, timing, _frames.Count);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _timingItems.Add(dialog.Timing with { Index = _timingItems.Count });
        await CommitTimingsAsync("Add animation timing").ConfigureAwait(true);
    }

    private async ValueTask EditTimingAsync()
    {
        int index = SelectedTimingIndex();
        if (index < 0 || index >= _timingItems.Count) return;
        using AnimationTimingDialog dialog = new(_session, _timingItems[index], _frames.Count);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _timingItems[index] = dialog.Timing with { Index = index };
        await CommitTimingsAsync("Edit animation timing").ConfigureAwait(true);
    }

    private async ValueTask DuplicateTimingAsync()
    {
        int index = SelectedTimingIndex();
        if (index < 0 || index >= _timingItems.Count) return;
        _timingItems.Insert(index + 1, _timingItems[index] with { Index = index + 1 });
        ReindexTimings();
        await CommitTimingsAsync("Duplicate animation timing").ConfigureAwait(true);
    }

    private async ValueTask DeleteTimingAsync()
    {
        int index = SelectedTimingIndex();
        if (index < 0 || index >= _timingItems.Count) return;
        _timingItems.RemoveAt(index);
        ReindexTimings();
        await CommitTimingsAsync("Delete animation timing").ConfigureAwait(true);
    }

    private async ValueTask MoveTimingToCurrentFrameAsync()
    {
        int index = SelectedTimingIndex();
        if (index < 0 || index >= _timingItems.Count) return;
        _timingItems[index] = _timingItems[index] with { Frame = _timeline.CurrentFrame };
        await CommitTimingsAsync("Move animation timing").ConfigureAwait(true);
    }

    private async ValueTask SortTimingsAsync()
    {
        _timingItems = _timingItems.OrderBy(timing => timing.Frame).ThenBy(timing => timing.Index).Select((timing, index) => timing with { Index = index }).ToList();
        await CommitTimingsAsync("Sort animation timings").ConfigureAwait(true);
    }

    private async ValueTask CommitFramesAsync(string description)
    {
        try
        {
            await _mutations.SetFramesAsync(_document.RelativePath, _recordId, _frames, description).ConfigureAwait(true);
            RefreshAll();
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async ValueTask CommitTimingsAsync(string description)
    {
        try
        {
            await _mutations.SetTimingsAsync(_document.RelativePath, _recordId, _timingItems, description).ConfigureAwait(true);
            RefreshAll();
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async ValueTask CommitFramesAndTimingsAsync(string description)
    {
        try
        {
            await _mutations.SetFramesAndTimingsAsync(
                _document.RelativePath,
                _recordId,
                _frames,
                _timingItems,
                description).ConfigureAwait(true);
            RefreshAll();
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private void SetCurrentFrame(int frameIndex)
    {
        if (_frames.Count == 0) frameIndex = 0;
        else frameIndex = Math.Clamp(frameIndex, 0, _frames.Count - 1);
        _timeline.CurrentFrame = frameIndex;
        _canvas.FrameIndex = frameIndex;
        _syncingFrameSelection = true;
        try
        {
            if (_frameList.Items.Count > 0) _frameList.SelectedIndex = Math.Clamp(frameIndex, 0, _frameList.Items.Count - 1);
        }
        finally { _syncingFrameSelection = false; }
        RefreshCellGrid();
        _frameStatus.Text = _frames.Count == 0 ? "No frames" : $"Frame {frameIndex + 1} / {_frames.Count} • 4 runtime ticks per frame • approximately 15 FPS at normal speed";
    }

    private void RefreshAll()
    {
        RefreshProjection();
        RefreshFrameList();
        RefreshCellGrid();
        RefreshTimingGrid();
        UpdateFrameCountDisplay();
        UpdateExtensionsText(_extensions);
    }


    private void RefreshFrameList()
    {
        int selected = _timeline.CurrentFrame;
        _syncingFrameSelection = true;
        try
        {
            _frameList.BeginUpdate();
            _frameList.Items.Clear();
            for (int index = 0; index < _frames.Count; index++)
            {
                int visibleCells = _frames[index].Cells.Count(cell => cell.IsVisible);
                _frameList.Items.Add($"#{index + 1:D3}   {visibleCells} cell{(visibleCells == 1 ? string.Empty : "s")}");
            }
            if (_frameList.Items.Count > 0) _frameList.SelectedIndex = Math.Clamp(selected, 0, _frameList.Items.Count - 1);
            _frameList.EndUpdate();
        }
        finally { _syncingFrameSelection = false; }
    }

    private void UpdateFrameCountDisplay()
    {
        NumericUpDown? display = FindControlByName<NumericUpDown>(this, "AnimationFrameCountDisplay");
        if (display is not null) display.Value = Math.Clamp(_frames.Count, (int)display.Minimum, (int)display.Maximum);
    }

    private static T? FindControlByName<T>(Control root, string name) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match && string.Equals(match.Name, name, StringComparison.Ordinal)) return match;
            T? nested = FindControlByName<T>(child, name);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void RefreshProjection()
    {
        if (_snapshot is not null)
        {
            _snapshot = _snapshot with
            {
                Name = _name.Text,
                Animation1Name = _sheet1.Selection.AssetName,
                Animation1Hue = decimal.ToInt32(_hue1.Value),
                Animation2Name = _sheet2.Selection.AssetName,
                Animation2Hue = decimal.ToInt32(_hue2.Value),
                Position = Phase9BEditorUi.SelectedOption(_position),
                Frames = _frames,
                Timings = _timingItems
            };
        }
        _canvas.SetAnimation(_snapshot, _sheet1Image, _sheet2Image);
        _timeline.SetAnimation(_snapshot, _sheet1Image, _sheet2Image);
        _canvas.Invalidate();
        _timeline.Invalidate();
    }

    private void RefreshCellGrid()
    {
        _cells.Rows.Clear();
        if (_frames.Count == 0 || _timeline.CurrentFrame >= _frames.Count) return;
        MvAnimationFrameSnapshot frame = _frames[_timeline.CurrentFrame];
        for (int lane = 0; lane < frame.Cells.Count; lane++)
        {
            MvAnimationCellSnapshot cell = frame.Cells[lane];
            _cells.Rows.Add(lane + 1, cell.Pattern, cell.X, cell.Y, cell.Scale, cell.Rotation, cell.Mirror, cell.Opacity, BlendName(cell.BlendMode), lane < MvAnimationConstants.RuntimeCellCount ? "Visible lane" : "Preserved extension");
        }
    }

    private void RefreshTimingGrid()
    {
        _timings.Rows.Clear();
        foreach (MvAnimationTimingSnapshot timing in _timingItems)
        {
            string flash = timing.FlashScope switch { 1 => "Target", 2 => "Screen", 3 => "Hide Target", _ => "None" };
            _timings.Rows.Add(timing.Index + 1, timing.Frame + 1, timing.SoundEffect?.Name ?? string.Empty, flash, timing.FlashDuration, string.Join(", ", timing.ExtensionFields.Keys));
        }
    }

    private void SyncCanvasSelectionFromGrid()
    {
        if (_loading || _syncingCellSelection || IsDisposed) return;
        _syncingCellSelection = true;
        try
        {
            List<int> lanes = [];
            foreach (DataGridViewRow row in _cells.SelectedRows)
            {
                if (row.IsNewRow || row.Cells.Count == 0 || row.Cells[0].Value is null) continue;
                if (!int.TryParse(
                        Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int displayedLane))
                {
                    continue;
                }

                int lane = displayedLane - 1;
                if (lane >= 0) lanes.Add(lane);
            }

            if (lanes.Count == 0)
            {
                _canvas.SelectLane(-1);
                return;
            }

            for (int index = 0; index < lanes.Count; index++)
            {
                _canvas.SelectLane(lanes[index], additive: index > 0);
            }
        }
        finally
        {
            _syncingCellSelection = false;
        }
    }

    private void SyncCellGridSelectionFromCanvas()
    {
        if (_loading || _syncingCellSelection || IsDisposed) return;
        _syncingCellSelection = true;
        try
        {
            _cells.ClearSelection();
            foreach (int lane in _canvas.SelectedLanes.ToArray())
            {
                if (lane >= 0 && lane < _cells.Rows.Count) _cells.Rows[lane].Selected = true;
            }
        }
        finally
        {
            _syncingCellSelection = false;
        }
    }

    private IReadOnlyCollection<int> SelectedLanes() => _canvas.SelectedLanes.Count > 0
        ? _canvas.SelectedLanes
        : _cells.SelectedRows.Cast<DataGridViewRow>().Select(row => Convert.ToInt32(row.Cells[0].Value, CultureInfo.InvariantCulture) - 1).Where(index => index >= 0).ToArray();

    private int SelectedTimingIndex() => _timings.SelectedRows.Count == 0 ? -1 : _timings.SelectedRows[0].Index;

    private void TogglePlayback()
    {
        if (_playing) { _playbackOffset += DateTime.UtcNow - _playbackStarted; _playing = false; _playback.Stop(); return; }
        if (_frames.Count == 0) return;
        _playing = true;
        _playbackStarted = DateTime.UtcNow;
        _playback.Start();
    }

    private void StopPlayback()
    {
        _playing = false;
        _playback.Stop();
        _playbackOffset = TimeSpan.Zero;
        SetCurrentFrame(0);
    }

    private void AdvancePlayback()
    {
        if (!_playing || _frames.Count == 0) return;
        TimeSpan elapsed = _playbackOffset + (DateTime.UtcNow - _playbackStarted);
        int frame = AnimationPlaybackClock.FrameAt(elapsed, _frames.Count, _loop.Checked, PlaybackSpeed());
        SetCurrentFrame(frame);
        if (!_loop.Checked && frame == _frames.Count - 1 && elapsed >= TimeSpan.FromSeconds(_frames.Count / (15d * PlaybackSpeed())))
        {
            _playing = false;
            _playback.Stop();
        }
    }

    private double PlaybackSpeed() => _speed.SelectedItem?.ToString() switch { "25%" => 0.25, "50%" => 0.5, "200%" => 2.0, _ => 1.0 };

    private void ReindexTimings()
    {
        for (int index = 0; index < _timingItems.Count; index++) _timingItems[index] = _timingItems[index] with { Index = index };
    }

    private void UpdateExtensionsText(TextBox text)
    {
        if (_snapshot is null) { text.Text = string.Empty; return; }
        StringBuilder builder = new();
        builder.AppendLine("Animation root extension fields:");
        foreach ((string name, string value) in _snapshot.ExtensionFields) builder.AppendLine($"  {name} = {value}");
        builder.AppendLine();
        builder.AppendLine("Timing extension fields:");
        foreach (MvAnimationTimingSnapshot timing in _timingItems.Where(timing => timing.ExtensionFields.Count > 0))
        {
            builder.AppendLine($"  Timing {timing.Index + 1} (frame {timing.Frame + 1})");
            foreach ((string name, string value) in timing.ExtensionFields) builder.AppendLine($"    {name} = {value}");
        }
        builder.AppendLine();
        builder.AppendLine("Frames with preserved extension cells beyond MV Core lanes 0–15:");
        foreach (MvAnimationFrameSnapshot frame in _frames.Where(frame => frame.Cells.Count > MvAnimationConstants.RuntimeCellCount))
        {
            builder.AppendLine($"  Frame {frame.Index + 1}: {frame.Cells.Count - MvAnimationConstants.RuntimeCellCount} extension cell(s)");
        }
        text.Text = builder.ToString();
    }

    private static string BlendName(int value) => value switch { 0 => "Normal", 1 => "Additive", 2 => "Multiply", 3 => "Screen", _ => $"Unknown #{value}" };

    private static DataGridView Grid(params string[] headers)
    {
        DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, RowHeadersVisible = false };
        foreach (string header in headers) grid.Columns.Add(header.Replace(" ", string.Empty), header);
        return grid;
    }
}
