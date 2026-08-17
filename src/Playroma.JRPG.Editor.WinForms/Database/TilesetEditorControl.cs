using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Assets.Windows;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Rendering;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class TilesetEditorControl : UserControl, IDatabaseEditorBody
{
    private readonly MvProjectSession _session;
    private readonly MutableJsonProjectDocument _document;
    private readonly Phase9EMutationService _mutations;
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _mode = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _note = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, AcceptsTab = true };
    private readonly Dictionary<TileSheetKind, AssetPicker> _sheetAssignments = [];
    private readonly TilesetFlagCanvas _canvas = new() { Dock = DockStyle.Fill, AccessibleName = "Tileset flag canvas" };
    private readonly TrackBar _zoom = new() { Dock = DockStyle.Bottom, Minimum = 10, Maximum = 400, Value = 50, TickFrequency = 50, Height = 30 };
    private readonly ComboBox _tool = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox _paintProperty = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly ComboBox _paintValue = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly FlowLayoutPanel _directionPanel = new() { AutoSize = true, WrapContents = false, Dock = DockStyle.Top };
    private readonly CheckBox[] _directions =
    [
        new CheckBox { Text = "↓", AutoSize = true }, new CheckBox { Text = "←", AutoSize = true },
        new CheckBox { Text = "→", AutoSize = true }, new CheckBox { Text = "↑", AutoSize = true }
    ];
    private readonly NumericUpDown _rawValue = new() { Minimum = int.MinValue, Maximum = int.MaxValue, Dock = DockStyle.Top, Visible = false, Hexadecimal = true };
    private readonly CheckBox _paintOnDrag = new() { Text = "Paint while dragging", AutoSize = true };
    private readonly CheckBox _allShapes = new() { Text = "All 48 autotile shapes", AutoSize = true, Checked = true };
    private readonly Label _inspector = new() { Dock = DockStyle.Bottom, Height = 56, AutoEllipsis = true, Padding = new Padding(6) };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, AutoEllipsis = true, Padding = new Padding(6, 0, 6, 0) };
    private readonly Label _previewCaption = new() { Dock = DockStyle.Top, Height = 20, Padding = new Padding(6, 2, 6, 0), AutoEllipsis = true };
    private readonly Button _normalize = new() { Text = "Normalize to 8192…", AutoSize = true };
    private readonly Button _apply = new() { Text = "Apply to selected tile", AutoSize = true };
    private readonly Button _copyFlag = new() { Text = "Copy Flag", AutoSize = true };
    private readonly Button _pasteFlag = new() { Text = "Paste Flag", AutoSize = true, Enabled = false };
    private readonly Button _tabA = new() { Text = "A", Width = 36, Height = 26 };
    private readonly Button _tabB = new() { Text = "B", Width = 36, Height = 26 };
    private readonly Button _tabC = new() { Text = "C", Width = 36, Height = 26 };
    private readonly Button _passage = new() { Text = "Passage", Width = 148, Height = 32 };
    private readonly Button _passage4 = new() { Text = "Passage (4 dir)", Width = 148, Height = 32 };
    private readonly Button _ladder = new() { Text = "Ladder", Width = 148, Height = 32 };
    private readonly Button _bush = new() { Text = "Bush", Width = 148, Height = 32 };
    private readonly Button _counter = new() { Text = "Counter", Width = 148, Height = 32 };
    private readonly Button _damageFloor = new() { Text = "Damage Floor", Width = 148, Height = 32 };
    private readonly Button _terrainTag = new() { Text = "Terrain Tag", Width = 148, Height = 32 };
    private readonly Button _star = new() { Text = "Star", Width = 148, Height = 32 };
    private readonly Dictionary<TilesetFlagProperty, Button> _propertyButtons = [];
    private CancellationTokenSource? _imageLoad;
    private MvTilesetSnapshot? _snapshot;
    private int _recordId;
    private int? _copiedFlag;
    private bool _updatingPassage;
    private bool _loading;
    private bool _passageDirectionalMode;
    private TilesetPreviewPage _previewPage = TilesetPreviewPage.A;

    public TilesetEditorControl(MvProjectSession session, MutableJsonProjectDocument document, IDatabaseEditorDefinition definition)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        ArgumentNullException.ThrowIfNull(definition);
        _mutations = new Phase9EMutationService(session.Editing);
        Dock = DockStyle.Fill;

        _mode.Items.AddRange([new ModeChoice(MvTilesetMode.World, "World Type"), new ModeChoice(MvTilesetMode.Area, "Area Type")]);
        _tool.Items.AddRange(Enum.GetValues<TilesetCanvasTool>().Cast<object>().ToArray());
        _tool.SelectedItem = TilesetCanvasTool.Pencil;
        foreach (TilesetFlagProperty property in Enum.GetValues<TilesetFlagProperty>()) _paintProperty.Items.Add(property);
        _paintProperty.SelectedItem = TilesetFlagProperty.Passage;
        foreach (CheckBox check in _directions) _directionPanel.Controls.Add(check);
        foreach (TileSheetKind kind in Enum.GetValues<TileSheetKind>())
        {
            _sheetAssignments[kind] = new AssetPicker(session, AssetCategory.Tilesets)
            {
                Dock = DockStyle.Fill,
                AccessibleName = $"{kind} tileset assignment"
            };
        }

        _propertyButtons[TilesetFlagProperty.Passage] = _passage;
        _propertyButtons[TilesetFlagProperty.Ladder] = _ladder;
        _propertyButtons[TilesetFlagProperty.Bush] = _bush;
        _propertyButtons[TilesetFlagProperty.Counter] = _counter;
        _propertyButtons[TilesetFlagProperty.DamageFloor] = _damageFloor;
        _propertyButtons[TilesetFlagProperty.TerrainTag] = _terrainTag;
        _propertyButtons[TilesetFlagProperty.Star] = _star;

        BuildLayout();
        WireEvents();
        RebuildPaintValues();
        RefreshPreviewTabs();
        RefreshPropertyButtons();
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
            Enabled = records.Count == 1;
            if (records.Count != 1)
            {
                _snapshot = null;
                _recordId = 0;
                _name.Text = string.Empty;
                _note.Text = string.Empty;
                foreach (AssetPicker picker in _sheetAssignments.Values) picker.Value = string.Empty;
                _canvas.SetImage(null, []);
                _status.Text = records.Count == 0 ? "Select one tileset." : "Tileset editor edits one tileset at a time.";
                _previewCaption.Text = string.Empty;
                return;
            }

            _recordId = records[0].Id;
            _snapshot = MvPhase9EReader.ReadTileset(_document, _recordId);
            _name.Text = _snapshot.Name;
            _name.Tag = _snapshot.Name;
            ModeChoice? selectedMode = _mode.Items.Cast<ModeChoice>().FirstOrDefault(choice => choice.Value == _snapshot.Mode);
            _mode.SelectedItem = selectedMode ?? _mode.Items[0];
            _note.Text = _snapshot.Note;
            _note.Tag = _snapshot.Note;
            PopulateAssignments();
            _canvas.SetFlags(_snapshot.Flags);
            _status.Text = BuildStatus(_snapshot);
            UpdateInspector();
        }
        finally { _loading = false; }

        await LoadPreviewAsync(cancellationToken).ConfigureAwait(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _imageLoad?.Cancel();
            _imageLoad?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));

        root.Controls.Add(BuildLeftPanel(), 0, 0);
        root.Controls.Add(BuildCenterPanel(), 1, 0);
        root.Controls.Add(BuildRightPanel(), 2, 0);
        Controls.Add(root);
    }

    private Control BuildLeftPanel()
    {
        Panel panel = new() { Dock = DockStyle.Fill, AutoScroll = true };
        panel.Controls.Add(BuildImagesGroup());
        panel.Controls.Add(BuildGeneralGroup());
        return panel;
    }

    private Control BuildGeneralGroup()
    {
        GroupBox group = new() { Text = "General Settings", Dock = DockStyle.Top, Height = 120, Padding = new Padding(10) };
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        table.Controls.Add(new Label { Text = "Name:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        table.Controls.Add(_name, 1, 0);
        table.Controls.Add(new Label { Text = "Mode:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 1);
        table.Controls.Add(_mode, 1, 1);
        group.Controls.Add(table);
        return group;
    }

    private Control BuildImagesGroup()
    {
        GroupBox group = new() { Text = "Images", Dock = DockStyle.Top, Height = 510, Padding = new Padding(10) };
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = _sheetAssignments.Count };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        int row = 0;
        foreach (TileSheetKind kind in Enum.GetValues<TileSheetKind>())
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            table.Controls.Add(new Label { Text = GetSheetLabel(kind), Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
            table.Controls.Add(_sheetAssignments[kind], 1, row);
            row++;
        }
        group.Controls.Add(table);
        return group;
    }

    private Control BuildCenterPanel()
    {
        Panel panel = new() { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        FlowLayoutPanel tabStrip = new() { Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(4, 4, 4, 0) };
        tabStrip.Controls.AddRange([_tabA, _tabB, _tabC]);
        FlowLayoutPanel toolbar = new() { Dock = DockStyle.Top, Height = 34, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(4, 4, 4, 0) };
        toolbar.Controls.AddRange([
            new Label { Text = "Tool", AutoSize = true, Margin = new Padding(0, 8, 6, 0) }, _tool,
            new Label { Text = "Zoom", AutoSize = true, Margin = new Padding(18, 8, 6, 0) }
        ]);

        GroupBox group = new() { Text = "Tileset Flags", Dock = DockStyle.Fill, Padding = new Padding(0, 24, 0, 0) };
        group.Controls.Add(_canvas);
        group.Controls.Add(_zoom);
        group.Controls.Add(_inspector);
        group.Controls.Add(_status);
        group.Controls.Add(tabStrip);
        group.Controls.Add(_previewCaption);
        group.Controls.Add(toolbar);
        panel.Controls.Add(group);
        return panel;
    }

    private Control BuildRightPanel()
    {
        Panel panel = new() { Dock = DockStyle.Fill };
        panel.Controls.Add(BuildNoteGroup());
        panel.Controls.Add(BuildEditGroup());
        panel.Controls.Add(BuildShortcutButtons());
        return panel;
    }

    private Control BuildShortcutButtons()
    {
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Top,
            Height = 310,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10, 0, 0, 0)
        };
        buttons.Controls.AddRange([_passage, _passage4, _star, _ladder, _bush, _counter, _damageFloor, _terrainTag]);
        return buttons;
    }

    private Control BuildEditGroup()
    {
        GroupBox group = new() { Text = "Edit Selected Tile", Dock = DockStyle.Top, Height = 250, Padding = new Padding(10) };
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        table.Controls.Add(new Label { Text = "Property", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, 0);
        table.Controls.Add(_paintProperty, 0, 1);
        table.Controls.Add(new Label { Text = "Value", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, 2);
        Panel valueHost = new() { Dock = DockStyle.Fill, Height = 58 };
        valueHost.Controls.Add(_directionPanel);
        valueHost.Controls.Add(_rawValue);
        valueHost.Controls.Add(_paintValue);
        table.Controls.Add(valueHost, 0, 3);
        FlowLayoutPanel commands = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        commands.Controls.AddRange([_apply, _copyFlag, _pasteFlag]);
        table.Controls.Add(commands, 0, 5);
        FlowLayoutPanel options = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        options.Controls.AddRange([_paintOnDrag, _allShapes, _normalize]);
        table.Controls.Add(options, 0, 7);
        group.Controls.Add(table);
        return group;
    }

    private Control BuildNoteGroup()
    {
        GroupBox group = new() { Text = "Note", Dock = DockStyle.Fill, Padding = new Padding(10) };
        group.Controls.Add(_note);
        return group;
    }

    private void WireEvents()
    {
        _name.Validated += async (_, _) =>
        {
            if (_loading || _snapshot is null || string.Equals(_name.Tag as string, _name.Text, StringComparison.Ordinal)) return;
            await _mutations.SetTilesetIdentityAsync(_document.RelativePath, _recordId, _name.Text, _snapshot.Mode, _snapshot.Note).ConfigureAwait(true);
        };
        _mode.SelectedIndexChanged += async (_, _) =>
        {
            if (_loading || _snapshot is null || _mode.SelectedItem is not ModeChoice choice || choice.Value == _snapshot.Mode) return;
            await _mutations.SetTilesetIdentityAsync(_document.RelativePath, _recordId, _snapshot.Name, choice.Value, _snapshot.Note).ConfigureAwait(true);
        };
        _note.Validated += async (_, _) =>
        {
            if (_loading || _snapshot is null || string.Equals(_note.Tag as string, _note.Text, StringComparison.Ordinal)) return;
            await _mutations.SetTilesetIdentityAsync(_document.RelativePath, _recordId, _snapshot.Name, _snapshot.Mode, _note.Text).ConfigureAwait(true);
        };

        foreach ((TileSheetKind kind, AssetPicker picker) in _sheetAssignments)
        {
            picker.ValueChanged += async (_, _) =>
            {
                if (_loading || _snapshot is null) return;
                string previous = _snapshot.GetSheetName((int)kind);
                if (string.Equals(previous, picker.Value, StringComparison.Ordinal)) return;
                await _mutations.SetTilesetSheetAsync(_document.RelativePath, _recordId, (int)kind, picker.Value).ConfigureAwait(true);
                if (PreviewPageContains(kind)) await LoadPreviewAsync().ConfigureAwait(true);
            };
        }

        _tool.SelectedIndexChanged += (_, _) =>
        {
            _canvas.Tool = _tool.SelectedItem is TilesetCanvasTool tool ? tool : TilesetCanvasTool.Pencil;
            _paintOnDrag.Enabled = _canvas.Tool == TilesetCanvasTool.Pencil;
        };
        _paintProperty.SelectedIndexChanged += (_, _) => { RebuildPaintValues(); RefreshPropertyButtons(); };
        _paintValue.SelectedIndexChanged += (_, _) => ApplyPassagePreset();
        foreach (CheckBox direction in _directions) direction.CheckedChanged += (_, _) => UpdatePassageText();
        _apply.Click += async (_, _) => await PaintAsync([_canvas.SelectedTileId]).ConfigureAwait(true);
        _copyFlag.Click += (_, _) => CopySelectedFlag();
        _pasteFlag.Click += async (_, _) => await PasteSelectedFlagAsync().ConfigureAwait(true);
        _canvas.SelectionChanged += (_, _) => UpdateInspector();
        _canvas.PaintGestureCompleted += async (_, tileIds) =>
        {
            TilesetCanvasTool tool = _canvas.Tool;
            if (tool == TilesetCanvasTool.Eyedropper) { EyedropSelected(); return; }
            IReadOnlyList<int> targets = tool == TilesetCanvasTool.Fill && tileIds.Count > 0 ? FloodFill(tileIds[0]) : tileIds;
            if (tool != TilesetCanvasTool.Pencil || _paintOnDrag.Checked) await PaintAsync(targets).ConfigureAwait(true);
        };
        _zoom.ValueChanged += (_, _) => _canvas.Zoom = _zoom.Value / 100f;
        _normalize.Click += async (_, _) =>
        {
            if (_snapshot is null || _snapshot.Flags.Count == MvPhase9EConstants.TileFlagCount) return;
            DialogResult result = MessageBox.Show(this,
                $"This explicitly replaces the stored {_snapshot.Flags.Count:N0}-entry array with exactly {MvPhase9EConstants.TileFlagCount:N0} entries. Existing standard entries are retained; extension tail values are discarded. Continue?",
                "Normalize tileset flags", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
            await _mutations.NormalizeTilesetFlagsAsync(_document.RelativePath, _recordId, _snapshot.Flags).ConfigureAwait(true);
        };

        _tabA.Click += async (_, _) => await SwitchPreviewPageAsync(TilesetPreviewPage.A).ConfigureAwait(true);
        _tabB.Click += async (_, _) => await SwitchPreviewPageAsync(TilesetPreviewPage.B).ConfigureAwait(true);
        _tabC.Click += async (_, _) => await SwitchPreviewPageAsync(TilesetPreviewPage.C).ConfigureAwait(true);

        _passage.Click += (_, _) => SelectShortcut(TilesetFlagProperty.Passage, false);
        _passage4.Click += (_, _) => SelectShortcut(TilesetFlagProperty.Passage, true);
        _star.Click += (_, _) => SelectShortcut(TilesetFlagProperty.Star, false);
        _ladder.Click += (_, _) => SelectShortcut(TilesetFlagProperty.Ladder, false);
        _bush.Click += (_, _) => SelectShortcut(TilesetFlagProperty.Bush, false);
        _counter.Click += (_, _) => SelectShortcut(TilesetFlagProperty.Counter, false);
        _damageFloor.Click += (_, _) => SelectShortcut(TilesetFlagProperty.DamageFloor, false);
        _terrainTag.Click += (_, _) => SelectShortcut(TilesetFlagProperty.TerrainTag, false);
    }

    private void SelectShortcut(TilesetFlagProperty property, bool directionalPassage)
    {
        _passageDirectionalMode = directionalPassage && property == TilesetFlagProperty.Passage;
        _paintProperty.SelectedItem = property;
        RebuildPaintValues();
        RefreshPropertyButtons();
    }

    private async ValueTask SwitchPreviewPageAsync(TilesetPreviewPage page)
    {
        if (_previewPage == page) return;
        _previewPage = page;
        RefreshPreviewTabs();
        await LoadPreviewAsync().ConfigureAwait(true);
    }

    private void PopulateAssignments()
    {
        if (_snapshot is null) return;
        foreach (TileSheetKind kind in Enum.GetValues<TileSheetKind>()) _sheetAssignments[kind].Value = _snapshot.GetSheetName((int)kind);
    }

    private async ValueTask LoadPreviewAsync(CancellationToken cancellationToken = default)
    {
        _imageLoad?.Cancel();
        _imageLoad?.Dispose();
        _imageLoad = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = _imageLoad.Token;
        if (_snapshot is null)
        {
            _canvas.SetImage(null, []);
            _previewCaption.Text = string.Empty;
            return;
        }

        List<(TileSheetKind Kind, Bitmap Image, IReadOnlyList<TilesetPaletteCell> Cells, string Name)> loaded = [];
        List<string> warnings = [];
        try
        {
            foreach (TileSheetKind kind in GetPreviewKinds(_previewPage))
            {
                string name = GetAssignedSheetName(kind);
                if (string.IsNullOrWhiteSpace(name)) continue;
                AssetRecord? record = _session.Assets.Find(new AssetKey(AssetCategory.Tilesets, name));
                AssetVariant? variant = record?.PreferredVariant;
                if (variant is null)
                {
                    warnings.Add($"{kind} missing");
                    continue;
                }
                if (variant.IsEncrypted)
                {
                    warnings.Add($"{kind} encrypted");
                    continue;
                }
                try
                {
                    Bitmap image = await AssetImageLoader.LoadOwnedBitmapAsync(_session.AssetContent, variant.RelativePath, token).ConfigureAwait(true);
                    token.ThrowIfCancellationRequested();
                    IReadOnlyList<TilesetPaletteCell> paletteCells = TilesetPaletteCellBuilder.Build(kind, image.Width, image.Height);
                    loaded.Add((kind, image, paletteCells, name));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
                {
                    warnings.Add($"{kind}: {exception.Message}");
                }
            }

            if (loaded.Count == 0)
            {
                _canvas.SetImage(null, []);
                _previewCaption.Text = PreviewPageCaption();
                _status.Text = warnings.Count == 0
                    ? $"{PreviewPageCaption()} has no assigned preview images."
                    : string.Join("  •  ", warnings);
                return;
            }

            Bitmap composite = ComposePreviewBitmap(loaded, out List<TilesetPaletteCell> cells);
            foreach ((_, Bitmap image, _, _) in loaded) image.Dispose();
            _canvas.SetImage(composite, cells);
            _canvas.SetFlags(_snapshot.Flags);
            _previewCaption.Text = PreviewPageCaption();
            _status.Text = BuildPreviewStatus(loaded, warnings);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            foreach ((_, Bitmap image, _, _) in loaded) image.Dispose();
        }
    }

    private static Bitmap ComposePreviewBitmap(IReadOnlyList<(TileSheetKind Kind, Bitmap Image, IReadOnlyList<TilesetPaletteCell> Cells, string Name)> loaded, out List<TilesetPaletteCell> cells)
    {
        const int gap = 6;
        int width = loaded.Max(item => item.Image.Width);
        int height = loaded.Sum(item => item.Image.Height) + Math.Max(0, loaded.Count - 1) * gap;
        Bitmap composite = new(Math.Max(1, width), Math.Max(1, height));
        cells = [];
        using Graphics graphics = Graphics.FromImage(composite);
        graphics.Clear(Color.Black);
        int y = 0;
        foreach ((TileSheetKind kind, Bitmap image, IReadOnlyList<TilesetPaletteCell> sourceCells, _) in loaded)
        {
            graphics.DrawImage(image, new Rectangle(0, y, image.Width, image.Height));
            foreach (TilesetPaletteCell cell in sourceCells)
            {
                Rectangle shifted = new(cell.Source.X, cell.Source.Y + y, cell.Source.Width, cell.Source.Height);
                cells.Add(new TilesetPaletteCell(shifted, cell.TileId, kind.ToString()));
            }
            y += image.Height + gap;
        }
        return composite;
    }

    private string GetAssignedSheetName(TileSheetKind kind) => _sheetAssignments.TryGetValue(kind, out AssetPicker? picker) ? picker.Value : _snapshot?.GetSheetName((int)kind) ?? string.Empty;

    private bool PreviewPageContains(TileSheetKind kind) => GetPreviewKinds(_previewPage).Contains(kind);

    private static IReadOnlyList<TileSheetKind> GetPreviewKinds(TilesetPreviewPage page) => page switch
    {
        TilesetPreviewPage.A => [TileSheetKind.A1, TileSheetKind.A2, TileSheetKind.A3, TileSheetKind.A4, TileSheetKind.A5],
        TilesetPreviewPage.B => [TileSheetKind.B],
        TilesetPreviewPage.C => [TileSheetKind.C, TileSheetKind.D, TileSheetKind.E],
        _ => []
    };

    private string PreviewPageCaption() => _previewPage switch
    {
        TilesetPreviewPage.A => "A page — stacked A1 through A5 preview",
        TilesetPreviewPage.B => "B page — B tileset preview",
        TilesetPreviewPage.C => "C page — stacked C through E preview",
        _ => string.Empty
    };

    private string BuildPreviewStatus(IReadOnlyList<(TileSheetKind Kind, Bitmap Image, IReadOnlyList<TilesetPaletteCell> Cells, string Name)> loaded, IReadOnlyList<string> warnings)
    {
        int logicalTiles = loaded.Sum(item => item.Cells.Count);
        string loadedText = string.Join(", ", loaded.Select(item => $"{item.Kind}:{item.Name}"));
        return warnings.Count == 0
            ? $"{PreviewPageCaption()} — {logicalTiles:N0} logical tile(s) loaded from {loadedText}."
            : $"{PreviewPageCaption()} — {logicalTiles:N0} logical tile(s) loaded from {loadedText}.  Missing/invalid: {string.Join("; ", warnings)}";
    }

    private void RefreshPreviewTabs()
    {
        StyleTab(_tabA, _previewPage == TilesetPreviewPage.A);
        StyleTab(_tabB, _previewPage == TilesetPreviewPage.B);
        StyleTab(_tabC, _previewPage == TilesetPreviewPage.C);
    }

    private void RefreshPropertyButtons()
    {
        TilesetFlagProperty property = SelectedProperty;
        foreach ((TilesetFlagProperty key, Button button) in _propertyButtons) StyleShortcut(button, key == property);
        StyleShortcut(_passage4, property == TilesetFlagProperty.Passage && _passageDirectionalMode);
        StyleShortcut(_passage, property == TilesetFlagProperty.Passage && !_passageDirectionalMode);
    }

    private static void StyleTab(Button button, bool active)
    {
        button.FlatStyle = FlatStyle.System;
        button.Font = active ? new Font(button.Font, FontStyle.Bold) : new Font(button.Font, FontStyle.Regular);
    }

    private static void StyleShortcut(Button button, bool active)
    {
        button.FlatStyle = FlatStyle.System;
        button.Font = active ? new Font(button.Font, FontStyle.Bold) : new Font(button.Font, FontStyle.Regular);
    }

    private static string GetSheetLabel(TileSheetKind kind) => kind switch
    {
        TileSheetKind.A1 => "A1 (Animation):",
        TileSheetKind.A2 => "A2 (Ground):",
        TileSheetKind.A3 => "A3 (Buildings):",
        TileSheetKind.A4 => "A4 (Walls):",
        TileSheetKind.A5 => "A5 (Normal):",
        TileSheetKind.B => "B:",
        TileSheetKind.C => "C:",
        TileSheetKind.D => "D:",
        TileSheetKind.E => "E:",
        _ => kind + ":"
    };

    private void RebuildPaintValues()
    {
        _paintValue.BeginUpdate();
        _paintValue.Items.Clear();
        TilesetFlagProperty property = SelectedProperty;
        _rawValue.Visible = property == TilesetFlagProperty.RawValue;
        _paintValue.Visible = !_rawValue.Visible;
        _directionPanel.Visible = property == TilesetFlagProperty.Passage && _passageDirectionalMode;
        if (property == TilesetFlagProperty.Passage)
        {
            _paintValue.Items.AddRange([
                new PaintValue(0, "O — passable"), new PaintValue(15, "X — blocked"),
                new PaintValue(1, "Block down"), new PaintValue(2, "Block left"), new PaintValue(4, "Block right"), new PaintValue(8, "Block up")
            ]);
        }
        else if (property == TilesetFlagProperty.TerrainTag)
        {
            for (int value = 0; value <= 15; value++) _paintValue.Items.Add(new PaintValue(value, value.ToString()));
        }
        else if (property != TilesetFlagProperty.RawValue)
        {
            _paintValue.Items.AddRange([new PaintValue(0, "Off"), new PaintValue(1, "On")]);
        }
        if (_paintValue.Items.Count > 0) _paintValue.SelectedIndex = 0;
        _paintValue.EndUpdate();
        ApplyPassagePreset();
    }

    private TilesetFlagProperty SelectedProperty => _paintProperty.SelectedItem is TilesetFlagProperty selected ? selected : TilesetFlagProperty.Passage;

    private int SelectedPaintValue
    {
        get
        {
            if (SelectedProperty == TilesetFlagProperty.RawValue) return decimal.ToInt32(_rawValue.Value);
            if (SelectedProperty != TilesetFlagProperty.Passage || !_passageDirectionalMode) return (_paintValue.SelectedItem as PaintValue)?.Value ?? 0;
            int value = 0;
            if (_directions[0].Checked) value |= (int)MvPassageDirection.Down;
            if (_directions[1].Checked) value |= (int)MvPassageDirection.Left;
            if (_directions[2].Checked) value |= (int)MvPassageDirection.Right;
            if (_directions[3].Checked) value |= (int)MvPassageDirection.Up;
            return value;
        }
    }

    private void ApplyPassagePreset()
    {
        if (_updatingPassage || SelectedProperty != TilesetFlagProperty.Passage || _paintValue.SelectedItem is not PaintValue value) return;
        _updatingPassage = true;
        try
        {
            _directions[0].Checked = (value.Value & (int)MvPassageDirection.Down) != 0;
            _directions[1].Checked = (value.Value & (int)MvPassageDirection.Left) != 0;
            _directions[2].Checked = (value.Value & (int)MvPassageDirection.Right) != 0;
            _directions[3].Checked = (value.Value & (int)MvPassageDirection.Up) != 0;
        }
        finally { _updatingPassage = false; }
    }

    private void UpdatePassageText()
    {
        if (_updatingPassage || SelectedProperty != TilesetFlagProperty.Passage || !_passageDirectionalMode) return;
        int value = SelectedPaintValue;
        PaintValue? match = _paintValue.Items.Cast<PaintValue>().FirstOrDefault(item => item.Value == value);
        _updatingPassage = true;
        try { _paintValue.SelectedItem = match; }
        finally { _updatingPassage = false; }
    }

    private void CopySelectedFlag()
    {
        if (_snapshot is null || _canvas.SelectedTileId < 0) return;
        _copiedFlag = _snapshot.GetFlag(_canvas.SelectedTileId).RawValue;
        _pasteFlag.Enabled = true;
        _status.Text = $"Copied raw flag 0x{_copiedFlag.Value:X8} from tile {_canvas.SelectedTileId}.";
    }

    private async ValueTask PasteSelectedFlagAsync()
    {
        if (!_copiedFlag.HasValue || _canvas.SelectedTileId < 0) return;
        await PaintWithEditAsync([_canvas.SelectedTileId], new TilesetFlagEdit(TilesetFlagProperty.RawValue, _copiedFlag.Value)).ConfigureAwait(true);
    }

    private void EyedropSelected()
    {
        if (_snapshot is null || _canvas.SelectedTileId < 0) return;
        MvTilesetFlag flag = _snapshot.GetFlag(_canvas.SelectedTileId);
        int value = GetPropertyValue(flag.RawValue, SelectedProperty);
        if (SelectedProperty == TilesetFlagProperty.RawValue) _rawValue.Value = Math.Clamp(value, decimal.ToInt32(_rawValue.Minimum), decimal.ToInt32(_rawValue.Maximum));
        else if (SelectedProperty == TilesetFlagProperty.Passage && _passageDirectionalMode)
        {
            _updatingPassage = true;
            try
            {
                _directions[0].Checked = (value & (int)MvPassageDirection.Down) != 0;
                _directions[1].Checked = (value & (int)MvPassageDirection.Left) != 0;
                _directions[2].Checked = (value & (int)MvPassageDirection.Right) != 0;
                _directions[3].Checked = (value & (int)MvPassageDirection.Up) != 0;
                _paintValue.SelectedItem = _paintValue.Items.Cast<PaintValue>().FirstOrDefault(item => item.Value == value);
            }
            finally { _updatingPassage = false; }
        }
        else _paintValue.SelectedItem = _paintValue.Items.Cast<PaintValue>().FirstOrDefault(item => item.Value == value);
        _status.Text = $"Eyedropper sampled {SelectedProperty} value {value} from tile {_canvas.SelectedTileId}.";
    }

    private IReadOnlyList<int> FloodFill(int seedTileId)
    {
        if (_snapshot is null) return [];
        TilesetPaletteCell? seed = _canvas.Cells.FirstOrDefault(cell => cell.TileId == seedTileId);
        if (seed is null) return [];
        int sought = GetPropertyValue(_snapshot.GetFlag(seedTileId).RawValue, SelectedProperty);
        TilesetPaletteCell[] eligible = _canvas.Cells.Where(cell => GetPropertyValue(_snapshot.GetFlag(cell.TileId).RawValue, SelectedProperty) == sought).ToArray();
        HashSet<int> result = [seedTileId];
        Queue<TilesetPaletteCell> queue = new();
        queue.Enqueue(seed);
        while (queue.Count > 0)
        {
            TilesetPaletteCell current = queue.Dequeue();
            foreach (TilesetPaletteCell candidate in eligible)
            {
                if (result.Contains(candidate.TileId) || !Touches(current.Source, candidate.Source)) continue;
                result.Add(candidate.TileId);
                queue.Enqueue(candidate);
            }
        }
        return result.OrderBy(value => value).ToArray();
    }

    private static bool Touches(Rectangle first, Rectangle second)
    {
        bool verticalOverlap = first.Top < second.Bottom && first.Bottom > second.Top;
        bool horizontalOverlap = first.Left < second.Right && first.Right > second.Left;
        return verticalOverlap && (first.Right == second.Left || second.Right == first.Left)
            || horizontalOverlap && (first.Bottom == second.Top || second.Bottom == first.Top);
    }

    private static int GetPropertyValue(int raw, TilesetFlagProperty property)
    {
        MvTilesetFlag flag = new(raw);
        return property switch
        {
            TilesetFlagProperty.Passage => (int)flag.BlockedDirections,
            TilesetFlagProperty.Star => flag.Star ? 1 : 0,
            TilesetFlagProperty.Ladder => flag.Ladder ? 1 : 0,
            TilesetFlagProperty.Bush => flag.Bush ? 1 : 0,
            TilesetFlagProperty.Counter => flag.Counter ? 1 : 0,
            TilesetFlagProperty.DamageFloor => flag.DamageFloor ? 1 : 0,
            TilesetFlagProperty.BoatBlocked => flag.BoatBlocked ? 1 : 0,
            TilesetFlagProperty.ShipBlocked => flag.ShipBlocked ? 1 : 0,
            TilesetFlagProperty.AirshipLandingBlocked => flag.AirshipLandingBlocked ? 1 : 0,
            TilesetFlagProperty.TerrainTag => flag.TerrainTag,
            TilesetFlagProperty.RawValue => raw,
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
    }

    private ValueTask PaintAsync(IEnumerable<int> tileIds) => PaintWithEditAsync(tileIds, new TilesetFlagEdit(SelectedProperty, SelectedPaintValue));

    private async ValueTask PaintWithEditAsync(IEnumerable<int> tileIds, TilesetFlagEdit edit)
    {
        if (_snapshot is null) return;
        int[] valid = tileIds.Where(id => id >= 0).Distinct().ToArray();
        if (valid.Length == 0) return;
        try
        {
            await _mutations.PaintTilesetFlagsAsync(_document.RelativePath, _recordId, _snapshot.Flags, valid, edit, _allShapes.Checked).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Tileset flag edit failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateInspector()
    {
        if (_snapshot is null || _canvas.SelectedTileId < 0)
        {
            _inspector.Text = "No tile selected.";
            return;
        }
        MvTilesetFlag flag = _snapshot.GetFlag(_canvas.SelectedTileId);
        _rawValue.Value = Math.Clamp(flag.RawValue, decimal.ToInt32(_rawValue.Minimum), decimal.ToInt32(_rawValue.Maximum));
        _inspector.Text = $"Tile {_canvas.SelectedTileId}  Raw 0x{flag.RawValue:X8}  Passage {flag.BlockedDirections}  Star {Yes(flag.Star)}  Ladder {Yes(flag.Ladder)}  Bush {Yes(flag.Bush)}  Counter {Yes(flag.Counter)}  Damage {Yes(flag.DamageFloor)}  Boat/Ship/Air {Yes(flag.BoatBlocked)}/{Yes(flag.ShipBlocked)}/{Yes(flag.AirshipLandingBlocked)}  Terrain {flag.TerrainTag}  Unknown 0x{flag.UnknownBits:X}";
    }

    private static string BuildStatus(MvTilesetSnapshot snapshot) => snapshot.Flags.Count switch
    {
        MvPhase9EConstants.TileFlagCount => $"Standard {MvPhase9EConstants.TileFlagCount:N0}-entry flag array. Unrelated and unknown bits are mask-preserved.",
        < MvPhase9EConstants.TileFlagCount => $"Warning: only {snapshot.Flags.Count:N0} flags are stored. Painting outside the existing array is blocked until explicit normalization.",
        _ => $"Extension array: {snapshot.Flags.Count:N0} flags. The {snapshot.Flags.Count - MvPhase9EConstants.TileFlagCount:N0}-entry tail is preserved unless normalization is explicitly requested."
    };
    private static string Yes(bool value) => value ? "Y" : "N";
    private sealed record ModeChoice(MvTilesetMode Value, string Text) { public override string ToString() => Text; }
    private sealed record PaintValue(int Value, string Text) { public override string ToString() => Text; }
}

internal enum TilesetPreviewPage { A, B, C }

public sealed record TilesetPaletteCell(Rectangle Source, int TileId, string Sheet);

public static class TilesetPaletteCellBuilder
{
    public static IReadOnlyList<TilesetPaletteCell> Build(TileSheetKind sheet, int width, int height)
    {
        List<TilesetPaletteCell> cells = [];
        switch (sheet)
        {
            case TileSheetKind.B:
            case TileSheetKind.C:
            case TileSheetKind.D:
            case TileSheetKind.E:
            case TileSheetKind.A5: BuildNormal(sheet, width, height, cells); break;
            case TileSheetKind.A1: BuildA1(width, height, cells); break;
            case TileSheetKind.A2: BuildAutotiles(sheet, width, height, 96, 144, MvTileRules.TileIdA2, cells); break;
            case TileSheetKind.A3: BuildAutotiles(sheet, width, height, 96, 96, MvTileRules.TileIdA3, cells); break;
            case TileSheetKind.A4: BuildA4(width, height, cells); break;
        }
        return cells;
    }

    private static void BuildNormal(TileSheetKind sheet, int width, int height, ICollection<TilesetPaletteCell> cells)
    {
        int baseId = sheet switch
        {
            TileSheetKind.B => MvTileRules.TileIdB, TileSheetKind.C => MvTileRules.TileIdC,
            TileSheetKind.D => MvTileRules.TileIdD, TileSheetKind.E => MvTileRules.TileIdE,
            TileSheetKind.A5 => MvTileRules.TileIdA5, _ => throw new ArgumentOutOfRangeException(nameof(sheet))
        };
        int columns = Math.Min(16, width / 48);
        int rows = height / 48;
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                int localId = checked((column / 8) * 128 + row * 8 + column % 8);
                cells.Add(new TilesetPaletteCell(new Rectangle(column * 48, row * 48, 48, 48), checked(baseId + localId), sheet.ToString()));
            }
    }

    private static void BuildAutotiles(TileSheetKind sheet, int width, int height, int cellWidth, int cellHeight, int firstTileId, ICollection<TilesetPaletteCell> cells)
    {
        int firstKind = MvTileRules.GetAutotileKind(firstTileId);
        for (int row = 0; row < height / cellHeight; row++)
            for (int column = 0; column < width / cellWidth; column++)
            {
                int kind = checked(firstKind + row * 8 + column);
                cells.Add(new TilesetPaletteCell(new Rectangle(column * cellWidth, row * cellHeight, cellWidth, cellHeight), MvTileRules.MakeAutotileId(kind, 0), sheet.ToString()));
            }
    }

    private static void BuildA1(int width, int height, ICollection<TilesetPaletteCell> cells)
    {
        for (int kind = 0; kind < 16; kind++)
        {
            int tx = kind % 8; int ty = kind / 8; int bx; int by; bool waterfall = false;
            if (kind == 0) { bx = 0; by = 0; }
            else if (kind == 1) { bx = 0; by = 3; }
            else if (kind == 2) { bx = 6; by = 0; }
            else if (kind == 3) { bx = 6; by = 3; }
            else { bx = tx / 4 * 8; by = ty * 6 + tx / 2 % 2 * 3; if (kind % 2 == 1) { bx += 6; waterfall = true; } }
            int x = bx * 48; int y = by * 48;
            if (x >= width || y >= height) continue;
            cells.Add(new TilesetPaletteCell(new Rectangle(x, y, Math.Min(96, width - x), Math.Min(waterfall ? 96 : 144, height - y)), MvTileRules.MakeAutotileId(kind, 0), "A1"));
        }
    }

    private static void BuildA4(int width, int height, ICollection<TilesetPaletteCell> cells)
    {
        int firstKind = MvTileRules.GetAutotileKind(MvTileRules.TileIdA4);
        for (int row = 0; row < 6; row++)
        {
            int ty = 10 + row;
            int by = (int)Math.Floor((ty - 10) * 2.5 + (ty % 2 == 1 ? 0.5 : 0));
            int cellHeight = ty % 2 == 1 ? 96 : 144;
            for (int column = 0; column < 8; column++)
            {
                int x = column * 96; int y = by * 48;
                if (x >= width || y >= height) continue;
                int kind = checked(firstKind + row * 8 + column);
                cells.Add(new TilesetPaletteCell(new Rectangle(x, y, Math.Min(96, width - x), Math.Min(cellHeight, height - y)), MvTileRules.MakeAutotileId(kind, 0), "A4"));
            }
        }
    }
}

public enum TilesetCanvasTool { Pencil, Rectangle, Fill, Eyedropper }

public sealed class TilesetFlagCanvas : ScrollableControl
{
    private Image? _image;
    private IReadOnlyList<TilesetPaletteCell> _cells = [];
    private IReadOnlyList<int> _flags = [];
    private float _zoom = 0.5f;
    private int _selectedTileId = -1;
    private readonly HashSet<int> _gesture = [];
    private TilesetPaletteCell? _gestureStart;
    private bool _dragging;

    public TilesetFlagCanvas()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(38, 38, 42);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler<IReadOnlyList<int>>? PaintGestureCompleted;
    public float Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 0.1f, 8f);
            UpdateScrollArea();
            Invalidate();
        }
    }
    public TilesetCanvasTool Tool { get; set; } = TilesetCanvasTool.Pencil;
    public int SelectedTileId => _selectedTileId;
    public IReadOnlyList<TilesetPaletteCell> Cells => _cells;

    public void SetImage(Image? image, IReadOnlyList<TilesetPaletteCell> cells)
    {
        Image? old = _image;
        _image = image;
        _cells = cells ?? [];
        _selectedTileId = _cells.Count == 0 ? -1 : _cells[0].TileId;
        old?.Dispose();
        UpdateScrollArea();
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFlags(IReadOnlyList<int> flags) { _flags = flags ?? []; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image is null)
        {
            TextRenderer.DrawText(e.Graphics, "No tileset image", Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        (Rectangle destination, float scale) = Destination();
        e.Graphics.InterpolationMode = scale >= 1f ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor : System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        e.Graphics.DrawImage(_image, destination);
        using Pen grid = new(Color.FromArgb(120, Color.DeepSkyBlue), 1f);
        using Pen selected = new(Color.Gold, 3f);
        using SolidBrush dark = new(Color.FromArgb(185, 0, 0, 0));
        using SolidBrush light = new(Color.White);
        foreach (TilesetPaletteCell cell in _cells)
        {
            RectangleF rect = Scale(cell.Source, destination.Location, scale);
            e.Graphics.DrawRectangle(cell.TileId == _selectedTileId ? selected : grid, rect.X, rect.Y, rect.Width, rect.Height);
            int raw = cell.TileId >= 0 && cell.TileId < _flags.Count ? _flags[cell.TileId] : 0;
            MvTilesetFlag flag = new(raw);
            string mark = flag.Star ? "★" : flag.BlockedDirections == MvPassageDirection.All ? "×" : flag.BlockedDirections == MvPassageDirection.None ? "○" : DirectionMark(flag.BlockedDirections);
            Rectangle labelRect = Rectangle.Round(new RectangleF(rect.X + 2, rect.Y + 2, Math.Min(34, rect.Width - 4), Math.Min(22, rect.Height - 4)));
            e.Graphics.FillRectangle(dark, labelRect);
            TextRenderer.DrawText(e.Graphics, mark, Font, labelRect, light.Color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (flag.TerrainTag > 0)
            {
                Rectangle tagRect = new((int)Math.Max(rect.Left, rect.Right - 22), (int)Math.Max(rect.Top, rect.Bottom - 20), 20, 18);
                e.Graphics.FillRectangle(dark, tagRect);
                TextRenderer.DrawText(e.Graphics, flag.TerrainTag.ToString(), Font, tagRect, light.Color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _gesture.Clear();
        _gestureStart = SelectAt(e.Location, addToGesture: true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging && Tool == TilesetCanvasTool.Pencil && (e.Button & MouseButtons.Left) != 0) SelectAt(e.Location, addToGesture: true);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging || e.Button != MouseButtons.Left) return;
        _dragging = false;
        TilesetPaletteCell? end = SelectAt(e.Location, addToGesture: Tool != TilesetCanvasTool.Rectangle);
        if (Tool == TilesetCanvasTool.Rectangle && _gestureStart is not null && end is not null)
        {
            Rectangle sourceSelection = Rectangle.FromLTRB(
                Math.Min(_gestureStart.Source.Left, end.Source.Left),
                Math.Min(_gestureStart.Source.Top, end.Source.Top),
                Math.Max(_gestureStart.Source.Right, end.Source.Right),
                Math.Max(_gestureStart.Source.Bottom, end.Source.Bottom));
            foreach (TilesetPaletteCell cell in _cells.Where(cell => sourceSelection.IntersectsWith(cell.Source))) _gesture.Add(cell.TileId);
        }
        _gestureStart = null;
        if (_gesture.Count > 0) PaintGestureCompleted?.Invoke(this, _gesture.OrderBy(value => value).ToArray());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _image?.Dispose(); _image = null; }
        base.Dispose(disposing);
    }

    private TilesetPaletteCell? SelectAt(Point point, bool addToGesture)
    {
        TilesetPaletteCell? cell = HitTest(point);
        if (cell is null) return null;
        if (_selectedTileId != cell.TileId)
        {
            _selectedTileId = cell.TileId;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
        if (addToGesture) _gesture.Add(cell.TileId);
        return cell;
    }

    private TilesetPaletteCell? HitTest(Point point)
    {
        if (_image is null) return null;
        (Rectangle destination, float scale) = Destination();
        float x = (point.X - destination.Left) / scale;
        float y = (point.Y - destination.Top) / scale;
        return _cells.FirstOrDefault(cell => cell.Source.Contains((int)x, (int)y));
    }

    private (Rectangle Destination, float Scale) Destination()
    {
        if (_image is null) return (Rectangle.Empty, 1f);
        float scale = _zoom;
        int width = Math.Max(1, (int)Math.Round(_image.Width * scale));
        int height = Math.Max(1, (int)Math.Round(_image.Height * scale));
        Point scroll = AutoScrollPosition;
        int x = width + 16 <= ClientSize.Width ? (ClientSize.Width - width) / 2 : scroll.X + 8;
        int y = height + 16 <= ClientSize.Height ? (ClientSize.Height - height) / 2 : scroll.Y + 8;
        return (new Rectangle(x, y, width, height), scale);
    }

    private void UpdateScrollArea()
    {
        if (_image is null)
        {
            AutoScrollMinSize = Size.Empty;
            return;
        }
        AutoScrollMinSize = new Size(
            Math.Max(1, (int)Math.Ceiling(_image.Width * _zoom)) + 16,
            Math.Max(1, (int)Math.Ceiling(_image.Height * _zoom)) + 16);
    }

    private static RectangleF Scale(Rectangle source, Point origin, float scale) => new(origin.X + source.X * scale, origin.Y + source.Y * scale, source.Width * scale, source.Height * scale);
    private static string DirectionMark(MvPassageDirection directions)
    {
        string result = string.Empty;
        if ((directions & MvPassageDirection.Up) != 0) result += "↑";
        if ((directions & MvPassageDirection.Right) != 0) result += "→";
        if ((directions & MvPassageDirection.Down) != 0) result += "↓";
        if ((directions & MvPassageDirection.Left) != 0) result += "←";
        return result;
    }
}
