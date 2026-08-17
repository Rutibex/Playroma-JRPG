using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;
using Playroma.Sprite.RpgMakerMV;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class SystemAssetEditorPanel : UserControl
{
    private readonly SpriteEditorSession session;
    private readonly Action<string> selectProfile;
    private readonly ComboBox editorBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ListBox regionList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly NumericUpDown itemBox = new() { Minimum = 0, Maximum = 999, Dock = DockStyle.Fill };
    private readonly NumericUpDown frameBox = new() { Minimum = 0, Maximum = 99, Dock = DockStyle.Fill };
    private readonly TextBox damageText = new() { Text = "1234", Dock = DockStyle.Fill };
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(28, 30, 38) };
    private readonly ListBox diagnostics = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label description = new() { Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = SystemColors.GrayText };
    private readonly Label dimensions = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly System.Windows.Forms.Timer animationTimer = new() { Interval = 140 };
    private readonly EventHandler sessionChangedHandler;
    private MvSystemAssetDefinition? definition;
    private bool suppress;

    public SystemAssetEditorPanel(SpriteEditorSession session, Action<string> selectProfile)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.selectProfile = selectProfile ?? throw new ArgumentNullException(nameof(selectProfile));
        Dock = DockStyle.Fill;
        editorBox.Items.AddRange(MvSystemAssetCatalog.Definitions.Select(item => new DefinitionChoice(item)).Cast<object>().ToArray());
        editorBox.SelectedIndexChanged += (_, _) => EditorChanged();
        itemBox.ValueChanged += (_, _) => { PopulateRegions(); RefreshPreview(); };
        frameBox.ValueChanged += (_, _) => { SelectMatchingRegion(); RefreshPreview(); };
        damageText.TextChanged += (_, _) => RefreshPreview();
        regionList.SelectedIndexChanged += (_, _) => RegionChanged();
        animationTimer.Tick += (_, _) => AnimatePreview();
        sessionChangedHandler = (_, _) => RefreshFromSession();
        session.Changed += sessionChangedHandler;
        Controls.Add(BuildLayout());
    }

    public void SetProfile(string? profileId)
    {
        if (!MvSystemAssetCatalog.TryGet(profileId, out MvSystemAssetDefinition? requested))
        {
            definition = null;
            suppress = true;
            try { editorBox.SelectedIndex = -1; }
            finally { suppress = false; }
            RefreshFromSession();
            return;
        }
        definition = requested;
        suppress = true;
        try
        {
            for (int index = 0; index < editorBox.Items.Count; index++)
                if (editorBox.Items[index] is DefinitionChoice choice && choice.Definition.ProfileId.Equals(requested.ProfileId, StringComparison.OrdinalIgnoreCase))
                { editorBox.SelectedIndex = index; break; }
        }
        finally { suppress = false; }
        ConfigureDefinition();
    }

    public void RefreshFromSession()
    {
        if (IsDisposed) return;
        if (definition is null && MvSystemAssetCatalog.TryGet(session.Document.Metadata.RpgMakerMvProfileId, out MvSystemAssetDefinition? detected)) definition = detected;
        ConfigureDefinition(refreshChoice: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            session.Changed -= sessionChangedHandler;
            animationTimer.Stop(); animationTimer.Dispose();
            Image? old = preview.Image; preview.Image = null; old?.Dispose();
        }
        base.Dispose(disposing);
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));

        TableLayoutPanel controls = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105)); controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        AddRow(controls, ref row, "Asset editor", editorBox);
        AddRow(controls, ref row, "Item / row", itemBox);
        AddRow(controls, ref row, "Frame / glyph", frameBox);
        AddRow(controls, ref row, "Damage sample", damageText);
        AddRow(controls, ref row, "Canvas", dimensions);
        AddFullRow(controls, ref row, description);

        FlowLayoutPanel buttons = new() { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        buttons.Controls.Add(Button("New stock template", CreateTemplate));
        buttons.Controls.Add(Button("Resize to stock", ResizeToStock));
        buttons.Controls.Add(Button("Select region", SelectRegion));
        buttons.Controls.Add(Button("Clear region", ClearRegion));
        buttons.Controls.Add(Button("◀", Previous));
        buttons.Controls.Add(Button("▶", Next));
        AddFullRow(controls, ref row, buttons);

        SplitContainer center = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 165, FixedPanel = FixedPanel.Panel1 };
        GroupBox regions = new() { Text = "Named regions and cells", Dock = DockStyle.Fill, Padding = new Padding(5) }; regions.Controls.Add(regionList);
        GroupBox live = new() { Text = "Live RPG Maker MV preview", Dock = DockStyle.Fill, Padding = new Padding(5) }; live.Controls.Add(preview);
        center.Panel1.Controls.Add(regions); center.Panel2.Controls.Add(live);
        GroupBox validation = new() { Text = "System-asset diagnostics", Dock = DockStyle.Fill, Padding = new Padding(5) }; validation.Controls.Add(diagnostics);
        root.Controls.Add(controls, 0, 0); root.Controls.Add(center, 0, 1); root.Controls.Add(validation, 0, 2);
        return root;
    }

    private void EditorChanged()
    {
        if (suppress || editorBox.SelectedItem is not DefinitionChoice choice) return;
        definition = choice.Definition;
        selectProfile(definition.ProfileId);
        ConfigureDefinition(refreshChoice: false);
    }

    private void ConfigureDefinition(bool refreshChoice = true)
    {
        if (definition is null)
        {
            description.Text = "Choose one of the specialized RPG Maker MV system-asset editors.";
            dimensions.Text = $"{session.Document.Canvas.Width}×{session.Document.Canvas.Height}";
            animationTimer.Stop(); PopulateRegions(); RefreshPreview(); RefreshDiagnostics(); return;
        }
        DefinitionChoice? current = editorBox.SelectedItem as DefinitionChoice;
        if (refreshChoice && (current is null || current.Definition.ProfileId != definition.ProfileId))
        {
            suppress = true;
            try
            {
                for (int index = 0; index < editorBox.Items.Count; index++)
                    if (editorBox.Items[index] is DefinitionChoice choice && choice.Definition.ProfileId == definition.ProfileId) { editorBox.SelectedIndex = index; break; }
            }
            finally { suppress = false; }
        }
        description.Text = definition.Description;
        dimensions.Text = $"{session.Document.Canvas.Width}×{session.Document.Canvas.Height} (stock {definition.StockWidth}×{definition.StockHeight})";
        ConfigureRanges(); PopulateRegions(); RefreshDiagnostics(); RefreshPreview();
        if (definition.Animated) animationTimer.Start(); else animationTimer.Stop();
    }

    private void ConfigureRanges()
    {
        if (definition is null) return;
        var grid = MvSystemAssetCatalog.ResolveGrid(definition, session.Document.Canvas.Width, session.Document.Canvas.Height);
        int columns = grid.Columns, rows = grid.Rows;
        int cells = Math.Max(1, columns * rows);
        int itemMaximum = definition.Kind switch
        {
            MvSystemAssetKind.Damage => rows - 1,
            MvSystemAssetKind.States => rows - 1,
            MvSystemAssetKind.Window => 0,
            MvSystemAssetKind.ButtonSet => 4,
            MvSystemAssetKind.Weapons1 or MvSystemAssetKind.Weapons2 => Math.Min(11, rows * 2 - 1),
            MvSystemAssetKind.Weapons3 => Math.Min(5, rows - 1),
            _ => Math.Max(0, cells / Math.Max(1, definition.FramesPerItem) - 1)
        };
        int frameMaximum = definition.Kind switch
        {
            MvSystemAssetKind.Damage => columns - 1,
            MvSystemAssetKind.Window => 3,
            MvSystemAssetKind.ButtonSet => 1,
            _ => Math.Max(0, definition.FramesPerItem - 1)
        };
        suppress = true;
        try
        {
            itemBox.Maximum = Math.Max(0, itemMaximum); itemBox.Value = Math.Min(itemBox.Value, itemBox.Maximum);
            frameBox.Maximum = Math.Max(0, frameMaximum); frameBox.Value = Math.Min(frameBox.Value, frameBox.Maximum);
            itemBox.Enabled = itemMaximum > 0;
            frameBox.Enabled = frameMaximum > 0;
            damageText.Enabled = definition.Kind == MvSystemAssetKind.Damage;
        }
        finally { suppress = false; }
    }

    private void PopulateRegions()
    {
        regionList.BeginUpdate(); regionList.Items.Clear();
        if (definition is not null)
        {
            IReadOnlyList<MvSystemAssetRegion> all = MvSystemAssetCatalog.GetRegions(definition, session.Document.Canvas.Width, session.Document.Canvas.Height);
            IEnumerable<MvSystemAssetRegion> visible = definition.Kind switch
            {
                MvSystemAssetKind.Damage => all.Where(region => region.Index == (int)itemBox.Value),
                MvSystemAssetKind.Balloon or MvSystemAssetKind.States or MvSystemAssetKind.ButtonSet or MvSystemAssetKind.Weapons1 or MvSystemAssetKind.Weapons2 or MvSystemAssetKind.Weapons3 => all.Where(region => region.Index == (int)itemBox.Value),
                _ => all
            };
            foreach (MvSystemAssetRegion region in visible) regionList.Items.Add(new RegionChoice(region));
        }
        regionList.EndUpdate(); SelectMatchingRegion();
    }

    private void SelectMatchingRegion()
    {
        if (suppress || definition is null) return;
        if (definition.Kind == MvSystemAssetKind.Window)
        {
            if (regionList.Items.Count > 0 && regionList.SelectedIndex < 0) regionList.SelectedIndex = 0;
            return;
        }
        int item = (int)itemBox.Value, frame = (int)frameBox.Value;
        for (int index = 0; index < regionList.Items.Count; index++)
        {
            if (regionList.Items[index] is not RegionChoice choice) continue;
            bool match = definition.Kind == MvSystemAssetKind.Damage ? choice.Region.Frame == frame : choice.Region.Index == item && choice.Region.Frame == frame;
            if (match) { regionList.SelectedIndex = index; return; }
        }
        if (regionList.Items.Count > 0 && regionList.SelectedIndex < 0) regionList.SelectedIndex = 0;
    }

    private void RegionChanged()
    {
        if (suppress || regionList.SelectedItem is not RegionChoice choice || definition is null) return;
        if (definition.Kind == MvSystemAssetKind.Window) { RefreshPreview(); return; }
        suppress = true;
        try
        {
            if (definition.Kind == MvSystemAssetKind.Damage) { itemBox.Value = Math.Min(itemBox.Maximum, choice.Region.Index); frameBox.Value = Math.Min(frameBox.Maximum, choice.Region.Frame); }
            else { itemBox.Value = Math.Min(itemBox.Maximum, choice.Region.Index); frameBox.Value = Math.Min(frameBox.Maximum, choice.Region.Frame); }
        }
        finally { suppress = false; }
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        Image? old = preview.Image; preview.Image = null; old?.Dispose();
        if (definition is null) return;
        try
        {
            PixelBuffer source = session.CompositeCurrentFrame(includeReferenceLayers: true);
            PixelBuffer rendered = MvSystemAssetPreviewRenderer.Render(source, definition, new MvSystemAssetPreviewOptions((int)itemBox.Value, (int)frameBox.Value, damageText.Text));
            preview.Image = PixelBufferBitmap.ToBitmap(rendered);
        }
        catch (Exception exception)
        {
            diagnostics.Items.Add("PREVIEW: " + exception.Message);
        }
    }

    private void RefreshDiagnostics()
    {
        diagnostics.BeginUpdate(); diagnostics.Items.Clear();
        if (definition is null) diagnostics.Items.Add("Choose a system-asset profile.");
        else
        {
            string fileName = session.ExportPath is null ? definition.ReservedFileName : Path.GetFileName(session.ExportPath);
            IReadOnlyList<MvSystemAssetDiagnostic> issues = MvSystemAssetCatalog.Validate(definition, fileName, session.Document.Canvas.Width, session.Document.Canvas.Height);
            if (issues.Count == 0) diagnostics.Items.Add("✓ Valid stock RPG Maker MV system asset");
            foreach (MvSystemAssetDiagnostic issue in issues) diagnostics.Items.Add($"{issue.Severity.ToUpperInvariant()}: {issue.Message}");
            diagnostics.Items.Add($"Runtime filename: {definition.ReservedFileName}");
            diagnostics.Items.Add($"Named regions: {MvSystemAssetCatalog.GetRegions(definition, session.Document.Canvas.Width, session.Document.Canvas.Height).Count}");
        }
        diagnostics.EndUpdate();
    }

    private void CreateTemplate()
    {
        if (definition is null) return;
        if (session.IsDirty && MessageBox.Show(this, "Replace the current document with a new stock system-asset template?", "Create template", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        session.ReplaceWithStaticImage(Path.GetFileNameWithoutExtension(definition.ReservedFileName), MvSystemAssetTemplateFactory.Create(definition), definition.ProfileId, dirty: true);
        ConfigureDefinition();
    }

    private void ResizeToStock()
    {
        if (definition is null) return;
        if (session.Document.Canvas.Width == definition.StockWidth && session.Document.Canvas.Height == definition.StockHeight) return;
        session.ResizeSprite(definition.StockWidth, definition.StockHeight, PixelResamplingMode.NearestNeighbor);
    }

    private void SelectRegion()
    {
        if (definition is null) return;
        IntRect region = CurrentRegionBounds();
        session.SetSelection(SelectionMask.Rectangle(session.Document.Canvas.Width, session.Document.Canvas.Height, region));
    }

    private void ClearRegion()
    {
        if (definition is null || session.ActiveLayer.Locked) return;
        IntRect region = CurrentRegionBounds();
        PixelBuffer before = session.ActiveBuffer.Clone();
        for (int y = Math.Max(0, region.Y); y < Math.Min(session.ActiveBuffer.Height, region.Bottom); y++)
        for (int x = Math.Max(0, region.X); x < Math.Min(session.ActiveBuffer.Width, region.Right); x++) session.ActiveBuffer.SetPixel(x, y, Rgba32.Transparent);
        session.CommitPixelEdit("Clear MV System Region", before);
    }

    private IntRect CurrentRegionBounds()
    {
        if (regionList.SelectedItem is RegionChoice choice) return choice.Region.Bounds;
        if (definition is null) return new IntRect(0, 0, session.Document.Canvas.Width, session.Document.Canvas.Height);
        if (definition.Kind == MvSystemAssetKind.Window) return MvSystemAssetCatalog.GetRegions(definition, session.Document.Canvas.Width, session.Document.Canvas.Height)[0].Bounds;
        return MvSystemAssetPreviewRenderer.ResolveCell(definition, session.Document.Canvas.Width, session.Document.Canvas.Height, (int)itemBox.Value, (int)frameBox.Value);
    }

    private void Previous()
    {
        if (frameBox.Enabled && frameBox.Value > frameBox.Minimum) frameBox.Value--;
        else if (itemBox.Enabled && itemBox.Value > itemBox.Minimum) { itemBox.Value--; frameBox.Value = frameBox.Maximum; }
    }

    private void Next()
    {
        if (frameBox.Enabled && frameBox.Value < frameBox.Maximum) frameBox.Value++;
        else if (itemBox.Enabled && itemBox.Value < itemBox.Maximum) { itemBox.Value++; frameBox.Value = frameBox.Minimum; }
    }

    private void AnimatePreview()
    {
        if (definition is null || !definition.Animated || !Visible || suppress || !frameBox.Enabled) return;
        frameBox.Value = frameBox.Value >= frameBox.Maximum ? frameBox.Minimum : frameBox.Value + 1;
    }

    private static Button Button(string text, Action click)
    {
        Button button = new() { Text = text, AutoSize = true, Margin = new Padding(2) };
        button.Click += (_, _) => click(); return button;
    }

    private static void AddRow(TableLayoutPanel table, ref int row, string label, Control control)
    {
        table.RowCount = row + 1; table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label caption = new() { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };
        table.Controls.Add(caption, 0, row); table.Controls.Add(control, 1, row++);
    }

    private static void AddFullRow(TableLayoutPanel table, ref int row, Control control)
    {
        table.RowCount = row + 1; table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(control, 0, row); table.SetColumnSpan(control, 2); row++;
    }

    private sealed record DefinitionChoice(MvSystemAssetDefinition Definition)
    {
        public override string ToString() => Definition.DisplayName;
    }

    private sealed record RegionChoice(MvSystemAssetRegion Region)
    {
        public override string ToString() => Region.DisplayName;
    }
}
