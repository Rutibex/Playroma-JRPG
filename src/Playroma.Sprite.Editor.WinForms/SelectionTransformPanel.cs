using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class SelectionTransformPanel : UserControl
{
    private readonly SpriteEditorSession session;
    private readonly SpriteCanvasControl canvas;
    private readonly ComboBox combineBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown toleranceBox = new() { Minimum = 0, Maximum = 255, Dock = DockStyle.Fill };
    private readonly CheckBox contiguousBox = new() { Text = "Magic wand contiguous", Checked = true, AutoSize = true };
    private readonly Label selectionStatus = new() { AutoSize = true };
    private PixelClipboardPayload? clipboard;
    private bool suppress;

    public SelectionTransformPanel(SpriteEditorSession editorSession, SpriteCanvasControl spriteCanvas)
    {
        session = editorSession ?? throw new ArgumentNullException(nameof(editorSession)); canvas = spriteCanvas ?? throw new ArgumentNullException(nameof(spriteCanvas));
        Dock = DockStyle.Fill; combineBox.Items.AddRange(Enum.GetNames<SelectionCombineMode>()); combineBox.SelectedItem = SelectionCombineMode.Replace.ToString();
        Controls.Add(BuildLayout());
        combineBox.SelectedIndexChanged += (_, _) => { if (!suppress && Enum.TryParse(combineBox.SelectedItem?.ToString(), out SelectionCombineMode mode)) canvas.SelectionCombineMode = mode; };
        toleranceBox.ValueChanged += (_, _) => canvas.MagicWandTolerance = (int)toleranceBox.Value;
        contiguousBox.CheckedChanged += (_, _) => canvas.MagicWandContiguous = contiguousBox.Checked;
        session.Changed += (_, _) => RefreshState(); RefreshState();
    }

    public void RefreshState()
    {
        if (IsDisposed) return; suppress = true;
        try
        {
            selectionStatus.Text = session.Selection.IsEmpty ? "No selection — drawing affects the full active layer." : $"{session.Selection.CountSelected():N0} selected pixels; bounds {FormatBounds(session.Selection.GetBounds())}.";
            canvas.SetSelection(session.Selection); canvas.MagicWandTolerance = (int)toleranceBox.Value; canvas.MagicWandContiguous = contiguousBox.Checked;
        }
        finally { suppress = false; }
    }

    public void Copy()
    {
        if (session.Selection.IsEmpty) return; clipboard = PixelSelectionTransforms.Copy(session.ActiveBuffer, session.Selection);
    }

    public void Cut()
    {
        if (session.Selection.IsEmpty || session.ActiveLayer.Locked) return;
        PixelBuffer before = session.ActiveBuffer.Clone(); clipboard = PixelSelectionTransforms.Cut(session.ActiveBuffer, session.Selection); session.CommitPixelEdit("Cut Selection", before);
    }

    public void Paste()
    {
        if (clipboard is null || session.ActiveLayer.Locked) return;
        PixelBuffer before = session.ActiveBuffer.Clone(); SelectionMask selected = PixelSelectionTransforms.Paste(session.ActiveBuffer, clipboard, clipboard.Origin, true);
        session.CommitSelectionTransform("Paste", before, selected);
    }

    public void PasteAsNewLayer()
    {
        if (clipboard is null) return;
        session.PasteToNewLayer(clipboard);
    }

    public void Nudge(int dx, int dy)
    {
        if (session.Selection.IsEmpty || session.ActiveLayer.Locked) return;
        PixelBuffer before = session.ActiveBuffer.Clone(); SelectionMask moved = PixelSelectionTransforms.Move(session.ActiveBuffer, session.Selection, dx, dy);
        session.CommitSelectionTransform("Move Selection", before, moved);
    }

    private Control BuildLayout()
    {
        TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(6) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(table, 0, "Combine mode", combineBox); AddRow(table, 1, "Wand tolerance", toleranceBox);
        table.Controls.Add(contiguousBox, 0, 2); table.SetColumnSpan(contiguousBox, 2);
        table.Controls.Add(selectionStatus, 0, 3); table.SetColumnSpan(selectionStatus, 2);
        FlowLayoutPanel selectionCommands = Flow();
        Add(selectionCommands, "All", SelectAll); Add(selectionCommands, "None", Deselect); Add(selectionCommands, "Invert", Invert); Add(selectionCommands, "Opaque", SelectOpaque);
        Add(selectionCommands, "Grow...", () => Morph(true)); Add(selectionCommands, "Shrink...", () => Morph(false)); Add(selectionCommands, "Border...", Border); Add(selectionCommands, "Feather...", Feather);
        table.Controls.Add(selectionCommands, 0, 4); table.SetColumnSpan(selectionCommands, 2);
        FlowLayoutPanel clipboardCommands = Flow(); Add(clipboardCommands, "Copy", Copy); Add(clipboardCommands, "Cut", Cut); Add(clipboardCommands, "Paste", Paste); Add(clipboardCommands, "Paste New Layer", PasteAsNewLayer);
        Add(clipboardCommands, "Copy to Layer", () => TryAction(() => session.MoveSelectionToNewLayer(false))); Add(clipboardCommands, "Cut to Layer", () => TryAction(() => session.MoveSelectionToNewLayer(true))); Add(clipboardCommands, "Copy Merged to Layer", () => TryAction(() => session.MoveSelectionToNewLayer(false, true)));
        table.Controls.Add(clipboardCommands, 0, 5); table.SetColumnSpan(clipboardCommands, 2);
        FlowLayoutPanel transforms = Flow();
        Add(transforms, "Flip H", () => Flip(true, false)); Add(transforms, "Flip V", () => Flip(false, true));
        Add(transforms, "Rotate 90°", () => Rotate(QuarterTurn.Clockwise90)); Add(transforms, "Rotate 180°", () => Rotate(QuarterTurn.HalfTurn));
        Add(transforms, "Rotate 270°", () => Rotate(QuarterTurn.CounterClockwise90)); Add(transforms, "Rotate...", RotateArbitrary); Add(transforms, "Scale...", ScaleSelection);
        table.Controls.Add(transforms, 0, 6); table.SetColumnSpan(transforms, 2);
        Panel root = new() { Dock = DockStyle.Fill, AutoScroll = true }; root.Controls.Add(table); return root;
    }

    public void SelectAll() { SelectionMask mask = new(session.Document.Canvas.Width, session.Document.Canvas.Height); mask.SelectAll(); session.SetSelection(mask); }
    public void Deselect() { session.SetSelection(new SelectionMask(session.Document.Canvas.Width, session.Document.Canvas.Height)); }
    public void Invert() { SelectionMask mask = session.Selection.Clone(); mask.Invert(); session.SetSelection(mask); }
    public void SelectOpaque() => session.SetSelection(SelectionMask.FromOpaque(session.ActiveBuffer));


    public void FlipHorizontal() => Flip(true, false);
    public void FlipVertical() => Flip(false, true);
    public void Rotate90() => Rotate(QuarterTurn.Clockwise90);
    public void Rotate180() => Rotate(QuarterTurn.HalfTurn);
    public void Rotate270() => Rotate(QuarterTurn.CounterClockwise90);

    public void RotateArbitrary()
    {
        if (session.Selection.IsEmpty || session.ActiveLayer.Locked) return;
        using TextPromptDialog dialog = new("Rotate Selection", "Degrees clockwise", "45");
        if (dialog.ShowDialog(this) != DialogResult.OK || !double.TryParse(dialog.Value, out double degrees)) return;
        PixelBuffer before = session.ActiveBuffer.Clone(); SelectionMask mask = PixelSelectionTransforms.Rotate(session.ActiveBuffer, session.Selection, degrees);
        session.CommitSelectionTransform($"Rotate Selection {degrees:0.##}°", before, mask);
    }

    private void Morph(bool grow)
    {
        int? amount = PromptInteger(grow ? "Grow Selection" : "Shrink Selection", "Pixels", 1); if (!amount.HasValue) return;
        SelectionMask mask = session.Selection.Clone(); if (grow) mask.Grow(amount.Value); else mask.Shrink(amount.Value); session.SetSelection(mask);
    }

    private void Border()
    {
        int? amount = PromptInteger("Selection Border", "Thickness", 1); if (!amount.HasValue) return; session.SetSelection(session.Selection.Border(amount.Value));
    }

    public void Feather()
    {
        int? amount = PromptInteger("Feather Selection", "Radius", 1); if (!amount.HasValue) return;
        SelectionMask mask = session.Selection.Clone(); mask.Feather(amount.Value); session.SetSelection(mask);
    }

    private void Flip(bool horizontal, bool vertical)
    {
        if (session.Selection.IsEmpty || session.ActiveLayer.Locked) return;
        PixelBuffer before = session.ActiveBuffer.Clone(); SelectionMask mask = PixelSelectionTransforms.Flip(session.ActiveBuffer, session.Selection, horizontal, vertical);
        session.CommitSelectionTransform(horizontal ? "Flip Selection Horizontally" : "Flip Selection Vertically", before, mask);
    }

    private void Rotate(QuarterTurn turn)
    {
        if (session.Selection.IsEmpty || session.ActiveLayer.Locked) return;
        PixelBuffer before = session.ActiveBuffer.Clone(); SelectionMask mask = PixelSelectionTransforms.RotateQuarter(session.ActiveBuffer, session.Selection, turn);
        session.CommitSelectionTransform("Rotate Selection", before, mask);
    }

    public void ScaleSelection()
    {
        IntRect? bounds = session.Selection.GetBounds(); if (bounds is null || session.ActiveLayer.Locked) return;
        using TextPromptDialog dialog = new("Scale Selection", "New size as WIDTHxHEIGHT", $"{bounds.Value.Width}x{bounds.Value.Height}");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string[] parts = dialog.Value.Split(['x', 'X', ',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out int width) || !int.TryParse(parts[1], out int height) || width <= 0 || height <= 0) return;
        PixelBuffer before = session.ActiveBuffer.Clone(); SelectionMask mask = PixelSelectionTransforms.Scale(session.ActiveBuffer, session.Selection, new IntRect(bounds.Value.X, bounds.Value.Y, width, height));
        session.CommitSelectionTransform("Scale Selection", before, mask);
    }

    private void TryAction(Action action)
    {
        try { action(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Selection operation", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    private int? PromptInteger(string title, string label, int initial)
    {
        using TextPromptDialog dialog = new(title, label, initial.ToString());
        return dialog.ShowDialog(this) == DialogResult.OK && int.TryParse(dialog.Value, out int value) && value > 0 ? value : null;
    }

    private static string FormatBounds(IntRect? bounds) => bounds is null ? "empty" : $"{bounds.Value.Width}×{bounds.Value.Height} at {bounds.Value.X},{bounds.Value.Y}";
    private static FlowLayoutPanel Flow() => new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 5, 0, 5) };
    private static void Add(FlowLayoutPanel panel, string text, Action action) { Button button = new() { Text = text, AutoSize = true }; button.Click += (_, _) => action(); panel.Controls.Add(button); }
    private static void AddRow(TableLayoutPanel table, int row, string label, Control control) { table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); table.Controls.Add(control, 1, row); }
}
