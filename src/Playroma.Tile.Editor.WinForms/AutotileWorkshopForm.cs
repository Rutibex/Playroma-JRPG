using System.Drawing.Imaging;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Rendering;
using Playroma.Tile.Autotiles.Services;
using Playroma.Tile.Autotiles.Validation;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Editor.WinForms.Controls;
using Playroma.Tile.Editor.WinForms.Services;
using Playroma.Tile.Materials.Integration;

namespace Playroma.Tile.Editor.WinForms;

public sealed class AutotileWorkshopForm : Form
{
    private readonly EditorWorkspace _workspace;
    private readonly SheetSession? _targetSession;
    private readonly TileSetDefinition? _targetTileSet;
    private readonly TileSheetDefinition? _targetSheet;
    private readonly int? _targetRegionIndex;
    private readonly MvAutotileCompiler _compiler = new();
    private readonly MvAutotileInverseImporter _inverseImporter = new();
    private readonly AutotileValidator _validator = new();
    private readonly AutotileMaterialComposer _materialComposer = new();
    private readonly AutotileTortureTestRenderer _tortureRenderer = new();
    private readonly ListBox _assetList = new();
    private readonly AutotileSourceCanvas _sourceCanvas = new();
    private readonly AutotileShapeGallery _gallery = new();
    private readonly AutotileTestMapCanvas _testMap = new();
    private readonly PictureBox _torturePicture = new();
    private readonly ListView _issues = new();
    private readonly TextBox _nameBox = new();
    private readonly TextBox _descriptionBox = new();
    private readonly ComboBox _kindCombo = new();
    private readonly NumericUpDown _frameBox = new();
    private readonly ComboBox _a2ModeCombo = new();
    private readonly CheckBox _counterCheck = new();
    private readonly CheckBox _rotationCheck = new();
    private readonly CheckBox _mirroringCheck = new();
    private readonly CheckBox _overlayCheck = new();
    private readonly CheckBox _fixedPatternCheck = new();
    private readonly Label _sourceInfo = new();
    private readonly Label _quarterInfo = new();
    private readonly Label _targetInfo = new();
    private readonly Label _diagnostic = new();
    private readonly Panel _colorSwatch = new();
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 350 };
    private readonly LinkedList<Bitmap> _undo = new();
    private readonly LinkedList<Bitmap> _redo = new();
    private AutotileAssetReference? _currentReference;
    private AutotileDefinition? _definition;
    private Bitmap? _source;
    private Bitmap? _quarterClipboard;
    private AutotileQuarterSelection? _selectedQuarter;
    private bool _dirty;
    private bool _loading;

    public AutotileWorkshopForm(
        EditorWorkspace workspace,
        SheetSession? targetSession,
        TileSetDefinition? targetTileSet,
        TileSheetDefinition? targetSheet,
        int? targetRegionIndex)
    {
        _workspace = workspace;
        _targetSession = targetSession;
        _targetTileSet = targetTileSet;
        _targetSheet = targetSheet;
        _targetRegionIndex = targetRegionIndex;

        Text = "Playroma Tile — Autotile Compiler Workshop";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1500;
        Height = 940;
        MinimumSize = new Size(1120, 720);

        Controls.Add(BuildLayout());
        FormClosing += OnClosing;
        _animationTimer.Tick += (_, _) => AdvanceFrame();
        RefreshAssetList();
        UpdateTargetInfo();
        if (_assetList.Items.Count > 0) _assetList.SelectedIndex = 0;
        UpdateUi();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Dispose();
            _source?.Dispose();
            _quarterClipboard?.Dispose();
            DisposeImage(_torturePicture);
            ClearHistory(_undo);
            ClearHistory(_redo);
        }
        base.Dispose(disposing);
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildToolbar(), 0, 0);

        var main = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1400, 760), SplitterDistance = 250, Panel1MinSize = 210, Panel2MinSize = 700 };
        main.Panel1.Controls.Add(BuildAssetPanel());
        var editor = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1400, 760), SplitterDistance = 1030, Panel1MinSize = 650, Panel2MinSize = 260 };
        editor.Panel1.Controls.Add(BuildTabs());
        editor.Panel2.Controls.Add(BuildPropertiesPanel());
        main.Panel2.Controls.Add(editor);
        root.Controls.Add(main, 0, 1);

        _diagnostic.AutoSize = true;
        _diagnostic.Padding = new Padding(4);
        _diagnostic.Text = "Ready.";
        root.Controls.Add(_diagnostic, 0, 2);
        return root;
    }

    private ToolStrip BuildToolbar()
    {
        var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Fill };
        bar.Items.Add(Button("New", (_, _) => NewAutotile()));
        bar.Items.Add(Button("Extract Selected MV Region", (_, _) => ExtractTargetRegion()));
        bar.Items.Add(Button("Save Definition", (_, _) => SaveCurrent()));
        bar.Items.Add(Button("Compile to Selected Region", (_, _) => CompileToTarget()));
        bar.Items.Add(Button("Recompile All Outdated", (_, _) => RecompileAllOutdated()));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(Button("Undo Pixels", (_, _) => UndoPixels()));
        bar.Items.Add(Button("Redo Pixels", (_, _) => RedoPixels()));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(Button("Build from Material…", (_, _) => BuildFromMaterial()));
        bar.Items.Add(Button("Import Source PNG", (_, _) => ImportSource()));
        bar.Items.Add(Button("Export Source PNG", (_, _) => ExportSource()));
        bar.Items.Add(Button("Export Shape Atlas", (_, _) => ExportShapeAtlas()));
        bar.Items.Add(Button("Reset Template", (_, _) => ResetTemplate()));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(Button("Validate", (_, _) => RefreshValidation()));
        var play = new ToolStripButton("Animate") { CheckOnClick = true };
        play.CheckedChanged += (_, _) => _animationTimer.Enabled = play.Checked && GetFrameCount() > 1;
        bar.Items.Add(play);
        return bar;
    }

    private Control BuildAssetPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(4) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = "Autotile Definitions", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 4, 0, 5) }, 0, 0);
        _assetList.Dock = DockStyle.Fill;
        _assetList.DisplayMember = nameof(AutotileAssetReference.Name);
        _assetList.SelectedIndexChanged += (_, _) => SelectReference(_assetList.SelectedItem as AutotileAssetReference);
        panel.Controls.Add(_assetList, 0, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var duplicate = new Button { Text = "Duplicate", AutoSize = true };
        duplicate.Click += (_, _) => DuplicateCurrent();
        var delete = new Button { Text = "Delete", AutoSize = true };
        delete.Click += (_, _) => DeleteCurrent();
        buttons.Controls.Add(duplicate);
        buttons.Controls.Add(delete);
        panel.Controls.Add(buttons, 0, 2);
        return panel;
    }

    private Control BuildTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildSourceTab());
        tabs.TabPages.Add(BuildGalleryTab());
        tabs.TabPages.Add(BuildTortureTab());
        tabs.TabPages.Add(BuildTestMapTab());
        tabs.TabPages.Add(BuildIssuesTab());
        return tabs;
    }

    private TabPage BuildSourceTab()
    {
        var page = new TabPage("Source Block");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var tools = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(4) };
        foreach (var tool in Enum.GetValues<AutotilePaintTool>())
        {
            var button = new RadioButton { Appearance = Appearance.Button, Text = tool.ToString(), AutoSize = true, Checked = tool == AutotilePaintTool.Pencil };
            button.CheckedChanged += (_, _) => { if (button.Checked) _sourceCanvas.Tool = tool; };
            tools.Controls.Add(button);
        }
        var colorButton = new Button { Text = "Colour…", AutoSize = true };
        colorButton.Click += (_, _) => ChooseColor();
        tools.Controls.Add(colorButton);
        _colorSwatch.Size = new Size(32, 24);
        _colorSwatch.BackColor = Color.White;
        _colorSwatch.BorderStyle = BorderStyle.FixedSingle;
        tools.Controls.Add(_colorSwatch);
        var pixelGrid = new CheckBox { Text = "Pixel grid", AutoSize = true };
        pixelGrid.CheckedChanged += (_, _) => { _sourceCanvas.ShowPixelGrid = pixelGrid.Checked; _sourceCanvas.Invalidate(); };
        tools.Controls.Add(pixelGrid);
        tools.Controls.Add(QuarterButton("Import quarter…", ImportQuarter));
        tools.Controls.Add(QuarterButton("Export quarter…", ExportQuarter));
        tools.Controls.Add(QuarterButton("Copy quarter", CopyQuarter));
        tools.Controls.Add(QuarterButton("Paste quarter", PasteQuarter));
        tools.Controls.Add(QuarterButton("Rotate 90°", () => TransformQuarter(RotateFlipType.Rotate90FlipNone)));
        tools.Controls.Add(QuarterButton("Flip H", () => TransformQuarter(RotateFlipType.RotateNoneFlipX)));
        tools.Controls.Add(QuarterButton("Flip V", () => TransformQuarter(RotateFlipType.RotateNoneFlipY)));
        tools.Controls.Add(QuarterButton("Copy to all frames", CopyQuarterToAllFrames));
        _quarterInfo.AutoSize = true;
        _quarterInfo.Padding = new Padding(8, 5, 0, 0);
        _quarterInfo.Text = "Click a 24 × 24 quarter to inspect it.";
        tools.Controls.Add(_quarterInfo);
        root.Controls.Add(tools, 0, 0);

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(37, 37, 38) };
        _sourceCanvas.EditStarted += (_, _) => CaptureUndo();
        _sourceCanvas.ImageChanged += (_, _) => MarkDirtyAndRefresh();
        _sourceCanvas.ColorPicked += (_, e) => SetPaintColor(e.Color);
        _sourceCanvas.SourcePixelSelected += (_, e) => SelectQuarter(e.Pixel);
        host.Controls.Add(_sourceCanvas);
        root.Controls.Add(host, 0, 1);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildGalleryTab()
    {
        var page = new TabPage("All MV Shapes");
        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(37, 37, 38) };
        host.Controls.Add(_gallery);
        page.Controls.Add(host);
        return page;
    }

    private TabPage BuildTortureTab()
    {
        var page = new TabPage("Torture Test");
        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(37, 37, 38) };
        _torturePicture.SizeMode = PictureBoxSizeMode.AutoSize;
        host.Controls.Add(_torturePicture);
        page.Controls.Add(host);
        return page;
    }

    private TabPage BuildTestMapTab()
    {
        var page = new TabPage("Interactive Test Map");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var tools = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var clear = new Button { Text = "Clear", AutoSize = true }; clear.Click += (_, _) => _testMap.ClearMap();
        var torture = new Button { Text = "Load Torture Pattern", AutoSize = true }; torture.Click += (_, _) => _testMap.LoadTorturePattern();
        var random = new Button { Text = "Random Stress Map", AutoSize = true }; random.Click += (_, _) => _testMap.Randomize(Environment.TickCount);
        _fixedPatternCheck.Text = "Fixed/Shift pattern"; _fixedPatternCheck.AutoSize = true;
        _fixedPatternCheck.CheckedChanged += (_, _) => _testMap.FixedPatternMode = _fixedPatternCheck.Checked;
        tools.Controls.Add(clear); tools.Controls.Add(torture); tools.Controls.Add(random); tools.Controls.Add(_fixedPatternCheck);
        root.Controls.Add(tools, 0, 0);
        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(37, 37, 38) };
        _testMap.DiagnosticChanged += (_, e) => _diagnostic.Text = e.Text;
        host.Controls.Add(_testMap);
        root.Controls.Add(host, 0, 1);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildIssuesTab()
    {
        var page = new TabPage("Validation");
        _issues.Dock = DockStyle.Fill;
        _issues.View = View.Details;
        _issues.FullRowSelect = true;
        _issues.Columns.Add("Severity", 100);
        _issues.Columns.Add("Code", 70);
        _issues.Columns.Add("Message", 760);
        page.Controls.Add(_issues);
        return page;
    }

    private Control BuildPropertiesPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(8) };
        panel.Controls.Add(Header("Definition"));
        panel.Controls.Add(LabelFor("Name"));
        _nameBox.Dock = DockStyle.Top;
        _nameBox.TextChanged += (_, _) => { if (!_loading && _definition is not null) { _definition.Name = _nameBox.Text; MarkDefinitionDirty(); } };
        panel.Controls.Add(_nameBox);
        panel.Controls.Add(LabelFor("Kind"));
        _kindCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _kindCombo.DataSource = Enum.GetValues<AutotileKind>();
        _kindCombo.SelectedIndexChanged += (_, _) => ChangeKind();
        panel.Controls.Add(_kindCombo);
        panel.Controls.Add(LabelFor("Description"));
        _descriptionBox.Multiline = true; _descriptionBox.Height = 90; _descriptionBox.ScrollBars = ScrollBars.Vertical;
        _descriptionBox.TextChanged += (_, _) => { if (!_loading && _definition is not null) { _definition.Description = _descriptionBox.Text; MarkDefinitionDirty(); } };
        panel.Controls.Add(_descriptionBox);
        panel.Controls.Add(LabelFor("A2 usage mode"));
        _a2ModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _a2ModeCombo.DataSource = Enum.GetValues<A2UsageMode>();
        _a2ModeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _definition is not null && _a2ModeCombo.SelectedItem is A2UsageMode mode)
            {
                _definition.A2UsageMode = mode;
                MarkDefinitionDirty();
            }
        };
        panel.Controls.Add(_a2ModeCombo);
        _counterCheck.Text = "Counter/table behavior";
        _counterCheck.CheckedChanged += (_, _) => { if (!_loading && _definition is not null) { _definition.IsCounter = _counterCheck.Checked; MarkDefinitionDirty(); } };
        panel.Controls.Add(_counterCheck);
        _overlayCheck.Text = "Transparent overlay";
        _overlayCheck.CheckedChanged += (_, _) => { if (!_loading && _definition is not null) { _definition.IsOverlay = _overlayCheck.Checked; MarkDefinitionDirty(); } };
        panel.Controls.Add(_overlayCheck);
        _rotationCheck.Text = "Rotation-safe artwork";
        _rotationCheck.CheckedChanged += (_, _) => { if (!_loading && _definition is not null) { _definition.AllowRotation = _rotationCheck.Checked; MarkDefinitionDirty(); } };
        panel.Controls.Add(_rotationCheck);
        _mirroringCheck.Text = "Mirroring-safe artwork";
        _mirroringCheck.CheckedChanged += (_, _) => { if (!_loading && _definition is not null) { _definition.AllowMirroring = _mirroringCheck.Checked; MarkDefinitionDirty(); } };
        panel.Controls.Add(_mirroringCheck);
        panel.Controls.Add(LabelFor("Animation frame"));
        _frameBox.Minimum = 1; _frameBox.Maximum = 3; _frameBox.Value = 1;
        _frameBox.ValueChanged += (_, _) => RefreshPreviews();
        panel.Controls.Add(_frameBox);
        panel.Controls.Add(Header("Source"));
        _sourceInfo.AutoSize = true; _sourceInfo.MaximumSize = new Size(280, 0);
        panel.Controls.Add(_sourceInfo);
        panel.Controls.Add(Header("Compilation target"));
        _targetInfo.AutoSize = true; _targetInfo.MaximumSize = new Size(280, 0);
        panel.Controls.Add(_targetInfo);
        var compile = new Button { Text = "Compile into selected MV region", AutoSize = true };
        compile.Click += (_, _) => CompileToTarget();
        panel.Controls.Add(compile);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill });
        return panel;
    }

    private void RefreshAssetList(Guid? selectId = null)
    {
        _assetList.BeginUpdate();
        _assetList.Items.Clear();
        foreach (var reference in Project.Autotiles.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) _assetList.Items.Add(reference);
        _assetList.EndUpdate();
        if (selectId.HasValue)
        {
            for (var i = 0; i < _assetList.Items.Count; i++)
                if (((AutotileAssetReference)_assetList.Items[i]).Id == selectId) { _assetList.SelectedIndex = i; break; }
        }
    }

    private void SelectReference(AutotileAssetReference? reference)
    {
        if (reference is null || reference == _currentReference) return;
        if (_dirty) SaveCurrent(refreshList: false);
        _source?.Dispose();
        ClearHistory(_undo); ClearHistory(_redo);
        _currentReference = reference;
        _definition = AutotileSerializer.Load(Project, reference);
        _source = AutotileSerializer.LoadSource(Project, _definition);
        _dirty = false;
        LoadProperties();
        _sourceCanvas.Bitmap = _source;
        SelectQuarter(Point.Empty);
        RefreshPreviews();
        RefreshValidation();
        UpdateUi();
    }

    private void NewAutotile()
    {
        using var dialog = new NewAutotileDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var definition = new AutotileDefinition { Name = dialog.AutotileName, Kind = dialog.AutotileKind };
        using var source = AutotileTemplateFactory.CreateDefault(definition.Kind);
        var reference = AutotileSerializer.SaveNew(Project, definition, source);
        _workspace.MarkProjectDirty();
        RefreshAssetList(reference.Id);
    }

    private void ExtractTargetRegion()
    {
        if (!TryGetTarget(out var regionMetadata)) return;
        var suggested = string.IsNullOrWhiteSpace(regionMetadata.Name)
            ? $"{_targetSheet!.Slot} {_targetRegionIndex!.Value + 1:00}"
            : regionMetadata.Name;
        var extracted = _inverseImporter.Extract(_targetSession!.Bitmap, _targetSheet!.Slot, _targetRegionIndex!.Value, suggested);
        extracted.Definition.Target = new AutotileTargetBinding
        {
            TileSetId = _targetTileSet!.Id,
            SheetId = _targetSheet.Id,
            Slot = _targetSheet.Slot,
            RegionIndex = _targetRegionIndex.Value
        };
        var reference = AutotileSerializer.SaveNew(Project, extracted.Definition, extracted.Source);
        extracted.Source.Dispose();
        _workspace.MarkProjectDirty();
        RefreshAssetList(reference.Id);
        _diagnostic.Text = "The selected MV source block was converted into an editable definition without resampling.";
    }

    private void DuplicateCurrent()
    {
        if (_definition is null || _source is null) return;
        var clone = new AutotileDefinition
        {
            Name = _definition.Name + " Copy",
            Description = _definition.Description,
            Kind = _definition.Kind,
            Seed = _definition.Seed,
            AllowMirroring = _definition.AllowMirroring,
            AllowRotation = _definition.AllowRotation,
            IsCounter = _definition.IsCounter,
            IsOverlay = _definition.IsOverlay,
            Tags = new List<string>(_definition.Tags)
        };
        using var sourceClone = (Bitmap)_source.Clone();
        var reference = AutotileSerializer.SaveNew(Project, clone, sourceClone);
        _workspace.MarkProjectDirty();
        RefreshAssetList(reference.Id);
    }

    private void DeleteCurrent()
    {
        if (_currentReference is null) return;
        if (MessageBox.Show(this, $"Delete '{_currentReference.Name}' and its source files?", "Delete Autotile",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        AutotileSerializer.DeleteFiles(Project, _currentReference);
        Project.Autotiles.Remove(_currentReference);
        _workspace.MarkProjectDirty();
        _currentReference = null; _definition = null; _source?.Dispose(); _source = null; _dirty = false;
        _sourceCanvas.Bitmap = null;
        _selectedQuarter = null;
        _sourceCanvas.SelectedQuarterBounds = null;
        _quarterInfo.Text = "Click a 24 × 24 quarter to inspect it.";
        RefreshAssetList();
        if (_assetList.Items.Count > 0) _assetList.SelectedIndex = 0;
        UpdateUi();
    }

    private void SaveCurrent(bool refreshList = true)
    {
        if (_definition is null || _source is null || _currentReference is null) return;
        if (!_dirty) return;
        _definition.Revision++;
        AutotileSerializer.Save(Project, _definition, _source, _currentReference.RelativeDefinitionPath);
        _currentReference.Name = _definition.Name;
        _workspace.MarkProjectDirty();
        _dirty = false;
        if (refreshList) RefreshAssetList(_definition.Id);
        RefreshValidation();
        _diagnostic.Text = $"Saved revision {_definition.Revision}.";
        UpdateUi();
    }

    private void CompileToTarget()
    {
        if (_definition is null || _source is null || !TryGetTarget(out var regionMetadata)) return;
        if (_dirty) SaveCurrent();
        if (!AutotileKindInfo.IsCompatible(_definition.Kind, _targetSheet!.Slot, _targetRegionIndex!.Value))
        {
            MessageBox.Show(this,
                $"{_definition.Kind} is not compatible with {_targetSheet.Slot} region {_targetRegionIndex.Value + 1}.\n\n" +
                "Extract that region to create a correctly typed definition, or select a matching target in the main editor.",
                "Incompatible Target", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (regionMetadata.IsLocked)
        {
            MessageBox.Show(this, "The selected region is locked.", "Compile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _targetSession!.CaptureBeforeEdit();
        _compiler.CompileIntoSheet(_targetSession.Bitmap, regionMetadata, _targetSheet.Slot, _targetRegionIndex.Value, _definition, _source);
        _definition.Target = new AutotileTargetBinding
        {
            TileSetId = _targetTileSet!.Id,
            SheetId = _targetSheet.Id,
            Slot = _targetSheet.Slot,
            RegionIndex = _targetRegionIndex.Value
        };
        _targetSession.MarkEdited();
        _workspace.MarkProjectDirty();
        AutotileSerializer.Save(Project, _definition, _source, _currentReference!.RelativeDefinitionPath);
        _currentReference.Name = _definition.Name;
        _currentReference.Revision = _definition.Revision;
        _currentReference.ModifiedUtc = _definition.ModifiedUtc;
        _dirty = false;
        RefreshValidation();
        UpdateUi();
        _diagnostic.Text = $"Compiled {_definition.Name} into {_targetSheet.Slot} region {_targetRegionIndex.Value + 1}. The operation is undoable in the main editor.";
    }

    private void RecompileAllOutdated()
    {
        if (_dirty) SaveCurrent();
        var compiled = 0;
        var skipped = 0;
        var failures = new List<string>();
        foreach (var reference in Project.Autotiles.ToArray())
        {
            try
            {
                var definition = AutotileSerializer.Load(Project, reference);
                if (definition.Target is null) { skipped++; continue; }
                var tileSet = Project.TileSets.FirstOrDefault(x => x.Id == definition.Target.TileSetId);
                var sheet = tileSet?.Sheets.FirstOrDefault(x => x.Id == definition.Target.SheetId);
                var region = sheet?.Regions.FirstOrDefault(x => x.Index == definition.Target.RegionIndex);
                if (tileSet is null || sheet is null || region is null)
                {
                    failures.Add($"{definition.Name}: saved target no longer exists.");
                    continue;
                }
                var dependency = new MaterialDependencyService();
                using var source = dependency.IsMaterialSourceOutdated(Project, definition)
                    ? dependency.RebuildAutotileSource(Project, definition)
                    : AutotileSerializer.LoadSource(Project, definition);
                var sourceHash = AutotileBitmap.ComputeSha256(source);
                if (region.SourceAutotileId == definition.Id &&
                    region.SourceAutotileRevision == definition.Revision &&
                    string.Equals(region.CompiledSourceSha256, sourceHash, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }
                if (!AutotileKindInfo.IsCompatible(definition.Kind, sheet.Slot, definition.Target.RegionIndex))
                {
                    failures.Add($"{definition.Name}: {definition.Kind} is incompatible with its saved target.");
                    continue;
                }
                if (region.IsLocked)
                {
                    failures.Add($"{definition.Name}: its saved target region is locked.");
                    continue;
                }
                var session = _workspace.OpenSheet(sheet);
                session.CaptureBeforeEdit();
                _compiler.CompileIntoSheet(session.Bitmap, region, sheet.Slot, definition.Target.RegionIndex, definition, source);
                session.MarkEdited();
                AutotileSerializer.Save(Project, definition, source, reference.RelativeDefinitionPath);
                reference.Name = definition.Name;
                reference.Revision = definition.Revision;
                reference.ModifiedUtc = definition.ModifiedUtc;
                compiled++;
            }
            catch (Exception ex)
            {
                failures.Add($"{reference.Name}: {ex.Message}");
            }
        }
        if (compiled > 0) _workspace.MarkProjectDirty();
        var message = $"Recompiled {compiled} outdated autotile(s); skipped {skipped} current or unbound definition(s).";
        if (failures.Count > 0)
        {
            message += "\r\n\r\nProblems:\r\n" + string.Join("\r\n", failures.Take(12));
        }
        MessageBox.Show(this, message, "Batch Recompile",
            MessageBoxButtons.OK, failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        _diagnostic.Text = message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)[0];
        RefreshAssetList(_definition?.Id);
    }

    private bool TryGetTarget(out TileRegionMetadata metadata)
    {
        metadata = null!;
        if (_targetSession is null || _targetSheet is null || _targetTileSet is null || !_targetRegionIndex.HasValue)
        {
            MessageBox.Show(this, "Select a logical A1–A4 region in the main editor before opening the workshop.",
                "No MV Target", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        metadata = _targetSheet.Regions.FirstOrDefault(x => x.Index == _targetRegionIndex.Value)!;
        if (metadata is null)
        {
            MessageBox.Show(this, "The selected logical region has no project metadata.", "No MV Target",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    private void BuildFromMaterial()
    {
        if (_definition is null) return;
        using var dialog = new OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
            Title = "Choose a Material Exemplar"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this,
                "This will replace the complete source block with a deterministic autotile built from the selected material. Continue?",
                "Build from Material", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        using var material = Image.FromFile(dialog.FileName);
        var composed = _materialComposer.Compose(_definition.Kind, material);
        ReplaceSource(composed, captureUndo: true);
        _diagnostic.Text = "Built a complete MV source block from the material exemplar. Adjust individual quarters or edges as needed.";
    }

    private void SelectQuarter(Point sourcePixel)
    {
        if (_definition is null || _source is null) return;
        sourcePixel.X = Math.Clamp(sourcePixel.X, 0, _source.Width - 1);
        sourcePixel.Y = Math.Clamp(sourcePixel.Y, 0, _source.Height - 1);
        _selectedQuarter = AutotileQuarterCatalog.Locate(_definition.Kind, sourcePixel);
        _sourceCanvas.SelectedQuarterBounds = _selectedQuarter.Bounds;
        _quarterInfo.Text = $"Frame {_selectedQuarter.Frame + 1} · Q({_selectedQuarter.QuarterX},{_selectedQuarter.QuarterY}) · {_selectedQuarter.Label}";
    }

    private void ImportQuarter()
    {
        if (_source is null || _selectedQuarter is null) return;
        using var dialog = new OpenFileDialog { Filter = "PNG image (*.png)|*.png|All images|*.png;*.jpg;*.jpeg;*.bmp", Title = "Import Quarter Component" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var image = Image.FromFile(dialog.FileName);
        using var scaled = ScaleToQuarter(image);
        CaptureUndo();
        DrawQuarter(scaled, _selectedQuarter.Bounds);
        MarkDirtyAndRefresh();
    }

    private void ExportQuarter()
    {
        if (_source is null || _selectedQuarter is null || _definition is null) return;
        using var quarter = AutotileBitmap.Crop(_source, _selectedQuarter.Bounds);
        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = SafeFileName($"{_definition.Name}_{_selectedQuarter.Label}") + ".png"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) quarter.Save(dialog.FileName, ImageFormat.Png);
    }

    private void CopyQuarter()
    {
        if (_source is null || _selectedQuarter is null) return;
        _quarterClipboard?.Dispose();
        _quarterClipboard = AutotileBitmap.Crop(_source, _selectedQuarter.Bounds);
        _diagnostic.Text = $"Copied {_selectedQuarter.Label}.";
    }

    private void PasteQuarter()
    {
        if (_source is null || _selectedQuarter is null || _quarterClipboard is null) return;
        CaptureUndo();
        DrawQuarter(_quarterClipboard, _selectedQuarter.Bounds);
        MarkDirtyAndRefresh();
    }

    private void TransformQuarter(RotateFlipType transform)
    {
        if (_source is null || _selectedQuarter is null) return;
        using var quarter = AutotileBitmap.Crop(_source, _selectedQuarter.Bounds);
        quarter.RotateFlip(transform);
        CaptureUndo();
        DrawQuarter(quarter, _selectedQuarter.Bounds);
        MarkDirtyAndRefresh();
    }

    private void CopyQuarterToAllFrames()
    {
        if (_definition is null || _source is null || _selectedQuarter is null || GetFrameCount() <= 1) return;
        using var quarter = AutotileBitmap.Crop(_source, _selectedQuarter.Bounds);
        CaptureUndo();
        for (var frame = 0; frame < GetFrameCount(); frame++)
        {
            var target = AutotileQuarterCatalog.Create(_definition.Kind, frame,
                _selectedQuarter.QuarterX, _selectedQuarter.QuarterY);
            DrawQuarter(quarter, target.Bounds);
        }
        MarkDirtyAndRefresh();
        SelectQuarter(_selectedQuarter.Bounds.Location);
    }

    private void DrawQuarter(Image image, Rectangle destination)
    {
        if (_source is null) return;
        using var graphics = Graphics.FromImage(_source);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(image, destination, new Rectangle(Point.Empty, image.Size), GraphicsUnit.Pixel);
        _sourceCanvas.Invalidate(new Rectangle(destination.X * _sourceCanvas.Zoom,
            destination.Y * _sourceCanvas.Zoom,
            destination.Width * _sourceCanvas.Zoom + 2,
            destination.Height * _sourceCanvas.Zoom + 2));
    }

    private static Bitmap ScaleToQuarter(Image image)
    {
        if (image.Width == 24 && image.Height == 24) return AutotileBitmap.CloneArgb(image);
        var result = new Bitmap(24, 24, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(image, new Rectangle(0, 0, 24, 24), new Rectangle(Point.Empty, image.Size), GraphicsUnit.Pixel);
        return result;
    }

    private void ImportSource()
    {
        if (_definition is null) return;
        using var dialog = new OpenFileDialog { Filter = "PNG image (*.png)|*.png", Title = "Import Autotile Source Block" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var imported = AutotileBitmap.LoadEditable(dialog.FileName);
        var expected = AutotileKindInfo.GetSourceSize(_definition.Kind);
        if (imported.Size != expected)
        {
            MessageBox.Show(this, $"This autotile requires an exact {expected.Width} × {expected.Height} image.",
                "Wrong Source Size", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ReplaceSource((Bitmap)imported.Clone(), true);
    }

    private void ExportSource()
    {
        if (_source is null || _definition is null) return;
        using var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = SafeFileName(_definition.Name) + "_source.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _source.Save(dialog.FileName, ImageFormat.Png);
    }

    private void ExportShapeAtlas()
    {
        if (_source is null || _definition is null) return;
        var frame = Math.Clamp((int)_frameBox.Value - 1, 0, GetFrameCount() - 1);
        using var atlas = new MvAutotileRenderer().RenderShapeAtlas(_source, _definition.Kind, frame);
        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = SafeFileName(_definition.Name) + $"_shapes_frame{frame + 1}.png"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) atlas.Save(dialog.FileName, ImageFormat.Png);
    }

    private void ResetTemplate()
    {
        if (_definition is null) return;
        if (MessageBox.Show(this, "Replace the current source pixels with a clean generated template?",
                "Reset Template", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        CaptureUndo();
        ReplaceSource(AutotileTemplateFactory.CreateDefault(_definition.Kind), false);
    }

    private void ChangeKind()
    {
        if (_loading || _definition is null || _kindCombo.SelectedItem is not AutotileKind kind || kind == _definition.Kind) return;
        var oldSize = AutotileKindInfo.GetSourceSize(_definition.Kind);
        var newSize = AutotileKindInfo.GetSourceSize(kind);
        if (oldSize != newSize && MessageBox.Show(this,
                $"Changing from {_definition.Kind} to {kind} changes the source size from {oldSize.Width} × {oldSize.Height} to {newSize.Width} × {newSize.Height}.\n\nCreate a new clean template?",
                "Change Autotile Kind", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            _loading = true; _kindCombo.SelectedItem = _definition.Kind; _loading = false;
            return;
        }
        _definition.Kind = kind;
        if (oldSize != newSize)
        {
            CaptureUndo();
            ReplaceSource(AutotileTemplateFactory.CreateDefault(kind), false);
        }
        MarkDefinitionDirty();
        LoadFrameRange();
        RefreshPreviews();
    }

    private void ChooseColor()
    {
        using var dialog = new ColorDialog { Color = _sourceCanvas.ForeColorValue, FullOpen = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) SetPaintColor(dialog.Color);
    }

    private void SetPaintColor(Color color)
    {
        _sourceCanvas.ForeColorValue = color;
        _colorSwatch.BackColor = color.A == 0 ? Color.Magenta : color;
    }

    private void CaptureUndo()
    {
        if (_source is null) return;
        _undo.AddLast((Bitmap)_source.Clone());
        while (_undo.Count > 30) { _undo.First!.Value.Dispose(); _undo.RemoveFirst(); }
        ClearHistory(_redo);
    }

    private void UndoPixels()
    {
        if (_source is null || _undo.Last is null) return;
        _redo.AddLast((Bitmap)_source.Clone());
        var replacement = _undo.Last.Value; _undo.RemoveLast();
        ReplaceSource(replacement, false);
    }

    private void RedoPixels()
    {
        if (_source is null || _redo.Last is null) return;
        _undo.AddLast((Bitmap)_source.Clone());
        var replacement = _redo.Last.Value; _redo.RemoveLast();
        ReplaceSource(replacement, false);
    }

    private void ReplaceSource(Bitmap replacement, bool captureUndo)
    {
        if (captureUndo) CaptureUndo();
        _source?.Dispose();
        _source = replacement;
        _sourceCanvas.Bitmap = _source;
        SelectQuarter(Point.Empty);
        MarkDirtyAndRefresh();
    }

    private void MarkDirtyAndRefresh()
    {
        _dirty = true;
        RefreshPreviews();
        RefreshValidation();
        UpdateUi();
    }

    private void MarkDefinitionDirty()
    {
        _dirty = true;
        if (_currentReference is not null) _currentReference.Name = _definition?.Name ?? _currentReference.Name;
        UpdateUi();
    }

    private void LoadProperties()
    {
        if (_definition is null) return;
        _loading = true;
        _nameBox.Text = _definition.Name;
        _descriptionBox.Text = _definition.Description;
        _kindCombo.SelectedItem = _definition.Kind;
        _a2ModeCombo.SelectedItem = _definition.A2UsageMode;
        _counterCheck.Checked = _definition.IsCounter;
        _overlayCheck.Checked = _definition.IsOverlay;
        _rotationCheck.Checked = _definition.AllowRotation;
        _mirroringCheck.Checked = _definition.AllowMirroring;
        LoadFrameRange();
        _loading = false;
        _sourceInfo.Text = $"{AutotileKindInfo.GetSourceSize(_definition.Kind).Width} × {AutotileKindInfo.GetSourceSize(_definition.Kind).Height} px\n" +
                           $"Family: {AutotileKindInfo.GetFamily(_definition.Kind)}\nRevision: {_definition.Revision}";
    }

    private void LoadFrameRange()
    {
        var count = GetFrameCount();
        _frameBox.Maximum = Math.Max(1, count);
        if (_frameBox.Value > count) _frameBox.Value = 1;
        _frameBox.Enabled = count > 1;
    }

    private void RefreshPreviews()
    {
        if (_source is null || _definition is null) return;
        var frame = Math.Clamp((int)_frameBox.Value - 1, 0, GetFrameCount() - 1);
        _gallery.SetSource(_source, _definition.Kind, frame);
        _testMap.SetSource(_source, _definition.Kind, frame);
        var image = _tortureRenderer.Render(_source, _definition.Kind, frame);
        DisposeImage(_torturePicture);
        _torturePicture.Image = image;
        _sourceInfo.Text = $"{_source.Width} × {_source.Height} px\nFamily: {AutotileKindInfo.GetFamily(_definition.Kind)}\n" +
                           $"Frame {frame + 1} of {GetFrameCount()}\nRevision: {_definition.Revision}";
    }

    private void RefreshValidation()
    {
        _issues.Items.Clear();
        if (_source is null || _definition is null) return;
        foreach (var issue in _validator.Validate(_definition, _source))
        {
            var item = new ListViewItem(issue.Severity.ToString());
            item.SubItems.Add(issue.Code);
            item.SubItems.Add(issue.Message);
            _issues.Items.Add(item);
        }
        if (_issues.Items.Count == 0)
        {
            var item = new ListViewItem("Information"); item.SubItems.Add("AT000"); item.SubItems.Add("No structural problems detected.");
            _issues.Items.Add(item);
        }
    }

    private void AdvanceFrame()
    {
        if (GetFrameCount() <= 1) return;
        _frameBox.Value = _frameBox.Value >= _frameBox.Maximum ? 1 : _frameBox.Value + 1;
    }

    private void UpdateTargetInfo()
    {
        _targetInfo.Text = _targetSheet is null || !_targetRegionIndex.HasValue
            ? "No logical A1–A4 region was selected when the workshop opened."
            : $"Tileset: {_targetTileSet?.Name}\nSheet: {_targetSheet.Slot}\nRegion: {_targetRegionIndex.Value + 1}\n" +
              $"Expected kind: {AutotileKindInfo.InferKind(_targetSheet.Slot, _targetRegionIndex.Value)}";
    }

    private void UpdateUi()
    {
        Text = "Playroma Tile — Autotile Compiler Workshop" + (_dirty ? " *" : string.Empty);
        _nameBox.Enabled = _definition is not null;
        _kindCombo.Enabled = _definition is not null;
        _descriptionBox.Enabled = _definition is not null;
        _a2ModeCombo.Enabled = _definition?.Kind == AutotileKind.A2Floor;
        _counterCheck.Enabled = _definition?.Kind == AutotileKind.A2Floor;
        _sourceCanvas.Enabled = _source is not null;
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_dirty) return;
        var result = MessageBox.Show(this, "Save changes to the current autotile definition?", "Autotile Workshop",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.Cancel) { e.Cancel = true; return; }
        if (result == DialogResult.Yes) SaveCurrent();
    }

    private int GetFrameCount() => _definition is null ? 1 : AutotileKindInfo.GetFrameCount(_definition.Kind);
    private TileProject Project => _workspace.Project ?? throw new InvalidOperationException("No project is open.");
    private static Button QuarterButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        return button;
    }

    private static ToolStripButton Button(string text, EventHandler handler) { var button = new ToolStripButton(text); button.Click += handler; return button; }
    private static Label Header(string text) => new() { Text = text, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(0, 12, 0, 4) };
    private static Label LabelFor(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(0, 5, 0, 2) };
    private static void DisposeImage(PictureBox picture) { var old = picture.Image; picture.Image = null; old?.Dispose(); }
    private static void ClearHistory(LinkedList<Bitmap> history) { foreach (var bitmap in history) bitmap.Dispose(); history.Clear(); }
    private static string SafeFileName(string value) => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}

internal sealed class NewAutotileDialog : Form
{
    private readonly TextBox _name = new() { Text = "New Autotile", Dock = DockStyle.Top };
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };

    public NewAutotileDialog()
    {
        Text = "Create Autotile Definition";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Width = 420; Height = 220;
        _kind.DataSource = Enum.GetValues<AutotileKind>();
        _kind.SelectedItem = AutotileKind.A2Floor;
        var form = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 5 };
        form.Controls.Add(new Label { Text = "Name", AutoSize = true });
        form.Controls.Add(_name);
        form.Controls.Add(new Label { Text = "RPG Maker MV type", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        form.Controls.Add(_kind);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };
        var ok = new Button { Text = "Create", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        form.Controls.Add(buttons);
        Controls.Add(form);
        AcceptButton = ok; CancelButton = cancel;
    }

    public string AutotileName => string.IsNullOrWhiteSpace(_name.Text) ? "New Autotile" : _name.Text.Trim();
    public AutotileKind AutotileKind => _kind.SelectedItem is AutotileKind kind ? kind : AutotileKind.A2Floor;
}
