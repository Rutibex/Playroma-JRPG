using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class ColorPalettePanel : UserControl
{
    private readonly SpriteEditorSession session;
    private readonly ComboBox modeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox ditherBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown colorCount = new() { Minimum = 2, Maximum = 256, Value = 32, Dock = DockStyle.Fill };
    private readonly ComboBox profileBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox sortBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly CheckBox descendingSort = new() { Text = "Descending", AutoSize = true };
    private readonly ListBox palettes = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox entries = new() { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22 };
    private bool refreshing;

    public ColorPalettePanel(SpriteEditorSession editorSession)
    {
        session = editorSession;
        Dock = DockStyle.Fill; Padding = new Padding(8);
        modeBox.Items.AddRange(Enum.GetNames<SpriteColorMode>()); ditherBox.Items.AddRange(Enum.GetNames<PaletteDitherMode>()); ditherBox.SelectedItem = nameof(PaletteDitherMode.None);
        foreach (BuiltInColorProfile profile in ColorProfileOperations.BuiltIns) profileBox.Items.Add(new ProfileChoice(profile.Id, profile.DisplayName));
        sortBox.Items.AddRange(Enum.GetNames<PaletteSortMode>()); sortBox.SelectedItem = nameof(PaletteSortMode.Index);
        palettes.SelectedIndexChanged += (_, _) => SelectPalette(); entries.DoubleClick += (_, _) => EditSelectedColor(); entries.DrawItem += DrawEntry;
        session.Changed += (_, _) => RefreshFromSession();
        Controls.Add(BuildLayout()); RefreshFromSession();
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 12 };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 34)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 66)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.Controls.Add(Labeled("Document color mode", modeBox));
        FlowLayoutPanel modeButtons = Flow(); modeButtons.Controls.Add(MakeButton("Convert", ConvertMode)); modeButtons.Controls.Add(MakeButton("Apply palette", ApplyPalette)); root.Controls.Add(modeButtons);
        root.Controls.Add(Labeled("Color profile", profileBox));
        FlowLayoutPanel profileButtons = Flow(); profileButtons.Controls.Add(MakeButton("Assign", AssignProfile)); profileButtons.Controls.Add(MakeButton("Convert pixels", ConvertProfile)); root.Controls.Add(profileButtons);
        root.Controls.Add(palettes);
        FlowLayoutPanel paletteButtons = Flow(); paletteButtons.Controls.Add(MakeButton("Generate", Generate)); paletteButtons.Controls.Add(MakeButton("Import", Import)); paletteButtons.Controls.Add(MakeButton("Export", Export)); paletteButtons.Controls.Add(MakeButton("Delete", DeletePalette)); root.Controls.Add(paletteButtons);
        root.Controls.Add(entries);
        FlowLayoutPanel entryButtons = Flow(); entryButtons.Controls.Add(MakeButton("Add color", AddColor)); entryButtons.Controls.Add(MakeButton("Edit color", EditSelectedColor)); entryButtons.Controls.Add(MakeButton("Remove color", RemoveColor)); root.Controls.Add(entryButtons);
        root.Controls.Add(Labeled("Palette sorting", sortBox));
        FlowLayoutPanel sortButtons = Flow(); sortButtons.Controls.Add(MakeButton("Sort", SortPalette)); sortButtons.Controls.Add(descendingSort); root.Controls.Add(sortButtons);
        root.Controls.Add(Labeled("Maximum colors", colorCount)); root.Controls.Add(Labeled("Remap dithering", ditherBox)); return root;
    }

    private void RefreshFromSession()
    {
        if (refreshing) return; refreshing = true;
        try
        {
            modeBox.SelectedItem = session.Document.ColorMode.ToString();
            ProfileChoice? profile = profileBox.Items.Cast<ProfileChoice>().FirstOrDefault(item => item.Id.Equals(ProfileIdForDocument(), StringComparison.OrdinalIgnoreCase));
            profileBox.SelectedItem = profile ?? profileBox.Items.Cast<ProfileChoice>().FirstOrDefault();
            Guid? selectedId = (palettes.SelectedItem as PaletteChoice)?.Id ?? session.Document.ActivePaletteId;
            palettes.Items.Clear(); foreach (SpritePalette palette in session.Document.Palettes) palettes.Items.Add(new PaletteChoice(palette.Id, $"{palette.Name} ({palette.Entries.Count})"));
            PaletteChoice? choice = palettes.Items.Cast<PaletteChoice>().FirstOrDefault(item => item.Id == selectedId) ?? palettes.Items.Cast<PaletteChoice>().FirstOrDefault();
            if (choice is not null) palettes.SelectedItem = choice; RefreshEntries();
        }
        finally { refreshing = false; }
    }

    private void SelectPalette()
    {
        if (refreshing || palettes.SelectedItem is not PaletteChoice choice) return;
        try { session.SetActivePalette(choice.Id); } catch { }
        RefreshEntries();
    }

    private void RefreshEntries()
    {
        entries.Items.Clear(); SpritePalette? palette = SelectedPalette(); if (palette is null) return;
        foreach (SpritePaletteEntry entry in palette.Entries.OrderBy(entry => entry.Index)) entries.Items.Add(new EntryChoice(entry.Index, entry.Name ?? $"Color {entry.Index}", new(entry.Color.R, entry.Color.G, entry.Color.B, entry.Color.A)));
    }


    private void AssignProfile()
    {
        if (profileBox.SelectedItem is not ProfileChoice profile) return;
        try { session.AssignColorProfile(profile.Id); } catch (Exception exception) { ShowError(exception); }
    }

    private void ConvertProfile()
    {
        if (profileBox.SelectedItem is not ProfileChoice profile) return;
        try { session.ConvertColorProfile(profile.Id); } catch (Exception exception) { ShowError(exception); }
    }

    private void SortPalette()
    {
        if (!Enum.TryParse(sortBox.SelectedItem?.ToString(), out PaletteSortMode mode)) return;
        try { session.SortActivePalette(mode, descendingSort.Checked); } catch (Exception exception) { ShowError(exception); }
    }

    private string ProfileIdForDocument()
    {
        SpriteColorProfileMetadata metadata = session.Document.ColorProfileMetadata;
        BuiltInColorProfile? exact = ColorProfileOperations.BuiltIns.FirstOrDefault(profile =>
            profile.Kind.Equals(metadata.Kind, StringComparison.OrdinalIgnoreCase) && Math.Abs(profile.Gamma - metadata.Gamma) < .000001d);
        return exact?.Id ?? "unspecified";
    }

    private void ConvertMode()
    {
        if (!Enum.TryParse(modeBox.SelectedItem?.ToString(), out SpriteColorMode mode)) return;
        try { session.ConvertColorMode(mode, Decimal.ToInt32(colorCount.Value), SelectedDither()); }
        catch (Exception exception) { ShowError(exception); }
    }

    private void Generate()
    {
        using TextPromptDialog dialog = new("Generate Palette", "Palette name", $"{colorCount.Value}-Color Palette"); if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { session.GeneratePalette(dialog.Value, Decimal.ToInt32(colorCount.Value), SelectedDither()); }
        catch (Exception exception) { ShowError(exception); }
    }

    private void ApplyPalette() { try { session.ApplyActivePalette(SelectedDither()); } catch (Exception exception) { ShowError(exception); } }

    private void Import()
    {
        using OpenFileDialog dialog = new() { Filter = "Palette files|*.gpl;*.pal;*.hex;*.txt;*.json|All files|*.*" }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { session.AddPalette(PaletteFileCodec.Load(dialog.FileName)); } catch (Exception exception) { ShowError(exception); }
    }

    private void Export()
    {
        SpritePalette? palette = SelectedPalette(); if (palette is null) return;
        using SaveFileDialog dialog = new() { Filter = "GIMP palette (*.gpl)|*.gpl|JASC palette (*.pal)|*.pal|Hex colors (*.hex)|*.hex|Playroma JSON (*.json)|*.json", FileName = palette.Name + ".gpl" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return; try { PaletteFileCodec.Save(palette, dialog.FileName); } catch (Exception exception) { ShowError(exception); }
    }

    private void DeletePalette() { if (SelectedPalette() is SpritePalette palette) try { session.RemovePalette(palette.Id); } catch (Exception exception) { ShowError(exception); } }

    private void AddColor()
    {
        SpritePalette? palette = SelectedPalette(); if (palette is null) return; using ColorDialog dialog = new() { FullOpen = true, Color = Color.White };
        if (dialog.ShowDialog(this) != DialogResult.OK) return; try { session.AddPaletteEntry(palette.Id, new(dialog.Color.R, dialog.Color.G, dialog.Color.B, dialog.Color.A)); } catch (Exception exception) { ShowError(exception); }
    }

    private void EditSelectedColor()
    {
        SpritePalette? palette = SelectedPalette(); if (palette is null || entries.SelectedItem is not EntryChoice choice) return;
        using ColorDialog dialog = new() { FullOpen = true, Color = Color.FromArgb(choice.Color.A, choice.Color.R, choice.Color.G, choice.Color.B) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return; try { session.UpdatePaletteEntry(palette.Id, choice.Index, new(dialog.Color.R, dialog.Color.G, dialog.Color.B, dialog.Color.A)); } catch (Exception exception) { ShowError(exception); }
    }

    private void RemoveColor() { SpritePalette? palette = SelectedPalette(); if (palette is null || entries.SelectedItem is not EntryChoice choice) return; try { session.RemovePaletteEntry(palette.Id, choice.Index); } catch (Exception exception) { ShowError(exception); } }
    private SpritePalette? SelectedPalette() => palettes.SelectedItem is PaletteChoice choice ? session.Document.Palettes.FirstOrDefault(palette => palette.Id == choice.Id) : session.ActivePalette;
    private PaletteDitherMode SelectedDither() => Enum.TryParse(ditherBox.SelectedItem?.ToString(), out PaletteDitherMode mode) ? mode : PaletteDitherMode.None;

    private void DrawEntry(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground(); if (e.Index < 0 || e.Index >= entries.Items.Count || entries.Items[e.Index] is not EntryChoice item) return;
        using SolidBrush swatch = new(Color.FromArgb(item.Color.A, item.Color.R, item.Color.G, item.Color.B)); e.Graphics.FillRectangle(swatch, e.Bounds.X + 3, e.Bounds.Y + 3, 28, e.Bounds.Height - 6); e.Graphics.DrawRectangle(Pens.Black, e.Bounds.X + 3, e.Bounds.Y + 3, 28, e.Bounds.Height - 6);
        TextRenderer.DrawText(e.Graphics, $"{item.Index:000}  {item.Name}  #{item.Color.R:X2}{item.Color.G:X2}{item.Color.B:X2}{item.Color.A:X2}", e.Font, new Rectangle(e.Bounds.X + 38, e.Bounds.Y, e.Bounds.Width - 40, e.Bounds.Height), e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); e.DrawFocusRectangle();
    }

    private static Button MakeButton(string text, Action action) { Button button = new() { Text = text, AutoSize = true }; button.Click += (_, _) => action(); return button; }
    private static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    private static Control Labeled(string text, Control control) { TableLayoutPanel panel = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 }; panel.ColumnStyles.Add(new(SizeType.Percent, 45)); panel.ColumnStyles.Add(new(SizeType.Percent, 55)); panel.Controls.Add(new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0); panel.Controls.Add(control, 1, 0); return panel; }
    private void ShowError(Exception exception) => MessageBox.Show(this, exception.Message, "Palette operation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    private sealed record PaletteChoice(Guid Id, string Name) { public override string ToString() => Name; }
    private sealed record ProfileChoice(string Id, string Name) { public override string ToString() => Name; }
    private sealed record EntryChoice(int Index, string Name, Rgba32 Color);
}
