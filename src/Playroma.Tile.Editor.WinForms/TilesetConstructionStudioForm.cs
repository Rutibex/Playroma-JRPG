using System.Drawing.Drawing2D;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Services;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.Editor.WinForms.Controls;
using Playroma.Tile.Editor.WinForms.Services;
using Playroma.Tile.Materials.Integration;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Serialization;
using Playroma.Tile.Materials.Services;
using Playroma.Tile.RpgMakerMV.Formats;
using Playroma.Tile.RpgMakerMV.Imaging;
using Playroma.Tile.Tilesets.Export;
using Playroma.Tile.Tilesets.Integration;
using Playroma.Tile.Tilesets.Models;
using Playroma.Tile.Tilesets.Rendering;
using Playroma.Tile.Tilesets.Services;
using Playroma.Tile.Tilesets.Validation;

namespace Playroma.Tile.Editor.WinForms;

public sealed class TilesetConstructionStudioForm : Form
{
    private readonly EditorWorkspace _workspace;
    private readonly TileProject _project;
    private readonly TileSetDefinition _tileSet;
    private TilesetAssemblyDefinition _definition;
    private TilesetConstructionReference _reference;
    private readonly Dictionary<MvSheetSlot, SheetSession> _sessions = new();
    private readonly Dictionary<MvSheetSlot, TileSheetCanvas> _canvases = new();
    private readonly TabControl _sheetTabs = new();
    private readonly ListView _assetLibrary = new();
    private readonly TilesetMetadataEditor _metadataEditor = new();
    private readonly TextBox _assetName = new();
    private readonly TextBox _assetCategory = new();
    private readonly CheckBox _assetLocked = new() { Text = "Lock selected slot" };
    private readonly Label _assetSource = new() { AutoSize = true };
    private readonly Label _assetStatus = new() { AutoSize = true };
    private readonly ListView _issues = new();
    private readonly DataGridView _coverage = new();
    private readonly TextBox _analysis = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly FullTestMapControl _testMap = new();
    private readonly ListBox _testPalette = new();
    private readonly ComboBox _testLayer = new();
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _score = new();
    private bool _loadingSelection;

