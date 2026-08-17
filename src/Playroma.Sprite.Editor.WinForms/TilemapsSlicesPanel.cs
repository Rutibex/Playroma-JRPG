using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;
using Playroma.Sprite.Tilemaps;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.Editor.WinForms.Integration;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class TilemapsSlicesPanel : UserControl
{
    private readonly SpriteEditorSession session;
    private readonly string? projectRoot;
    private readonly Func<Rgba32> foregroundProvider;
    private readonly Func<IReadOnlyCollection<int>> selectedFramesProvider;
    private readonly ComboBox tilesetBox = DropDown();
    private readonly ComboBox modeBox = DropDown();
    private readonly ComboBox transformBox = DropDown();
    private readonly NumericUpDown tileWidth = Number(1, 512, 48);
    private readonly NumericUpDown tileHeight = Number(1, 512, 48);
    private readonly NumericUpDown tilesetMargin = Number(0, 512, 0);
    private readonly NumericUpDown tilesetSpacing = Number(0, 512, 0);
    private readonly NumericUpDown mapColumns = Number(1, 4096, 17);
    private readonly NumericUpDown mapRows = Number(1, 4096, 13);
    private readonly NumericUpDown zoomBox = Number(1, 16, 2);
    private readonly TilesetPaletteControl palette;
    private readonly TilemapGridControl map;
    private readonly TilePixelEditorControl pixelEditor;
    private readonly ListBox sliceList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox sliceKeyList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox sliceName = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown sliceX = Number(-32768, 32768, 0);
    private readonly NumericUpDown sliceY = Number(-32768, 32768, 0);
    private readonly NumericUpDown sliceW = Number(1, 32768, 32);
    private readonly NumericUpDown sliceH = Number(1, 32768, 32);
    private readonly CheckBox pivotEnabled = new() { Text = "Use pivot", AutoSize = true };
    private readonly NumericUpDown pivotX = Number(-32768, 32768, 0);
    private readonly NumericUpDown pivotY = Number(-32768, 32768, 0);
    private readonly CheckBox centerEnabled = new() { Text = "Use nine-slice center", AutoSize = true };
    private readonly NumericUpDown centerX = Number(-32768, 32768, 8);
    private readonly NumericUpDown centerY = Number(-32768, 32768, 8);
    private readonly NumericUpDown centerW = Number(1, 32768, 16);
    private readonly NumericUpDown centerH = Number(1, 32768, 16);
    private readonly NumericUpDown previewW = Number(1, 2048, 180);
    private readonly NumericUpDown previewH = Number(1, 2048, 120);
    private readonly SlicePreviewControl slicePreview;
    private readonly Label bridgeStatus = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), Text = "No active Tile Studio bridge." };
    private TileStudioBridgeRequest? lastBridge;
    private readonly List<int> stackTileIndexes = [];
    private bool refreshing;

    public TilemapsSlicesPanel(SpriteEditorSession session, string? projectRoot, Func<Rgba32> foregroundProvider, Func<IReadOnlyCollection<int>> selectedFramesProvider)
    {
        this.session = session;
        this.projectRoot = projectRoot;
        this.foregroundProvider = foregroundProvider;
        this.selectedFramesProvider = selectedFramesProvider;
        Dock = DockStyle.Fill;
        palette = new TilesetPaletteControl(session) { Dock = DockStyle.Fill };
        map = new TilemapGridControl(session) { Dock = DockStyle.Fill };
        pixelEditor = new TilePixelEditorControl(session) { Dock = DockStyle.Fill };
        slicePreview = new SlicePreviewControl(session) { Dock = DockStyle.Fill };
        Controls.Add(BuildTabs());
        modeBox.Items.AddRange(Enum.GetNames<SpriteTileEditMode>().Cast<object>().ToArray()); modeBox.SelectedItem = nameof(SpriteTileEditMode.Manual);
        transformBox.Items.AddRange(["None", "Flip Horizontal", "Flip Vertical", "Rotate 90", "Rotate 90 + Flip H", "Rotate 90 + Flip V"]); transformBox.SelectedIndex = 0;
        session.Changed += (_, _) => RefreshAll();
        tilesetBox.SelectedIndexChanged += (_, _) => { if (!refreshing) ApplyTilesetSelection(); };
        palette.SelectedTileChanged += (_, _) => { map.SelectedTileIndex = palette.SelectedTileIndex; pixelEditor.TileIndex = palette.SelectedTileIndex; pixelEditor.ForegroundColor = foregroundProvider(); pixelEditor.Invalidate(); };
        modeBox.SelectedIndexChanged += (_, _) => { if (!refreshing) ApplyMode(); };
        transformBox.SelectedIndexChanged += (_, _) => map.Transform = ResolveTransform();
        zoomBox.ValueChanged += (_, _) => { map.Zoom = (int)zoomBox.Value; map.Invalidate(); };
        sliceList.SelectedIndexChanged += (_, _) => LoadSelectedSlice();
        sliceKeyList.SelectedIndexChanged += (_, _) => LoadSelectedSliceKey();
        foreach (Control control in new Control[] { sliceName, sliceX, sliceY, sliceW, sliceH, pivotEnabled, pivotX, pivotY, centerEnabled, centerX, centerY, centerW, centerH, previewW, previewH })
            if (control is NumericUpDown numeric) numeric.ValueChanged += (_, _) => RefreshSlicePreview();
            else if (control is CheckBox check) check.CheckedChanged += (_, _) => RefreshSlicePreview();
            else if (control is TextBox text) text.TextChanged += (_, _) => RefreshSlicePreview();
        RefreshAll();
    }

    public void ActivateTilemapWorkspace() => ((TabControl)Controls[0]).SelectedIndex = 0;
    public void ActivateSliceWorkspace() => ((TabControl)Controls[0]).SelectedIndex = 1;

    public void PromptImportTileset()
    {
        using OpenFileDialog dialog = new() { Filter = "PNG Images|*.png", Multiselect = false, Title = "Import Reusable Tileset" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            PixelBuffer source = PngCodec.Load(dialog.FileName);
            string name = Path.GetFileNameWithoutExtension(dialog.FileName);
            session.ImportTileset(source, new TilesetImportOptions(name, (int)tileWidth.Value, (int)tileHeight.Value, (int)tilesetMargin.Value, (int)tilesetSpacing.Value, SourceImagePath: dialog.FileName));
            ActivateTilemapWorkspace();
        }
        catch (Exception exception) { ShowError("The tileset could not be imported.", exception); }
    }

    public void PromptAddTilemapLayer()
    {
        if (tilesetBox.SelectedItem is not TilesetChoice choice)
        {
            MessageBox.Show(this, "Import or select a tileset first.", "Tilemaps", MessageBoxButtons.OK, MessageBoxIcon.Information); return;
        }
        try { session.AddTilemapLayer(choice.Id, "Tilemap", (int)mapColumns.Value, (int)mapRows.Value); ActivateTilemapWorkspace(); }
        catch (Exception exception) { ShowError("The tilemap layer could not be created.", exception); }
    }

    public void PromptAddSliceFromSelection()
    {
        IntRect bounds = session.Selection.GetBounds() ?? new IntRect(0, 0, Math.Max(1, session.Document.Canvas.Width), Math.Max(1, session.Document.Canvas.Height));
        try { session.AddSlice("Slice", bounds); ActivateSliceWorkspace(); }
        catch (Exception exception) { ShowError("The slice could not be created.", exception); }
    }

    public void ExportSliceMetadata()
    {
        using SaveFileDialog dialog = new() { Filter = "JSON Files|*.json", FileName = Path.GetFileNameWithoutExtension(session.Document.Name) + ".slices.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, SliceDocumentOperations.ExportMetadata(session.Document)); }
        catch (Exception exception) { ShowError("Slice metadata could not be exported.", exception); }
    }

    private Control BuildTabs()
    {
        TabControl tabs = new() { Dock = DockStyle.Fill };
        TabPage tilemaps = new("Tilemaps"); tilemaps.Controls.Add(BuildTilemapPage());
        TabPage slices = new("Slices"); slices.Controls.Add(BuildSlicesPage());
        TabPage bridge = new("Tile Studio"); bridge.Controls.Add(BuildBridgePage());
        tabs.TabPages.AddRange([tilemaps, slices, bridge]); return tabs;
    }

    private Control BuildTilemapPage()
    {
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(4) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 38)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 42)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        FlowLayoutPanel controls = Flow();
        controls.Controls.AddRange([LabelFor("Tileset"), tilesetBox, LabelFor("Tile"), tileWidth, LabelFor("×"), tileHeight, LabelFor("Margin"), tilesetMargin, LabelFor("Spacing"), tilesetSpacing, Button("Import PNG", (_, _) => PromptImportTileset()), LabelFor("Map"), mapColumns, LabelFor("×"), mapRows, Button("+ Tilemap Layer", (_, _) => PromptAddTilemapLayer()), Button("Ensure Cel", (_, _) => Safe(() => session.EnsureSelectedTilemapCel(false))), Button("Linked Cel", (_, _) => Safe(() => session.EnsureSelectedTilemapCel(true))), Button("Unlink", (_, _) => Safe(session.UnlinkSelectedTilemapCel)), Button("Rasterize", (_, _) => Safe(() => session.RasterizeSelectedTilemap()))]);
        FlowLayoutPanel modes = Flow(); modes.Controls.AddRange([LabelFor("Mode"), modeBox, LabelFor("Transform"), transformBox, LabelFor("Zoom"), zoomBox, Button("Fill Region", (_, _) => Safe(() => map.FillSelection(CurrentBrush()))), Button("Flood Fill", (_, _) => Safe(() => map.FloodFill(CurrentBrush()))), Button("Clear", (_, _) => Safe(map.ClearSelection)), Button("Flip H", (_, _) => Safe(() => map.TransformSelection(SpriteTileTransform.FlipHorizontal))), Button("Flip V", (_, _) => Safe(() => map.TransformSelection(SpriteTileTransform.FlipVertical))), Button("Rotate", (_, _) => Safe(() => map.TransformSelection(SpriteTileTransform.Rotate90))), Button("Stack Brush", (_, _) => PromptStack()), Button("Auto Rules", (_, _) => GenerateAutoRules()), Button("Remap", (_, _) => PromptRemap())]);
        Panel top = new() { Dock = DockStyle.Fill, Height = 74 }; controls.Dock = DockStyle.Top; modes.Dock = DockStyle.Bottom; top.Controls.Add(modes); top.Controls.Add(controls);
        SplitContainer paletteAndMap = new() { Dock = DockStyle.Fill, SplitterDistance = 260 }; paletteAndMap.Panel1.Controls.Add(palette); paletteAndMap.Panel2.Controls.Add(map);
        GroupBox pixelGroup = new() { Text = "Pixel-edit selected reusable tile", Dock = DockStyle.Fill }; pixelGroup.Controls.Add(pixelEditor);
        root.Controls.Add(top, 0, 0); root.Controls.Add(paletteAndMap, 0, 1);
        root.Controls.Add(pixelGroup, 0, 2);
        Label hint = new() { Dock = DockStyle.Fill, Text = "Left-click paints tiles. Right-click erases. Shift-drag selects a rectangular tile region. Pixel mode edits the reusable tile below; Manual/Automatic/Stack modes edit the tilemap.", AutoSize = false };
        root.Controls.Add(hint, 0, 3);
        return root;
    }

    private Control BuildSlicesPage()
    {
        SplitContainer split = new() { Dock = DockStyle.Fill, SplitterDistance = 220 };
        Panel left = new() { Dock = DockStyle.Fill }; FlowLayoutPanel buttons = Flow(); buttons.Dock = DockStyle.Bottom; buttons.Controls.AddRange([Button("Add", (_, _) => PromptAddSliceFromSelection()), Button("Remove", (_, _) => RemoveSlice()), Button("Export JSON", (_, _) => ExportSliceMetadata())]); left.Controls.Add(sliceList); left.Controls.Add(buttons); split.Panel1.Controls.Add(left);
        TableLayoutPanel right = new() { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(6) }; right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); int row = 0;
        AddRow(right, ref row, "Name", sliceName); AddRow(right, ref row, "X", sliceX); AddRow(right, ref row, "Y", sliceY); AddRow(right, ref row, "Width", sliceW); AddRow(right, ref row, "Height", sliceH); AddFull(right, ref row, pivotEnabled); AddRow(right, ref row, "Pivot X", pivotX); AddRow(right, ref row, "Pivot Y", pivotY); AddFull(right, ref row, centerEnabled); AddRow(right, ref row, "Center X", centerX); AddRow(right, ref row, "Center Y", centerY); AddRow(right, ref row, "Center Width", centerW); AddRow(right, ref row, "Center Height", centerH);
        FlowLayoutPanel actions = Flow(); actions.Controls.AddRange([Button("Save Current-Frame Key", (_, _) => SaveSliceKey()), Button("Delete Current Key", (_, _) => DeleteSliceKey()), Button("Use Selection Bounds", (_, _) => UseSelectionBounds())]); AddFull(right, ref row, actions);
        AddRow(right, ref row, "Animated keys", sliceKeyList, 110); AddRow(right, ref row, "Preview width", previewW); AddRow(right, ref row, "Preview height", previewH); AddRow(right, ref row, "Nine-slice preview", slicePreview, 230);
        split.Panel2.Controls.Add(right); return split;
    }

    private Control BuildBridgePage()
    {
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(12) };
        table.Controls.Add(bridgeStatus); table.Controls.Add(LabelFor("Send the current composite or selected animation frames to Playroma Tile Studio. Tile Studio receives the PNG automatically and can return its selected sheet through File → Return Selected Sheet to Sprite Studio."));
        FlowLayoutPanel buttons = Flow(); buttons.Controls.AddRange([Button("Open Current Frame in Tile Studio", (_, _) => OpenTileStudio(false)), Button("Open Selected Frames", (_, _) => OpenTileStudio(true)), Button("Receive Returned PNG", (_, _) => ReceiveBridge()), Button("Import Tile Studio Project", (_, _) => ImportTileStudioProject())]); table.Controls.Add(buttons);
        return table;
    }

    private void RefreshAll()
    {
        if (refreshing || IsDisposed) return; refreshing = true;
        try
        {
            Guid? selected = (tilesetBox.SelectedItem as TilesetChoice)?.Id;
            tilesetBox.BeginUpdate(); tilesetBox.Items.Clear(); foreach (SpriteTileset set in session.Document.Tilesets) tilesetBox.Items.Add(new TilesetChoice(set.Id, set.Name)); tilesetBox.EndUpdate();
            int index = selected is Guid id ? Enumerable.Range(0, tilesetBox.Items.Count).FirstOrDefault(i => ((TilesetChoice)tilesetBox.Items[i]).Id == id) : -1;
            if (tilesetBox.Items.Count > 0) tilesetBox.SelectedIndex = index >= 0 ? index : 0;
            SpriteLayer selectedLayer = session.SelectedLayer;
            if (selectedLayer.Kind == SpriteLayerKind.Tilemap && selectedLayer.TilesetId is Guid selectedSet)
            {
                for (int i = 0; i < tilesetBox.Items.Count; i++) if (((TilesetChoice)tilesetBox.Items[i]).Id == selectedSet) { tilesetBox.SelectedIndex = i; break; }
                modeBox.SelectedItem = selectedLayer.TileEditMode.ToString(); map.EditMode = selectedLayer.TileEditMode;
                SpriteTilemapCel? cel = selectedLayer.TilemapCels.FirstOrDefault(item => item.FrameId == session.ActiveFrameId);
                SpriteTilemapContent? content = cel is null ? null : session.Document.TilemapContents.FirstOrDefault(item => item.Id == cel.TilemapContentId);
                if (content is not null) { mapColumns.Value = Math.Clamp(content.Columns, (int)mapColumns.Minimum, (int)mapColumns.Maximum); mapRows.Value = Math.Clamp(content.Rows, (int)mapRows.Minimum, (int)mapRows.Maximum); }
            }
            ApplyTilesetSelection();
            RefreshSlices(); map.Invalidate(); pixelEditor.Invalidate(); slicePreview.Invalidate();
        }
        finally { refreshing = false; }
        if (sliceList.SelectedItem is not null) LoadSelectedSlice();
    }

    private void ApplyTilesetSelection()
    {
        Guid? id = (tilesetBox.SelectedItem as TilesetChoice)?.Id; palette.TilesetId = id; pixelEditor.TilesetId = id; pixelEditor.ForegroundColor = foregroundProvider(); palette.Invalidate();
        if (id is Guid value && session.Document.Tilesets.FirstOrDefault(item => item.Id == value) is SpriteTileset set)
        { tileWidth.Value = Math.Clamp(set.TileWidth, (int)tileWidth.Minimum, (int)tileWidth.Maximum); tileHeight.Value = Math.Clamp(set.TileHeight, (int)tileHeight.Minimum, (int)tileHeight.Maximum); tilesetMargin.Value = Math.Clamp(set.Margin, (int)tilesetMargin.Minimum, (int)tilesetMargin.Maximum); tilesetSpacing.Value = Math.Clamp(set.Spacing, (int)tilesetSpacing.Minimum, (int)tilesetSpacing.Maximum); }
    }

    private void ApplyMode()
    {
        if (!Enum.TryParse(modeBox.Text, out SpriteTileEditMode mode)) return;
        map.EditMode = mode;
        if (session.SelectedLayer.Kind != SpriteLayerKind.Tilemap || session.SelectedLayer.TileEditMode == mode) return;
        SpritePackageData before = session.CapturePackageSnapshot(); session.SelectedLayer.TileEditMode = mode; session.CommitTilemapEdit("Change Tile Edit Mode", before);
    }

    private void PromptStack()
    {
        string initial = stackTileIndexes.Count == 0 ? palette.SelectedTileIndex.ToString() : string.Join(",", stackTileIndexes);
        using TextPromptDialog dialog = new("Stack Brush", "Enter tile indexes in painting order, such as 0,1,2. Automatic and manual brushes ignore this list.", initial);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Safe(() =>
        {
            List<int> parsed = [];
            foreach (string part in dialog.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(part, out int index)) parsed.Add(index); else throw new FormatException($"Invalid tile index: {part}");
            if (parsed.Count == 0) parsed.Add(palette.SelectedTileIndex);
            if (tilesetBox.SelectedItem is TilesetChoice choice)
            {
                HashSet<int> valid = session.Document.Tilesets.First(item => item.Id == choice.Id).Tiles.Select(tile => tile.Index).ToHashSet();
                if (parsed.Any(index => !valid.Contains(index))) throw new ArgumentOutOfRangeException(nameof(dialog), "The stack contains an index that is not present in the selected tileset.");
            }
            stackTileIndexes.Clear(); stackTileIndexes.AddRange(parsed); map.StackTileIndexes = stackTileIndexes.ToArray();
        });
    }

    private TileBrush CurrentBrush() => new(map.EditMode == SpriteTileEditMode.Stack && stackTileIndexes.Count > 0 ? stackTileIndexes : [palette.SelectedTileIndex], ResolveTransform());
    private SpriteTileTransform ResolveTransform() => transformBox.SelectedIndex switch { 1 => SpriteTileTransform.FlipHorizontal, 2 => SpriteTileTransform.FlipVertical, 3 => SpriteTileTransform.Rotate90, 4 => SpriteTileTransform.Rotate90 | SpriteTileTransform.FlipHorizontal, 5 => SpriteTileTransform.Rotate90 | SpriteTileTransform.FlipVertical, _ => SpriteTileTransform.None };

    private void GenerateAutoRules()
    {
        if (tilesetBox.SelectedItem is not TilesetChoice choice) return;
        Safe(() =>
        {
            SpritePackageData before = session.CapturePackageSnapshot(); SpriteTileset set = session.Document.Tilesets.First(item => item.Id == choice.Id); int[] tiles = set.Tiles.Where(tile => !tile.Empty).Select(tile => tile.Index).ToArray(); if (tiles.Length == 0) throw new InvalidOperationException("The tileset has no drawable tiles.");
            set.AutoRules = Enumerable.Range(0, 256).Select(mask => new SpriteAutoTileRule { NeighborMask = mask, TileIndex = tiles[mask % tiles.Length] }).ToList(); session.CommitTilemapEdit("Generate Auto-Tile Rules", before);
        });
    }

    private void PromptRemap()
    {
        if (tilesetBox.SelectedItem is not TilesetChoice choice) return; using TextPromptDialog dialog = new("Remap Tiles", "Enter mappings such as 0:5, 1:6, 2:-1", "0:1"); if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Safe(() =>
        {
            Dictionary<int, int> remap = []; foreach (string entry in dialog.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) { string[] parts = entry.Split(':', StringSplitOptions.TrimEntries); if (parts.Length != 2 || !int.TryParse(parts[0], out int from) || !int.TryParse(parts[1], out int to)) throw new FormatException($"Invalid mapping: {entry}"); remap[from] = to; }
            SpritePackageData before = session.CapturePackageSnapshot(); TilemapDocumentOperations.RemapTiles(session.Document, choice.Id, remap); session.CommitTilemapEdit("Remap Tiles", before);
        });
    }

    private void RefreshSlices()
    {
        Guid? selected = (sliceList.SelectedItem as SliceChoice)?.Id; sliceList.BeginUpdate(); sliceList.Items.Clear(); foreach (SpriteSlice slice in session.Document.Slices) sliceList.Items.Add(new SliceChoice(slice.Id, slice.Name)); sliceList.EndUpdate();
        if (sliceList.Items.Count == 0) return; int index = 0; if (selected is Guid id) for (int i = 0; i < sliceList.Items.Count; i++) if (((SliceChoice)sliceList.Items[i]).Id == id) { index = i; break; } sliceList.SelectedIndex = index;
    }

    private SpriteSlice? SelectedSlice() => sliceList.SelectedItem is SliceChoice choice ? session.Document.Slices.FirstOrDefault(item => item.Id == choice.Id) : null;
    private void LoadSelectedSlice()
    {
        if (refreshing) return; SpriteSlice? slice = SelectedSlice(); if (slice is null) return; refreshing = true;
        try { LoadKey(slice.Resolve(session.ActiveFrameIndex)); sliceName.Text = slice.Name; RefreshSliceKeyList(slice); }
        finally { refreshing = false; }
    }
    private void LoadSelectedSliceKey() { if (refreshing || sliceKeyList.SelectedItem is not SliceKeyChoice choice) return; SpriteSlice? slice = SelectedSlice(); SpriteSliceKey? key = slice?.Keys.FirstOrDefault(item => item.FrameNumber == choice.Frame); if (key is not null) LoadKey(key); }
    private void LoadKey(SpriteSliceKey key)
    {
        sliceX.Value = Clamp(sliceX, key.Bounds.X); sliceY.Value = Clamp(sliceY, key.Bounds.Y); sliceW.Value = Clamp(sliceW, key.Bounds.Width); sliceH.Value = Clamp(sliceH, key.Bounds.Height);
        pivotEnabled.Checked = key.Pivot.HasValue; if (key.Pivot is IntPoint pivot) { pivotX.Value = Clamp(pivotX, pivot.X); pivotY.Value = Clamp(pivotY, pivot.Y); }
        centerEnabled.Checked = key.Center.HasValue; if (key.Center is IntRect center) { centerX.Value = Clamp(centerX, center.X); centerY.Value = Clamp(centerY, center.Y); centerW.Value = Clamp(centerW, center.Width); centerH.Value = Clamp(centerH, center.Height); }
        RefreshSlicePreview();
    }
    private void RefreshSliceKeyList(SpriteSlice slice) { sliceKeyList.Items.Clear(); foreach (SpriteSliceKey key in slice.Keys.OrderBy(item => item.FrameNumber)) sliceKeyList.Items.Add(new SliceKeyChoice(key.FrameNumber)); if (sliceKeyList.Items.Count > 0) sliceKeyList.SelectedIndex = Math.Max(0, slice.Keys.FindIndex(item => item.FrameNumber == session.ActiveFrameIndex)); }
    private SpriteSliceKey ReadKey() => new() { FrameNumber = session.ActiveFrameIndex, Bounds = new IntRect((int)sliceX.Value, (int)sliceY.Value, (int)sliceW.Value, (int)sliceH.Value), Pivot = pivotEnabled.Checked ? new IntPoint((int)pivotX.Value, (int)pivotY.Value) : null, Center = centerEnabled.Checked ? new IntRect((int)centerX.Value, (int)centerY.Value, (int)centerW.Value, (int)centerH.Value) : null };
    private void SaveSliceKey() { SpriteSlice? slice = SelectedSlice(); if (slice is null) return; Safe(() => { SpriteSliceKey key = ReadKey(); session.UpdateSlice(slice.Id, sliceName.Text, key.Bounds, key.Pivot, key.Center, true); }); }
    private void DeleteSliceKey() { SpriteSlice? slice = SelectedSlice(); if (slice is null) return; Safe(() => session.RemoveSliceKey(slice.Id, session.ActiveFrameIndex)); }
    private void RemoveSlice() { SpriteSlice? slice = SelectedSlice(); if (slice is null) return; Safe(() => session.RemoveSlice(slice.Id)); }
    private void UseSelectionBounds() { IntRect? selected = session.Selection.GetBounds(); if (selected is null) return; IntRect bounds = selected.Value; sliceX.Value = Clamp(sliceX, bounds.X); sliceY.Value = Clamp(sliceY, bounds.Y); sliceW.Value = Clamp(sliceW, bounds.Width); sliceH.Value = Clamp(sliceH, bounds.Height); RefreshSlicePreview(); }
    private void RefreshSlicePreview() { if (refreshing) return; slicePreview.Key = ReadKey(); slicePreview.TargetSize = new Size((int)previewW.Value, (int)previewH.Value); slicePreview.Invalidate(); }

    private void OpenTileStudio(bool selectedFrames)
    {
        Safe(() =>
        {
            string root = projectRoot is not null ? Path.Combine(projectRoot, "data", "PlayromaTile", "SpriteBridge") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playroma", "SpriteStudio", "TileBridge");
            IReadOnlyCollection<int> frames = selectedFrames ? selectedFramesProvider() : [session.ActiveFrameIndex];
            int tw = (int)tileWidth.Value, th = (int)tileHeight.Value; lastBridge = TileStudioBridge.Export(session.Package, root, session.Document.Name, tw, th, selectedFrames ? "animation-frames" : "tileset", null, frames);
            string authoring = projectRoot is not null ? Path.Combine(projectRoot, "data", "PlayromaTile", "ProjectTiles.playromatile.json") : Path.Combine(root, "BridgeTiles.playromatile.json");
            TileEditorIntegrationOptions options = new(projectRoot ?? root, session.Document.Name, authoring, null, false, [lastBridge.SourceImagePath], lastBridge.ManifestPath, lastBridge.ReturnDirectory);
            Playroma.Tile.Editor.WinForms.MainForm form = new(options); form.Show(this); bridgeStatus.Text = $"Bridge active: {lastBridge.ManifestPath}";
        });
    }

    private void ReceiveBridge()
    {
        if (lastBridge is null) { MessageBox.Show(this, "No Tile Studio bridge is active.", "Tile Studio", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        Safe(() =>
        {
            IReadOnlyList<string> files = TileStudioBridge.FindReturnedPngFiles(lastBridge); if (files.Count == 0) throw new FileNotFoundException("Tile Studio has not returned a PNG yet.");
            PixelBuffer source = PngCodec.Load(files[^1]); session.ImportTileset(source, new TilesetImportOptions(Path.GetFileNameWithoutExtension(files[^1]), lastBridge.TileWidth, lastBridge.TileHeight, SourceImagePath: files[^1])); bridgeStatus.Text = $"Received {Path.GetFileName(files[^1])}.";
        });
    }

    private void ImportTileStudioProject()
    {
        using OpenFileDialog dialog = new() { Filter = "Playroma Tile Projects|*.playromatile.json;*.json|JSON Files|*.json", Title = "Import from Playroma Tile Studio" }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Safe(() =>
        {
            TileProject project = ProjectSerializer.Load(dialog.FileName); List<TileSheetChoice> choices = project.TileSets.SelectMany(set => set.Sheets.Select(sheet => new TileSheetChoice(set, sheet))).ToList(); if (choices.Count == 0) throw new InvalidDataException("The Tile Studio project has no sheets.");
            using TileStudioSheetDialog picker = new(choices); if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected is null) return;
            string imagePath = ProjectPath.Resolve(project, picker.Selected.Sheet.RelativeImagePath); PixelBuffer source = PngCodec.Load(imagePath); int tw = picker.Selected.Sheet.Slot.ToString().StartsWith("A", StringComparison.OrdinalIgnoreCase) ? 48 : (int)tileWidth.Value; int th = tw;
            session.ImportTileset(source, new TilesetImportOptions(picker.Selected.TileSet.Name + " " + picker.Selected.Sheet.DisplayName, tw, th, SourceImagePath: imagePath, SourceTileStudioProjectPath: dialog.FileName, ExternalTilesetId: picker.Selected.TileSet.Id)); bridgeStatus.Text = $"Linked {picker.Selected.Sheet.DisplayName} from Tile Studio.";
        });
    }

    private void Safe(Action action) { try { action(); } catch (Exception exception) { ShowError("The operation could not be completed.", exception); } }
    private void ShowError(string message, Exception exception) => MessageBox.Show(this, message + Environment.NewLine + Environment.NewLine + exception.Message, "Playroma Sprite Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private static FlowLayoutPanel Flow() => new() { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
    private static Button Button(string text, EventHandler click) { Button button = new() { Text = text, AutoSize = true }; button.Click += click; return button; }
    private static Label LabelFor(string text) => new() { Text = text, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(5, 7, 3, 3) };
    private static ComboBox DropDown() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private static NumericUpDown Number(decimal min, decimal max, decimal value) => new() { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 72 };
    private static decimal Clamp(NumericUpDown control, int value) => Math.Clamp((decimal)value, control.Minimum, control.Maximum);
    private static void AddRow(TableLayoutPanel table, ref int row, string label, Control control, int height = 30) { table.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); table.Controls.Add(LabelFor(label), 0, row); control.Dock = DockStyle.Fill; table.Controls.Add(control, 1, row++); }
    private static void AddFull(TableLayoutPanel table, ref int row, Control control) { table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); control.Dock = DockStyle.Top; table.Controls.Add(control, 0, row); table.SetColumnSpan(control, 2); row++; }

    private sealed record TilesetChoice(Guid Id, string Name) { public override string ToString() => Name; }
    private sealed record SliceChoice(Guid Id, string Name) { public override string ToString() => Name; }
    private sealed record SliceKeyChoice(int Frame) { public override string ToString() => $"Frame {Frame + 1}"; }
    private sealed record TileSheetChoice(TileSetDefinition TileSet, TileSheetDefinition Sheet) { public override string ToString() => $"{TileSet.Name} — {Sheet.DisplayName} ({Sheet.Slot})"; }

    private sealed class TileStudioSheetDialog : Form
    {
        private readonly ListBox list = new() { Dock = DockStyle.Fill };
        public TileStudioSheetDialog(IReadOnlyList<TileSheetChoice> choices) { Text = "Select Tile Studio Sheet"; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(620, 420); list.Items.AddRange(choices.Cast<object>().ToArray()); if (list.Items.Count > 0) list.SelectedIndex = 0; FlowLayoutPanel buttons = Flow(); buttons.Dock = DockStyle.Bottom; Button ok = Button("Import", (_, _) => { DialogResult = DialogResult.OK; Close(); }); Button cancel = Button("Cancel", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }); buttons.Controls.AddRange([ok, cancel]); Controls.Add(list); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel; }
        public TileSheetChoice? Selected => list.SelectedItem as TileSheetChoice;
    }
}

internal sealed class SlicePreviewControl : Control
{
    private readonly SpriteEditorSession session;
    public SlicePreviewControl(SpriteEditorSession session) { this.session = session; DoubleBuffered = true; BackColor = Color.FromArgb(42, 42, 46); }
    public SpriteSliceKey? Key { get; set; }
    public Size TargetSize { get; set; } = new(180, 120);
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (Key is null) return;
        try
        {
            PixelBuffer source = session.CompositeCurrentFrame(true); PixelBuffer preview = NineSliceRenderer.Render(source, Key, TargetSize.Width, TargetSize.Height); using Bitmap bitmap = PixelBufferBitmap.ToBitmap(preview); int scale = Math.Max(1, Math.Min(ClientSize.Width / Math.Max(1, preview.Width), ClientSize.Height / Math.Max(1, preview.Height))); e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor; e.Graphics.DrawImage(bitmap, new Rectangle(0, 0, preview.Width * scale, preview.Height * scale));
        }
        catch (Exception exception) { TextRenderer.DrawText(e.Graphics, exception.Message, Font, ClientRectangle, Color.Gainsboro, TextFormatFlags.WordBreak); }
    }
}