    public TilesetConstructionStudioForm(EditorWorkspace workspace, TileSetDefinition tileSet)
    {
        _workspace = workspace;
        _project = workspace.Project ?? throw new InvalidOperationException("No project is open.");
        _tileSet = tileSet;
        new TilesetConstructionService().EnsureCompleteSheets(_project, _tileSet);
        _definition = TilesetSerializer.LoadOrCreate(_project, _tileSet);
        _reference = TilesetSerializer.GetReference(_project, _definition);
        TilesetSerializer.InitializeBindings(_tileSet, _definition);
        Text = $"Playroma Tile — Tileset Construction — {_tileSet.Name}";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1580;
        Height = 960;
        MinimumSize = new Size(1120, 720);
        KeyPreview = true;

        var menu = BuildMenu();
        var toolbar = BuildToolbar();
        var status = new StatusStrip();
        status.Items.Add(_status);
        status.Items.Add(_score);
        Controls.Add(BuildContent());
        Controls.Add(status);
        Controls.Add(toolbar);
        Controls.Add(menu);
        MainMenuStrip = menu;

        BuildSheetTabs();
        ConfigureLists();
        RefreshAll();
        FormClosing += OnFormClosing;
        KeyDown += OnStudioKeyDown;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(Item("&Save Construction", Keys.Control | Keys.S, (_, _) => SaveAll()));
        file.DropDownItems.Add(Item("Create &Snapshot…", Keys.Control | Keys.Shift | Keys.S, (_, _) => CreateSnapshot()));
        file.DropDownItems.Add(Item("&Restore Snapshot…", Keys.None, (_, _) => RestoreSnapshot()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Export Publication &Package…", Keys.Control | Keys.E, (_, _) => ExportPackage()));
        file.DropDownItems.Add(Item("Patch RPG Maker MV &Project…", Keys.None, (_, _) => PatchMvProject()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("&Close", Keys.Alt | Keys.F4, (_, _) => Close()));

        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.Add(Item("&Undo Sheet", Keys.Control | Keys.Z, (_, _) => UndoSheet()));
        edit.DropDownItems.Add(Item("&Redo Sheet", Keys.Control | Keys.Y, (_, _) => RedoSheet()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Item("Clear Selected Slot", Keys.Delete, (_, _) => ClearSelectedSlot()));
        edit.DropDownItems.Add(Item("Apply Slot Properties", Keys.Control | Keys.Enter, (_, _) => ApplySlotProperties()));

        var assets = new ToolStripMenuItem("&Assets");
        assets.DropDownItems.Add(Item("Import Ordinary &Object…", Keys.Control | Keys.I, (_, _) => ImportObject()));
        assets.DropDownItems.Add(Item("Edit Selected Object…", Keys.None, (_, _) => EditSelectedObject()));
        assets.DropDownItems.Add(Item("Create Object &Variation…", Keys.None, (_, _) => CreateObjectVariation()));
        assets.DropDownItems.Add(new ToolStripSeparator());
        // WinForms rejects a bare Enter key as ToolStripMenuItem.ShortcutKeys (value 13),
        // even though Enter is a perfectly valid accelerator for our asset list.  Keep the
        // working KeyDown behavior and render the shortcut text without assigning the
        // invalid ShortcutKeys value.
        assets.DropDownItems.Add(Item("Assign Selected Asset to Slot", Keys.None, (_, _) => AssignSelectedAsset(), "Enter"));
        assets.DropDownItems.Add(Item("Auto-Pack Objects into B–E", Keys.None, (_, _) => AutoPack()));

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add(Item("Build Tileset from &Theme…", Keys.None, (_, _) => AssistedBuild()));
        tools.DropDownItems.Add(Item("Recommend Metadata", Keys.None, (_, _) => RecommendMetadata()));
        tools.DropDownItems.Add(Item("Validate Tileset", Keys.F9, (_, _) => ValidateTileset()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(Item("Analyze Style Consistency", Keys.None, (_, _) => AnalyzeStyle()));
        tools.DropDownItems.Add(Item("Harmonize Sheets…", Keys.None, (_, _) => Harmonize()));
        tools.DropDownItems.Add(Item("Find Duplicate Tiles", Keys.None, (_, _) => FindDuplicates()));
        tools.DropDownItems.Add(Item("Generate Showcase Scene…", Keys.None, (_, _) => ExportShowcase()));

        menu.Items.AddRange(new ToolStripItem[] { file, edit, assets, tools });
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        toolbar.Items.Add(Button("Save", (_, _) => SaveAll()));
        toolbar.Items.Add(Button("Import Object", (_, _) => ImportObject()));
        toolbar.Items.Add(Button("Assign", (_, _) => AssignSelectedAsset()));
        toolbar.Items.Add(Button("Auto Pack", (_, _) => AutoPack()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(Button("Build Theme", (_, _) => AssistedBuild()));
        toolbar.Items.Add(Button("Validate", (_, _) => ValidateTileset()));
        toolbar.Items.Add(Button("Export", (_, _) => ExportPackage()));
        return toolbar;
    }

    private Control BuildContent()
    {
        var mainTabs = new TabControl { Dock = DockStyle.Fill };
        mainTabs.TabPages.Add(new TabPage("Construction") { Controls = { BuildConstructionPage() } });
        mainTabs.TabPages.Add(new TabPage("Full Test Map") { Controls = { BuildTestMapPage() } });
        mainTabs.TabPages.Add(new TabPage("Completeness") { Controls = { BuildCoveragePage() } });
        mainTabs.TabPages.Add(new TabPage("Analysis") { Controls = { BuildAnalysisPage() } });
        return mainTabs;
    }

    private Control BuildConstructionPage()
    {
        var left = new Panel { Dock = DockStyle.Fill };
        var libraryButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(3) };
        libraryButtons.Controls.AddRange(new Control[]
        {
            SmallButton("Import", (_, _) => ImportObject()),
            SmallButton("Edit", (_, _) => EditSelectedObject()),
            SmallButton("Assign", (_, _) => AssignSelectedAsset())
        });
        left.Controls.Add(_assetLibrary);
        left.Controls.Add(libraryButtons);
        _assetLibrary.Dock = DockStyle.Fill;

        var rightTabs = new TabControl { Dock = DockStyle.Fill };
        rightTabs.TabPages.Add(new TabPage("Slot") { Controls = { BuildSlotPanel() } });
        rightTabs.TabPages.Add(new TabPage("Gameplay") { Controls = { _metadataEditor } });

        ConfigureIssueList();
        var centerSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(1200, 800), SplitterDistance = 600, Panel1MinSize = 280, Panel2MinSize = 100 };
        centerSplit.Panel1.Controls.Add(_sheetTabs);
        centerSplit.Panel2.Controls.Add(_issues);
        _sheetTabs.Dock = DockStyle.Fill;

        var inner = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1320, 800), SplitterDistance = 960, Panel1MinSize = 500, Panel2MinSize = 250 };
        inner.Panel1.Controls.Add(centerSplit);
        inner.Panel2.Controls.Add(rightTabs);
        var outer = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1500, 800), SplitterDistance = 290, Panel1MinSize = 210 };
        outer.Panel1.Controls.Add(left);
        outer.Panel2.Controls.Add(inner);
        return outer;
    }

    private Control BuildSlotPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(panel, "Name", _assetName);
        AddRow(panel, "Category", _assetCategory);
        AddRow(panel, "Source", _assetSource);
        AddRow(panel, "Status", _assetStatus);
        AddRow(panel, string.Empty, _assetLocked);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.AddRange(new Control[]
        {
            SmallButton("Apply", (_, _) => ApplySlotProperties()),
            SmallButton("Recommend", (_, _) => RecommendMetadata()),
            SmallButton("Clear", (_, _) => ClearSelectedSlot())
        });
        AddRow(panel, string.Empty, actions);
        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        host.Controls.Add(panel);
        return host;
    }

    private Control BuildTestMapPage()
    {
        _testLayer.DropDownStyle = ComboBoxStyle.DropDownList;
        _testLayer.Items.AddRange(Enum.GetNames<TestMapLayer>());
        _testLayer.SelectedItem = TestMapLayer.Lower1.ToString();
        _testLayer.SelectedIndexChanged += (_, _) =>
        {
            if (_testLayer.SelectedItem is string text && Enum.TryParse<TestMapLayer>(text, out var layer)) _testMap.ActiveLayer = layer;
        };
        _testPalette.Dock = DockStyle.Fill;
        _testPalette.SelectedIndexChanged += (_, _) =>
        {
            if (_testPalette.SelectedItem is TestPaletteItem item) _testMap.SelectedTileAddress = item.Address;
        };
        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 39, Padding = new Padding(4) };
        var grid = new CheckBox { Text = "Grid", Checked = true, AutoSize = true };
        var collision = new CheckBox { Text = "Collision", AutoSize = true };
        var player = new CheckBox { Text = "Place player", AutoSize = true };
        grid.CheckedChanged += (_, _) => { _testMap.ShowGrid = grid.Checked; _testMap.Invalidate(); };
        collision.CheckedChanged += (_, _) => { _testMap.ShowCollision = collision.Checked; _testMap.Invalidate(); };
        player.CheckedChanged += (_, _) => _testMap.PlayerMode = player.Checked;
        controls.Controls.Add(new Label { Text = "Layer:", AutoSize = true, Margin = new Padding(4, 8, 2, 2) });
        controls.Controls.Add(_testLayer);
        controls.Controls.Add(grid);
        controls.Controls.Add(collision);
        controls.Controls.Add(player);
        controls.Controls.Add(SmallButton("Clear Layer", (_, _) => ClearTestLayer()));
        controls.Controls.Add(SmallButton("Standard Scene", (_, _) => GenerateStandardTestMap()));
        var mapHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(30, 30, 32) };
        mapHost.Controls.Add(_testMap);
        _testMap.Location = Point.Empty;
        _testMap.MapChanged += (_, _) => MarkDefinitionDirty("Test map changed.");
        _testMap.StatusChanged += (_, text) => _status.Text = text;
        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_testPalette);
        left.Controls.Add(new Label { Text = "Tiles", Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4) });
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1400, 760), SplitterDistance = 270, Panel1MinSize = 190 };
        split.Panel1.Controls.Add(left);
        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(mapHost);
        right.Controls.Add(controls);
        split.Panel2.Controls.Add(right);
        return split;
    }

    private Control BuildCoveragePage()
    {
        _coverage.Dock = DockStyle.Fill;
        _coverage.ReadOnly = true;
        _coverage.AllowUserToAddRows = false;
        _coverage.AllowUserToDeleteRows = false;
        _coverage.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _coverage.RowHeadersVisible = false;
        _coverage.Columns.Add("Level", "Level");
        _coverage.Columns.Add("Requirement", "Requirement");
        _coverage.Columns.Add("Found", "Found");
        _coverage.Columns.Add("Status", "Status");
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(5) };
        var template = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        template.Items.AddRange(Enum.GetNames<TilesetTemplateKind>());
        template.SelectedItem = _definition.TemplateKind.ToString();
        template.SelectedIndexChanged += (_, _) =>
        {
            if (template.SelectedItem is string text && Enum.TryParse<TilesetTemplateKind>(text, out var kind))
            {
                _definition.TemplateKind = kind;
                MarkDefinitionDirty("Construction profile changed.");
                RefreshCoverage();
            }
        };
        top.Controls.Add(new Label { Text = "Profile:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        top.Controls.Add(template);
        top.Controls.Add(SmallButton("Refresh", (_, _) => RefreshCoverage()));
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(_coverage);
        panel.Controls.Add(top);
        return panel;
    }

    private Control BuildAnalysisPage()
    {
        _analysis.Dock = DockStyle.Fill;
        _analysis.Font = new Font(FontFamily.GenericMonospace, 10f);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(5) };
        top.Controls.AddRange(new Control[]
        {
            SmallButton("Style Report", (_, _) => AnalyzeStyle()),
            SmallButton("Duplicates", (_, _) => FindDuplicates()),
            SmallButton("Dependencies", (_, _) => AnalyzeDependencies()),
            SmallButton("Validate", (_, _) => ValidateTileset())
        });
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(_analysis);
        panel.Controls.Add(top);
        return panel;
    }

    private void BuildSheetTabs()
    {
        _sessions.Clear();
        _canvases.Clear();
        _sheetTabs.TabPages.Clear();
        foreach (var sheet in _tileSet.Sheets.OrderBy(x => x.Slot))
        {
            var session = _workspace.OpenSheet(sheet);
            _sessions[sheet.Slot] = session;
            var canvas = new TileSheetCanvas
            {
                SheetImage = session.Bitmap,
                Regions = MvLayoutCatalog.GetLogicalRegions(sheet.Slot),
                Zoom = 0.75,
                ShowGrid = true
            };
            canvas.SelectedRegionChanged += (_, _) => LoadSelectedSlot();
            canvas.DoubleClick += (_, _) => AssignSelectedAsset();
            _canvases[sheet.Slot] = canvas;
            var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(35, 35, 37) };
            host.Controls.Add(canvas);
            var page = new TabPage(sheet.Slot.ToString()) { Tag = sheet.Slot };
            page.Controls.Add(host);
            _sheetTabs.TabPages.Add(page);
        }
        _sheetTabs.SelectedIndexChanged += (_, _) => LoadSelectedSlot();
    }

    private void ConfigureLists()
    {
        _assetLibrary.View = View.Details;
        _assetLibrary.FullRowSelect = true;
        _assetLibrary.HideSelection = false;
        _assetLibrary.Columns.Add("Type", 72);
        _assetLibrary.Columns.Add("Name", 135);
        _assetLibrary.Columns.Add("Category", 95);
        _assetLibrary.Columns.Add("Status", 80);
        _assetLibrary.DoubleClick += (_, _) => AssignSelectedAsset();
        _assetLibrary.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) AssignSelectedAsset(); };
    }

    private void ConfigureIssueList()
    {
        _issues.Dock = DockStyle.Fill;
        _issues.View = View.Details;
        _issues.FullRowSelect = true;
        _issues.GridLines = true;
        _issues.Columns.Add("Severity", 80);
        _issues.Columns.Add("Sheet", 60);
        _issues.Columns.Add("Cell", 60);
        _issues.Columns.Add("Message", 780);
        _issues.DoubleClick += (_, _) => NavigateIssue();
    }

    private void RefreshAll()
    {
        RefreshAssetLibrary();
        RefreshTestPalette();
        RefreshCoverage();
        BindTestMap();
        LoadSelectedSlot();
        ValidateTileset();
    }

    private void RefreshAssetLibrary()
    {
        _assetLibrary.BeginUpdate();
        _assetLibrary.Items.Clear();
        var dependency = new TilesetDependencyService();
        foreach (var reference in _project.Autotiles.OrderBy(x => x.Name))
            AddLibraryItem(new AssetLibraryItem("Autotile", reference.Name, "A1–A4", reference.Id, reference, TilesetAssetStatus.Current));
        foreach (var reference in _project.Materials.OrderBy(x => x.Name))
            AddLibraryItem(new AssetLibraryItem("Material", reference.Name, "Procedural", reference.Id, reference, TilesetAssetStatus.Current));
        foreach (var obj in _definition.Objects.OrderBy(x => x.Category).ThenBy(x => x.Name))
            AddLibraryItem(new AssetLibraryItem("Object", obj.Name, obj.Category.ToString(), obj.Id, obj, obj.IsLocked ? TilesetAssetStatus.Locked : TilesetAssetStatus.Current));
        foreach (var binding in _definition.Assets.Where(x => dependency.GetStatus(_project, x) is TilesetAssetStatus.SourceChanged or TilesetAssetStatus.MissingSource))
            AddLibraryItem(new AssetLibraryItem("Binding", binding.Name, binding.Category, binding.Id, binding, dependency.GetStatus(_project, binding)));
        _assetLibrary.EndUpdate();
    }

    private void AddLibraryItem(AssetLibraryItem item)
    {
        var row = new ListViewItem(new[] { item.Type, item.Name, item.Category, item.Status.ToString() }) { Tag = item };
        if (item.Status == TilesetAssetStatus.MissingSource) row.ForeColor = Color.Firebrick;
        else if (item.Status == TilesetAssetStatus.SourceChanged) row.ForeColor = Color.DarkOrange;
        _assetLibrary.Items.Add(row);
    }

    private void RefreshTestPalette()
    {
        _testPalette.BeginUpdate();
        _testPalette.Items.Clear();
        foreach (var binding in _definition.Assets.Where(x => !string.IsNullOrWhiteSpace(x.Name)).OrderBy(x => x.Sheet).ThenBy(x => x.LogicalSlotIndex))
            _testPalette.Items.Add(new TestPaletteItem(binding.Name, TileAddressCodec.Encode(binding.Sheet, binding.LogicalSlotIndex)));
        _testPalette.EndUpdate();
    }

    private void BindTestMap() => _testMap.Bind(_definition.TestMap, _definition,
        _sessions.ToDictionary(x => x.Key, x => x.Value.Bitmap));

    private void LoadSelectedSlot()
    {
        if (!TryGetCurrentSelection(out var slot, out var canvas, out var index))
        {
            _metadataEditor.SelectedMetadata = null;
            return;
        }
        _loadingSelection = true;
        try
        {
            var binding = _definition.Assets.FirstOrDefault(x => x.Sheet == slot && x.LogicalSlotIndex == index);
            _assetName.Text = binding?.Name ?? string.Empty;
            _assetCategory.Text = binding?.Category ?? string.Empty;
            _assetLocked.Checked = binding?.IsLocked == true;
            _assetSource.Text = binding is null ? "Unassigned" : $"{binding.SourceKind}: {binding.SourceAssetId?.ToString("N")[..8] ?? "manual"}";
            _assetStatus.Text = binding is null ? "Empty" : new TilesetDependencyService().GetStatus(_project, binding).ToString();
            var metadata = new TilesetConstructionService().GetOrCreateMetadata(_definition, slot, index);
            _metadataEditor.SelectedMetadata = metadata.Metadata;
            _metadataEditor.MetadataChanged -= MetadataEditorOnMetadataChanged;
            _metadataEditor.MetadataChanged += MetadataEditorOnMetadataChanged;
            _status.Text = $"{slot} slot {index + 1} selected.";
            canvas.Invalidate();
        }
        finally { _loadingSelection = false; }
    }

    private void MetadataEditorOnMetadataChanged(object? sender, EventArgs e)
    {
        if (_loadingSelection) return;
        MarkDefinitionDirty("Gameplay metadata changed.");
    }

    private void ApplySlotProperties()
    {
        if (!TryGetCurrentSelection(out var slot, out _, out var index)) return;
        var binding = _definition.Assets.FirstOrDefault(x => x.Sheet == slot && x.LogicalSlotIndex == index);
        if (binding is null)
        {
            binding = new TilesetAssetBinding { Sheet = slot, LogicalSlotIndex = index, SourceKind = TilesetAssetSourceKind.ManualArtwork };
            _definition.Assets.Add(binding);
        }
        binding.Name = _assetName.Text.Trim();
        binding.Category = _assetCategory.Text.Trim();
        binding.IsLocked = _assetLocked.Checked;
        var sheet = _tileSet.Sheets.First(x => x.Slot == slot);
        var region = sheet.Regions.First(x => x.Index == index);
        region.Name = binding.Name;
        region.Tags = binding.Category.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        region.IsLocked = binding.IsLocked;
        MarkDefinitionDirty("Slot properties changed.");
        RefreshAssetLibrary();
        RefreshTestPalette();
    }

    private void ImportObject()
    {
        using var dialog = new OpenFileDialog { Filter = "PNG images (*.png)|*.png|Images (*.png;*.bmp;*.jpg)|*.png;*.bmp;*.jpg", Multiselect = true, Title = "Import Ordinary Tile Objects" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var path in dialog.FileNames)
        {
            var obj = new OrdinaryTileService().ImportObject(_project, path);
            _definition.Objects.Add(obj);
        }
        MarkDefinitionDirty($"Imported {dialog.FileNames.Length} object(s).");
        RefreshAssetLibrary();
    }

    private void EditSelectedObject()
    {
        var obj = SelectedLibrary()?.Payload as TileObjectDefinition;
        if (obj is null) { MessageBox.Show(this, "Select an ordinary object in the asset library.", "Object Editor", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var editor = new ObjectEditorForm(_project, obj);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        MarkDefinitionDirty("Object definition changed.");
        RefreshAssetLibrary();
    }

    private void CreateObjectVariation()
    {
        var source = SelectedLibrary()?.Payload as TileObjectDefinition;
        if (source is null) { MessageBox.Show(this, "Select an object first."); return; }
        var sourcePath = ProjectPath.Resolve(_project, source.RelativeImagePath);
        using var bitmap = BitmapFile.LoadEditable(sourcePath);
        var seed = Random.Shared.Next();
        using var variation = new OrdinaryTileService().CreateVariation(bitmap, Random.Shared.Next(-24, 25), 0.9f + (float)Random.Shared.NextDouble() * 0.25f, 0.9f + (float)Random.Shared.NextDouble() * 0.2f, true, seed);
        var destinationName = source.Name + " Variation";
        var directory = Path.Combine(_project.ProjectDirectory, "Objects", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "object.png");
        BitmapFile.SavePngAtomic(variation, path);
        var copy = new TileObjectDefinition
        {
            Name = destinationName,
            Category = source.Category,
            GridWidth = source.GridWidth,
            GridHeight = source.GridHeight,
            AnchorCellX = source.AnchorCellX,
            AnchorCellY = source.AnchorCellY,
            CollisionCellX = source.CollisionCellX,
            CollisionCellY = source.CollisionCellY,
            CollisionCellWidth = source.CollisionCellWidth,
            CollisionCellHeight = source.CollisionCellHeight,
            RelativeImagePath = ProjectPath.MakeRelative(_project, path),
            Metadata = source.Metadata.Clone(),
            Tags = source.Tags.Concat(new[] { "variation" }).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
        var group = source.VariationGroupId.HasValue ? _definition.VariationGroups.FirstOrDefault(x => x.Id == source.VariationGroupId) : null;
        if (group is null)
        {
            group = new TileVariationGroup { Name = source.Name + " Family" };
            group.Entries.Add(new TileVariationEntry { ObjectId = source.Id });
            _definition.VariationGroups.Add(group);
            source.VariationGroupId = group.Id;
        }
        copy.VariationGroupId = group.Id;
        group.Entries.Add(new TileVariationEntry { ObjectId = copy.Id });
        _definition.Objects.Add(copy);
        MarkDefinitionDirty("Created object variation.");
        RefreshAssetLibrary();
    }

    private void AssignSelectedAsset()
    {
        var item = SelectedLibrary();
        if (item is null || !TryGetCurrentSelection(out var slot, out var canvas, out var index)) return;
        try
        {
            switch (item.Payload)
            {
                case AutotileAssetReference autotile:
                    AssignAutotile(autotile, slot, index);
                    break;
                case MaterialAssetReference material:
                    AssignMaterial(material, slot, index);
                    break;
                case TileObjectDefinition obj:
                    AssignObject(obj, slot, index);
                    break;
                case TilesetAssetBinding binding:
                    NavigateTo(binding.Sheet, binding.LogicalSlotIndex);
                    break;
            }
            canvas.Invalidate();
            RefreshAllAfterEdit("Asset assigned.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Assign Asset", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AssignAutotile(AutotileAssetReference reference, MvSheetSlot slot, int index)
    {
        var definition = AutotileSerializer.Load(_project, reference);
        if (!AutotileKindInfo.IsCompatible(definition.Kind, slot, index)) throw new InvalidOperationException($"{definition.Kind} is not compatible with {slot} slot {index + 1}.");
        var session = _sessions[slot];
        session.CaptureBeforeEdit();
        using var source = AutotileSerializer.LoadSource(_project, definition);
        var region = _tileSet.Sheets.First(x => x.Slot == slot).Regions.First(x => x.Index == index);
        var result = new MvAutotileCompiler().CompileIntoSheet(session.Bitmap, region, slot, index, definition, source);
        session.MarkEdited();
        new TilesetConstructionService().AssignBinding(_definition, slot, index, TilesetAssetSourceKind.AutotileDefinition,
            reference.Id, reference.Revision, result.SourceSha256, result.OutputSha256, definition.Name, string.Join(", ", definition.Tags));
        AutotileSerializer.Save(_project, definition, source, reference.RelativeDefinitionPath);
    }

    private void AssignMaterial(MaterialAssetReference reference, MvSheetSlot slot, int index)
    {
        var recipe = MaterialSerializer.Load(_project, reference);
        var mode = InferMaterialMode(recipe.Name + " " + string.Join(' ', recipe.Tags));
        var kind = mode switch
        {
            MaterialAdapterMode.Wall or MaterialAdapterMode.Cliff => AutotileKind.A4Wall,
            MaterialAdapterMode.Roof => AutotileKind.A3Building,
            MaterialAdapterMode.AnimatedWater or MaterialAdapterMode.Shoreline => AutotileKind.A1Water,
            MaterialAdapterMode.Waterfall => AutotileKind.A1Waterfall,
            _ => AutotileKind.A2Floor
        };
        if (!AutotileKindInfo.IsCompatible(kind, slot, index)) throw new InvalidOperationException($"The inferred {kind} output is not compatible with {slot} slot {index + 1}.");
        var frames = kind is AutotileKind.A1Water or AutotileKind.A1Waterfall ? 3 : 1;
        using var result = new MaterialRenderService().Render(recipe, 96, 96, recipe.Seed, MaterialQuality.Final, frameCount: frames);
        var created = new MaterialAutotileAdapter().CreateAutotile(recipe, result, new MaterialAutotileSettings { Mode = mode });
        using var source = created.Source;
        var session = _sessions[slot];
        session.CaptureBeforeEdit();
        var region = _tileSet.Sheets.First(x => x.Slot == slot).Regions.First(x => x.Index == index);
        var compile = new MvAutotileCompiler().CompileIntoSheet(session.Bitmap, region, slot, index, created.Definition, source);
        region.SourceAutotileId = null;
        region.SourceAutotileRevision = 0;
        session.MarkEdited();
        new TilesetConstructionService().AssignBinding(_definition, slot, index, TilesetAssetSourceKind.MaterialRecipe,
            reference.Id, reference.Revision, compile.SourceSha256, compile.OutputSha256, recipe.Name, string.Join(", ", recipe.Tags));
    }

    private void AssignObject(TileObjectDefinition obj, MvSheetSlot slot, int index)
    {
        if (slot is not (MvSheetSlot.A5 or MvSheetSlot.B or MvSheetSlot.C or MvSheetSlot.D or MvSheetSlot.E))
            throw new InvalidOperationException("Ordinary objects can only be placed in A5 or B–E.");
        var columns = slot == MvSheetSlot.A5 ? 8 : 16;
        var rows = slot == MvSheetSlot.A5 ? 16 : 16;
        var startX = index % columns;
        var startY = index / columns;
        if (startX + obj.GridWidth > columns || startY + obj.GridHeight > rows)
            throw new InvalidOperationException("The multi-tile object does not fit from the selected cell.");
        var session = _sessions[slot];
        session.CaptureBeforeEdit();
        using var image = BitmapFile.LoadEditable(ProjectPath.Resolve(_project, obj.RelativeImagePath));
        using var graphics = Graphics.FromImage(session.Bitmap);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.DrawImage(image, new Rectangle(startX * 48, startY * 48, obj.GridWidth * 48, obj.GridHeight * 48),
            new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        for (var oy = 0; oy < obj.GridHeight; oy++)
        for (var ox = 0; ox < obj.GridWidth; ox++)
        {
            var cell = (startY + oy) * columns + startX + ox;
            new TilesetConstructionService().AssignBinding(_definition, slot, cell, TilesetAssetSourceKind.ObjectDefinition,
                obj.Id, 1, string.Empty, string.Empty, obj.GridWidth * obj.GridHeight == 1 ? obj.Name : $"{obj.Name} [{ox + 1},{oy + 1}]", obj.Category.ToString());
            var metadata = new TilesetConstructionService().GetOrCreateMetadata(_definition, slot, cell);
            metadata.Metadata = obj.Metadata.Clone();
        }
        session.MarkEdited();
    }

    private void AutoPack()
    {
        if (_definition.Objects.Count == 0) { MessageBox.Show(this, "Import at least one ordinary object first."); return; }
        var sheets = _sessions.ToDictionary(x => x.Key, x => x.Value.Bitmap);
        var plan = new TilesetPacker().CreatePlan(_definition, sheets);
        if (plan.Placements.Count == 0) { MessageBox.Show(this, string.Join(Environment.NewLine, plan.Warnings.DefaultIfEmpty("No objects could be packed."))); return; }
        var message = $"Place {plan.Placements.Count} object(s), occupying {plan.OccupiedCells} cells?";
        if (plan.UnplacedObjectIds.Count > 0) message += $"\n{plan.UnplacedObjectIds.Count} object(s) will remain unplaced.";
        if (MessageBox.Show(this, message, "Auto Pack", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        CreateAutomaticSnapshot("Before automatic packing");
        foreach (var slot in plan.Placements.Select(x => x.Sheet).Distinct()) _sessions[slot].CaptureBeforeEdit();
        new TilesetPacker().ApplyPlan(_definition, plan, sheets, obj => BitmapFile.LoadEditable(ProjectPath.Resolve(_project, obj.RelativeImagePath)));
        foreach (var slot in plan.Placements.Select(x => x.Sheet).Distinct()) { _sessions[slot].MarkEdited(); _canvases[slot].Invalidate(); }
        RefreshAllAfterEdit("Objects packed.");
    }

    private void AssistedBuild()
    {
        using var dialog = new Form { Text = "Build Tileset from Theme", StartPosition = FormStartPosition.CenterParent, Width = 440, Height = 300, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var template = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        template.Items.AddRange(Enum.GetNames<TilesetTemplateKind>());
        template.SelectedItem = _definition.TemplateKind.ToString();
        var emptyOnly = new CheckBox { Text = "Fill empty slots only", Checked = true, AutoSize = true };
        var autotiles = new CheckBox { Text = "Use saved autotiles", Checked = true, AutoSize = true };
        var materials = new CheckBox { Text = "Use saved materials", Checked = true, AutoSize = true };
        var seed = new NumericUpDown { Minimum = int.MinValue, Maximum = int.MaxValue, Value = 12345, Width = 130 };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(14), WrapContents = false };
        panel.Controls.Add(new Label { Text = "Construction profile", AutoSize = true }); panel.Controls.Add(template); panel.Controls.Add(emptyOnly); panel.Controls.Add(autotiles); panel.Controls.Add(materials); panel.Controls.Add(new Label { Text = "Seed", AutoSize = true }); panel.Controls.Add(seed);
        var ok = new Button { Text = "Build", DialogResult = DialogResult.OK, AutoSize = true }; var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) }; buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        dialog.Controls.Add(panel); dialog.Controls.Add(buttons); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        CreateAutomaticSnapshot("Before assisted theme build");
        foreach (var session in _sessions.Values.Where(x => x.Definition.Slot is MvSheetSlot.A1 or MvSheetSlot.A2 or MvSheetSlot.A3 or MvSheetSlot.A4)) session.CaptureBeforeEdit();
        var request = new AssistedTilesetBuildRequest
        {
            TemplateKind = Enum.TryParse<TilesetTemplateKind>(template.SelectedItem?.ToString(), out var parsed) ? parsed : TilesetTemplateKind.Outdoor,
            FillEmptySlotsOnly = emptyOnly.Checked,
            IncludeSavedAutotiles = autotiles.Checked,
            IncludeSavedMaterials = materials.Checked,
            Seed = (int)seed.Value
        };
        var result = new AssistedTilesetBuilder().Build(_project, _tileSet, _definition,
            _sessions.ToDictionary(x => x.Key, x => x.Value.Bitmap), request);
        foreach (var session in _sessions.Values.Where(x => x.Definition.Slot is MvSheetSlot.A1 or MvSheetSlot.A2 or MvSheetSlot.A3 or MvSheetSlot.A4)) session.MarkEdited();
        foreach (var canvas in _canvases.Values) canvas.Invalidate();
        RefreshAllAfterEdit($"Theme build placed {result.AutotilesPlaced} autotiles and {result.MaterialsPlaced} materials.");
        if (result.Warnings.Count > 0) MessageBox.Show(this, string.Join(Environment.NewLine, result.Warnings), "Theme Build Warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void RecommendMetadata()
    {
        if (!TryGetCurrentSelection(out var slot, out _, out var index)) return;
        var binding = _definition.Assets.FirstOrDefault(x => x.Sheet == slot && x.LogicalSlotIndex == index);
        var cell = new TilesetConstructionService().GetOrCreateMetadata(_definition, slot, index);
        var recommendation = new MetadataRecommendationService().Recommend(binding?.Name ?? _assetName.Text, (binding?.Category ?? _assetCategory.Text).Split(','));
        var changes = new MetadataRecommendationService().DescribeDifferences(cell.Metadata, recommendation);
        if (changes.Count == 0) { MessageBox.Show(this, "The current metadata already matches the recommendation."); return; }
        if (MessageBox.Show(this, "Apply these recommendations?\n\n" + string.Join("\n", changes.Select(x => "• " + x)), "Metadata Recommendation", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        cell.Metadata = recommendation;
        _metadataEditor.SelectedMetadata = cell.Metadata;
        MarkDefinitionDirty("Metadata recommendation applied.");
    }

    private void ClearSelectedSlot()
    {
        if (!TryGetCurrentSelection(out var slot, out var canvas, out var index)) return;
        var binding = _definition.Assets.FirstOrDefault(x => x.Sheet == slot && x.LogicalSlotIndex == index);
        if (binding?.IsLocked == true) { MessageBox.Show(this, "This slot is locked."); return; }
        if (MessageBox.Show(this, $"Clear {slot} slot {index + 1}?", "Clear Slot", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var session = _sessions[slot];
        session.CaptureBeforeEdit();
        new TilesetConstructionService().ClearSlot(_definition, session.Bitmap, slot, index);
        session.MarkEdited();
        canvas.Invalidate();
        RefreshAllAfterEdit("Slot cleared.");
    }

    private void UndoSheet()
    {
        if (!TryGetCurrentSlot(out var slot) || !_sessions[slot].Undo()) return;
        _canvases[slot].SheetImage = _sessions[slot].Bitmap;
        _status.Text = "Sheet edit undone.";
    }

    private void RedoSheet()
    {
        if (!TryGetCurrentSlot(out var slot) || !_sessions[slot].Redo()) return;
        _canvases[slot].SheetImage = _sessions[slot].Bitmap;
        _status.Text = "Sheet edit redone.";
    }

    private void ValidateTileset()
    {
        var issues = new TilesetValidator().Validate(_project, _tileSet, _definition, _sessions.ToDictionary(x => x.Key, x => x.Value.Bitmap));
        _issues.BeginUpdate(); _issues.Items.Clear();
        foreach (var issue in issues)
        {
            var row = new ListViewItem(new[] { issue.Severity.ToString(), issue.Sheet?.ToString() ?? string.Empty, issue.CellIndex.HasValue ? (issue.CellIndex.Value + 1).ToString() : string.Empty, issue.Message ?? string.Empty }) { Tag = issue };
            row.ForeColor = issue.Severity switch { TilesetIssueSeverity.Error => Color.Firebrick, TilesetIssueSeverity.Warning => Color.DarkOrange, _ => SystemColors.WindowText };
            _issues.Items.Add(row);
        }
        _issues.EndUpdate();
        _status.Text = $"Validation: {issues.Count(x => x.Severity == TilesetIssueSeverity.Error)} errors, {issues.Count(x => x.Severity == TilesetIssueSeverity.Warning)} warnings.";
        RefreshCoverage();
    }

    private void RefreshCoverage()
    {
        var report = new CompletenessAnalyzer().Analyze(_definition, new ConstructionTemplateCatalog().Get(_definition.TemplateKind));
        _coverage.Rows.Clear();
        foreach (var item in report.Coverage)
            _coverage.Rows.Add(item.Requirement.Level, item.Requirement.Name, $"{item.FoundCount}/{item.Requirement.MinimumCount}", item.IsSatisfied ? "Complete" : "Missing");
        _score.Text = $"Completeness {report.Score:0.0}%";
    }

    private void AnalyzeStyle()
    {
        var report = new TilesetHarmonizer().Analyze(_sessions.ToDictionary(x => x.Key, x => x.Value.Bitmap));
        _analysis.Text = $"STYLE CONSISTENCY REPORT\r\n------------------------\r\nPalette consistency: {report.PaletteConsistency:0.0}%\r\nAverage saturation: {report.AverageSaturation:0.000}\r\nAverage luminance: {report.AverageLuminance:0.000}\r\nAverage contrast: {report.AverageContrast:0.000}\r\nTransparent fringe samples: {report.TransparentFringeCount}\r\nDetail outliers: {report.DetailOutlierCount}\r\n\r\n{string.Join("\r\n", report.Warnings.Select(x => "• " + x))}";
        _status.Text = "Style analysis complete.";
    }

    private void Harmonize()
    {
        var report = new TilesetHarmonizer().Analyze(_sessions.ToDictionary(x => x.Key, x => x.Value.Bitmap));
        if (MessageBox.Show(this, $"Normalize saturation, luminance, contrast, and alpha across all sheets?\n\nCurrent palette consistency: {report.PaletteConsistency:0.0}%\nThis operation is undoable per sheet.", "Harmonize Tileset", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        CreateAutomaticSnapshot("Before tileset harmonization");
        foreach (var session in _sessions.Values)
        {
            session.CaptureBeforeEdit();
            new TilesetHarmonizer().Normalize(session.Bitmap, (float)report.AverageSaturation, (float)report.AverageLuminance, 1.05f, true, false);
            session.MarkEdited();
            _canvases[session.Definition.Slot].Invalidate();
        }
        MarkDefinitionDirty("Tileset harmonized.");
        AnalyzeStyle();
    }

    private void FindDuplicates()
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            var matches = new DuplicateDetector().FindDuplicates(_sessions.ToDictionary(x => x.Key, x => x.Value.Bitmap));
            _analysis.Text = "DUPLICATE AND NEAR-DUPLICATE REPORT\r\n-----------------------------------\r\n" +
                             (matches.Count == 0 ? "No duplicates found." : string.Join("\r\n", matches.Take(500).Select(x => $"{x.FirstSheet} {x.FirstCell + 1} ↔ {x.SecondSheet} {x.SecondCell + 1}: {x.Similarity:P1}{(x.IsExact ? " exact" : string.Empty)}")));
            _status.Text = $"Found {matches.Count} duplicate or near-duplicate pairs.";
        }
        finally { Cursor = Cursors.Default; }
    }

    private void AnalyzeDependencies()
    {
        var statuses = new TilesetDependencyService().Analyze(_project, _definition);
        _analysis.Text = "DEPENDENCY REPORT\r\n-----------------\r\n" + string.Join("\r\n",
            _definition.Assets.OrderBy(x => x.Sheet).ThenBy(x => x.LogicalSlotIndex)
                .Select(x => $"{x.Sheet} {x.LogicalSlotIndex + 1,-4} {statuses[x.Id],-18} {x.Name}"));
    }

    private void ExportPackage()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose the publication package destination", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SaveAll();
            var destination = Path.Combine(dialog.SelectedPath, ProjectPath.SanitizeFileName(_tileSet.Name));
            var result = new TilesetPublicationExporter().Export(_project, _tileSet, _definition, destination,
                _sessions.ToDictionary(x => x.Key, x => x.Value.Bitmap));
            MessageBox.Show(this, $"Exported {result.ExportedFiles.Count} files to:\n{destination}", "Tileset Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Tileset Export", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ExportShowcase()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose the showcase scene destination", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var map = new ShowcaseSceneBuilder().Build(_definition);
        using var beauty = new FullTestMapRenderer().Render(map, _sessions.ToDictionary(x => x.Key, x => x.Value.Bitmap), _definition, false, false);
        var result = new ShowcaseSceneBuilder().Export(dialog.SelectedPath, map, beauty, _definition);
        MessageBox.Show(this, $"Showcase scene exported to:\n{dialog.SelectedPath}\n\n{Path.GetFileName(result.BeautyPath)}", "Showcase", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void PatchMvProject()
    {
        using var folder = new FolderBrowserDialog { Description = "Select an RPG Maker MV project folder", UseDescriptionForTitle = true };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SaveAll();
            var service = new MvProjectIntegrationService();
            var inspection = service.Inspect(folder.SelectedPath);
            var choice = ChooseMvRecord(inspection);
            if (choice.Cancelled) return;
            var result = service.Patch(_project, _tileSet, _definition, folder.SelectedPath, choice.RecordId, choice.CreateNew, overwriteImages: true);
            MessageBox.Show(this, $"RPG Maker MV project updated.\nTileset record: {result.RecordId}\nBackup: {result.BackupDirectory}", "MV Project Integration", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "MV Project Integration", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private (bool Cancelled, bool CreateNew, int? RecordId) ChooseMvRecord(MvProjectInspection inspection)
    {
        using var dialog = new Form { Text = "Choose RPG Maker MV Tileset Record", StartPosition = FormStartPosition.CenterParent, Width = 500, Height = 210, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var create = new RadioButton { Text = "Create a new tileset record", Checked = true, AutoSize = true };
        var update = new RadioButton { Text = "Update existing record", AutoSize = true };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 390, Enabled = false };
        combo.Items.AddRange(inspection.Records.Cast<object>().ToArray());
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        update.CheckedChanged += (_, _) => combo.Enabled = update.Checked;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12), WrapContents = false };
        panel.Controls.Add(create); panel.Controls.Add(update); panel.Controls.Add(combo);
        var ok = new Button { Text = "Continue", DialogResult = DialogResult.OK, AutoSize = true }; var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) }; buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        dialog.Controls.Add(panel); dialog.Controls.Add(buttons); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return (true, false, null);
        return (false, create.Checked, update.Checked ? (combo.SelectedItem as MvTilesetRecordSummary)?.Id : null);
    }

    private void CreateSnapshot()
    {
        var name = Prompt("Snapshot name", $"Snapshot {DateTime.Now:yyyy-MM-dd HH-mm}");
        if (name is null) return;
        SaveAll();
        var record = new TilesetSnapshotService().CreateSnapshot(_project, _tileSet, _definition, name, "Manual snapshot");
        TilesetSerializer.Save(_project, _reference, _definition);
        MessageBox.Show(this, $"Snapshot created:\n{record.Name}", "Snapshot", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RestoreSnapshot()
    {
        if (_definition.Snapshots.Count == 0)
        {
            MessageBox.Show(this, "This construction has no snapshots.", "Restore Snapshot", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new Form { Text = "Restore Tileset Snapshot", StartPosition = FormStartPosition.CenterParent, Width = 620, Height = 370, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = nameof(TilesetSnapshotRecord.Name) };
        foreach (var snapshot in _definition.Snapshots.OrderByDescending(x => x.CreatedUtc)) list.Items.Add(snapshot);
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        var details = new Label { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(8), AutoEllipsis = true };
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is TilesetSnapshotRecord item) details.Text = $"{item.CreatedUtc.LocalDateTime:g} — {item.Reason}";
        };
        if (list.SelectedItem is TilesetSnapshotRecord initial) details.Text = $"{initial.CreatedUtc.LocalDateTime:g} — {initial.Reason}";
        var restore = new Button { Text = "Restore", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.Add(restore); buttons.Controls.Add(cancel);
        dialog.Controls.Add(list); dialog.Controls.Add(details); dialog.Controls.Add(buttons); dialog.AcceptButton = restore; dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK || list.SelectedItem is not TilesetSnapshotRecord selected) return;
        if (MessageBox.Show(this, $"Restore snapshot '{selected.Name}'?\n\nThe current state will first be preserved as an automatic snapshot.", "Restore Snapshot", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        CreateAutomaticSnapshot("Before restoring snapshot");
        var currentSnapshots = _definition.Snapshots.ToList();
        var restored = new TilesetSnapshotService().RestoreSnapshot(_project, _tileSet, selected, restoreImages: false);
        restored.Snapshots = currentSnapshots;
        restored.Id = _definition.Id;
        restored.TileSetId = _tileSet.Id;
        var snapshotDirectory = ProjectPath.Resolve(_project, selected.RelativeDirectory);
        foreach (var sheet in _tileSet.Sheets)
        {
            var imagePath = Path.Combine(snapshotDirectory, sheet.FileName);
            if (!File.Exists(imagePath) || !_sessions.TryGetValue(sheet.Slot, out var session)) continue;
            using var image = BitmapFile.LoadEditable(imagePath);
            session.ReplaceBitmap(image);
            _canvases[sheet.Slot].SheetImage = session.Bitmap;
        }
        _definition = restored;
        TilesetSerializer.Save(_project, _reference, _definition);
        RefreshAllAfterEdit($"Restored snapshot: {selected.Name}");
    }

    private void CreateAutomaticSnapshot(string reason)
    {
        SaveAll();
        new TilesetSnapshotService().CreateSnapshot(_project, _tileSet, _definition,
            $"Automatic {DateTime.Now:yyyy-MM-dd HH-mm-ss}", reason);
        TilesetSerializer.Save(_project, _reference, _definition);
        _workspace.MarkProjectDirty();
    }

    private void GenerateStandardTestMap()
    {
        _definition.TestMap = new ShowcaseSceneBuilder().Build(_definition, 20, 15);
        BindTestMap();
        MarkDefinitionDirty("Standard test map generated.");
    }

    private void ClearTestLayer()
    {
        if (_testLayer.SelectedItem is not string text || !Enum.TryParse<TestMapLayer>(text, out var layer)) return;
        _definition.TestMap.Layers[layer] = Enumerable.Repeat(-1, _definition.TestMap.Width * _definition.TestMap.Height).ToArray();
        _testMap.Invalidate();
        MarkDefinitionDirty("Test map layer cleared.");
    }

    private void SaveAll()
    {
        ApplySlotProperties();
        TilesetSerializer.Save(_project, _reference, _definition);
        _workspace.MarkProjectDirty();
        _workspace.Save();
        _status.Text = "Tileset construction saved.";
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_workspace.HasUnsavedChanges) return;
        var choice = MessageBox.Show(this, "Save tileset construction changes before closing?", "Playroma Tile", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) { e.Cancel = true; return; }
        if (choice == DialogResult.Yes)
        {
            try { SaveAll(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error); e.Cancel = true; }
        }
    }

    private void OnStudioKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F9) { ValidateTileset(); e.Handled = true; }
    }

    private void RefreshAllAfterEdit(string message)
    {
        MarkDefinitionDirty(message);
        RefreshAssetLibrary();
        RefreshTestPalette();
        RefreshCoverage();
        BindTestMap();
        LoadSelectedSlot();
    }

    private void MarkDefinitionDirty(string message)
    {
        _definition.ModifiedUtc = DateTimeOffset.UtcNow;
        _workspace.MarkProjectDirty();
        _status.Text = message;
    }

    private void NavigateIssue()
    {
        if (_issues.SelectedItems.Count == 0 || _issues.SelectedItems[0].Tag is not TilesetIssue issue || !issue.Sheet.HasValue || !issue.CellIndex.HasValue) return;
        NavigateTo(issue.Sheet.Value, issue.CellIndex.Value);
    }

    private void NavigateTo(MvSheetSlot sheet, int index)
    {
        var page = _sheetTabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Tag is MvSheetSlot value && value == sheet);
        if (page is null) return;
        _sheetTabs.SelectedTab = page;
        _canvases[sheet].SelectRegionByLogicalIndex(index);
    }

    private bool TryGetCurrentSelection(out MvSheetSlot slot, out TileSheetCanvas canvas, out int index)
    {
        if (TryGetCurrentSlot(out slot) && _canvases.TryGetValue(slot, out canvas!) && canvas.SelectedRegion is { } region)
        {
            index = region.Index;
            return true;
        }
        canvas = null!;
        index = -1;
        return false;
    }

    private bool TryGetCurrentSlot(out MvSheetSlot slot)
    {
        if (_sheetTabs.SelectedTab?.Tag is MvSheetSlot value) { slot = value; return true; }
        slot = default;
        return false;
    }

    private AssetLibraryItem? SelectedLibrary() => _assetLibrary.SelectedItems.Count > 0 ? _assetLibrary.SelectedItems[0].Tag as AssetLibraryItem : null;

    private static MaterialAdapterMode InferMaterialMode(string text)
    {
        text = text.ToLowerInvariant();
        if (text.Contains("waterfall")) return MaterialAdapterMode.Waterfall;
        if (text.Contains("water") || text.Contains("ocean") || text.Contains("lava")) return MaterialAdapterMode.AnimatedWater;
        if (text.Contains("wall") || text.Contains("cliff")) return MaterialAdapterMode.Wall;
        if (text.Contains("roof") || text.Contains("shingle")) return MaterialAdapterMode.Roof;
        if (text.Contains("overlay") || text.Contains("flower") || text.Contains("decoration")) return MaterialAdapterMode.TransparentDecoration;
        return MaterialAdapterMode.UniformFloor;
    }

    private static string? Prompt(string title, string initial)
    {
        using var form = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, Width = 420, Height = 145, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var text = new TextBox { Text = initial, Dock = DockStyle.Top, Margin = new Padding(10) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true }; var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) }; buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) }; panel.Controls.Add(text);
        form.Controls.Add(panel); form.Controls.Add(buttons); form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? text.Text.Trim() : null;
    }

    private static ToolStripMenuItem Item(string text, Keys shortcut, EventHandler handler, string? shortcutDisplay = null)
    {
        var item = new ToolStripMenuItem(text)
        {
            ShowShortcutKeys = shortcut != Keys.None || !string.IsNullOrWhiteSpace(shortcutDisplay)
        };

        if (shortcut != Keys.None)
        {
            item.ShortcutKeys = shortcut;
        }

        if (!string.IsNullOrWhiteSpace(shortcutDisplay))
        {
            item.ShortcutKeyDisplayString = shortcutDisplay;
        }

        item.Click += handler;
        return item;
    }
    private static ToolStripButton Button(string text, EventHandler handler) { var item = new ToolStripButton(text); item.Click += handler; return item; }
    private static Button SmallButton(string text, EventHandler handler) { var button = new Button { Text = text, AutoSize = true }; button.Click += handler; return button; }
    private static void AddRow(TableLayoutPanel panel, string label, Control control)
    {
        var row = panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, 0, row); control.Dock = DockStyle.Fill; panel.Controls.Add(control, 1, row);
    }

    private sealed record AssetLibraryItem(string Type, string Name, string Category, Guid Id, object Payload, TilesetAssetStatus Status);
    private sealed record TestPaletteItem(string Name, int Address) { public override string ToString() => Name; }
}
