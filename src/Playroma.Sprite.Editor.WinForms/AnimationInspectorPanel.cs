using Playroma.Sprite.Animation;
using Playroma.Sprite.Core;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class AnimationInspectorPanel : UserControl
{
    private readonly SpriteEditorSession session;
    private readonly AnimationTimelinePanel timeline;
    private readonly ListBox tagList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly CheckBox onionEnabled = Check("Enable onion skin", true);
    private readonly NumericUpDown previousBox = Number(0, 20, 1);
    private readonly NumericUpDown nextBox = Number(0, 20, 1);
    private readonly NumericUpDown onionOpacity = Number(1, 255, 96);
    private readonly CheckBox tintBox = Check("Tint previous and next frames", true);
    private readonly CheckBox restrictTagBox = Check("Restrict onion skin to active tag");
    private readonly CheckBox referencesBox = Check("Include reference layers");
    private readonly Button previousColor = new() { Text = "Previous tint", Dock = DockStyle.Fill };
    private readonly Button nextColor = new() { Text = "Next tint", Dock = DockStyle.Fill };
    private bool suppress;

    public AnimationInspectorPanel(SpriteEditorSession editorSession, AnimationTimelinePanel timelinePanel)
    {
        session = editorSession ?? throw new ArgumentNullException(nameof(editorSession));
        timeline = timelinePanel ?? throw new ArgumentNullException(nameof(timelinePanel));
        Dock = DockStyle.Fill;
        OnionSettings = new OnionSkinSettings();
        Controls.Add(BuildLayout());
        tagList.SelectedIndexChanged += (_, _) => SelectTag();
        previousColor.Click += (_, _) => PickTint(true);
        nextColor.Click += (_, _) => PickTint(false);
        foreach (CheckBox box in new[] { onionEnabled, tintBox, restrictTagBox, referencesBox }) box.CheckedChanged += (_, _) => ApplySettings();
        foreach (NumericUpDown box in new[] { previousBox, nextBox, onionOpacity }) box.ValueChanged += (_, _) => ApplySettings();
        session.Changed += (_, _) => RefreshTags();
        RefreshTintButtons(); RefreshTags(); ApplySettings();
    }

    public OnionSkinSettings OnionSettings { get; }
    public SpriteAnimationTag? SelectedTag => tagList.SelectedItem as SpriteAnimationTag;
    public event EventHandler? OnionSettingsChanged;
    public event EventHandler? ActiveTagChanged;

    private Control BuildLayout()
    {
        SplitContainer split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 230, FixedPanel = FixedPanel.Panel1 };
        GroupBox tags = new() { Text = "Animation Tags", Dock = DockStyle.Fill, Padding = new Padding(6) };
        FlowLayoutPanel commands = new() { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true };
        commands.Controls.Add(Button("Add", (_, _) => AddTag())); commands.Controls.Add(Button("Edit", (_, _) => EditTag())); commands.Controls.Add(Button("Delete", (_, _) => DeleteTag()));
        tags.Controls.Add(tagList); tags.Controls.Add(commands); split.Panel1.Controls.Add(tags);

        TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        int row = 0; AddFull(table, ref row, onionEnabled); AddRow(table, ref row, "Previous frames", previousBox); AddRow(table, ref row, "Next frames", nextBox);
        AddRow(table, ref row, "Base opacity", onionOpacity); AddFull(table, ref row, tintBox); AddRow(table, ref row, "Previous", previousColor); AddRow(table, ref row, "Next", nextColor);
        AddFull(table, ref row, restrictTagBox); AddFull(table, ref row, referencesBox);
        GroupBox onion = new() { Text = "Onion Skin", Dock = DockStyle.Fill }; Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(table); onion.Controls.Add(scroll); split.Panel2.Controls.Add(onion);
        return split;
    }

    private void AddTag()
    {
        IReadOnlyList<int> selected = timeline.SelectedFrameIndexes;
        int from = selected.Count == 0 ? session.ActiveFrameIndex : selected.Min();
        int to = selected.Count == 0 ? session.ActiveFrameIndex : selected.Max();
        SpriteAnimationTag seed = new() { Name = "Animation", FromFrame = from, ToFrame = to, Direction = AnimationPlaybackDirection.Forward };
        using AnimationTagDialog dialog = new(session.Document.Frames.Count, seed);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SpriteAnimationTag tag = session.AddTag(dialog.TagName, dialog.FromFrame, dialog.ToFrame, dialog.Direction, dialog.TagColor);
        RefreshTags(); tagList.SelectedItem = tag;
    }

    private void EditTag()
    {
        if (SelectedTag is not SpriteAnimationTag tag) return;
        using AnimationTagDialog dialog = new(session.Document.Frames.Count, tag);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        session.UpdateTag(tag.Id, dialog.TagName, dialog.FromFrame, dialog.ToFrame, dialog.Direction, dialog.TagColor);
    }

    private void DeleteTag()
    {
        if (SelectedTag is not SpriteAnimationTag tag) return;
        session.DeleteTag(tag.Id); timeline.SetActiveTag(null); ActiveTagChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshTags()
    {
        if (IsDisposed || suppress) return;
        Guid? selectedId = SelectedTag?.Id;
        suppress = true;
        try
        {
            tagList.BeginUpdate(); tagList.Items.Clear();
            foreach (SpriteAnimationTag tag in session.Document.Tags.OrderBy(tag => tag.FromFrame).ThenBy(tag => tag.Name)) tagList.Items.Add(tag);
            tagList.DisplayMember = nameof(SpriteAnimationTag.Name);
            if (selectedId is Guid id) tagList.SelectedItem = session.Document.Tags.FirstOrDefault(tag => tag.Id == id);
            if (tagList.SelectedIndex < 0 && tagList.Items.Count > 0) tagList.SelectedIndex = 0;
        }
        finally { tagList.EndUpdate(); suppress = false; }
        SelectTag();
    }

    private void SelectTag()
    {
        if (suppress) return;
        timeline.SetActiveTag(SelectedTag); ActiveTagChanged?.Invoke(this, EventArgs.Empty); OnionSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySettings()
    {
        if (suppress) return;
        OnionSettings.Enabled = onionEnabled.Checked;
        OnionSettings.PreviousFrames = Decimal.ToInt32(previousBox.Value);
        OnionSettings.NextFrames = Decimal.ToInt32(nextBox.Value);
        OnionSettings.BaseOpacity = (byte)onionOpacity.Value;
        OnionSettings.TintFrames = tintBox.Checked;
        OnionSettings.RestrictToTag = restrictTagBox.Checked;
        OnionSettings.IncludeReferenceLayers = referencesBox.Checked;
        OnionSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PickTint(bool previous)
    {
        Playroma.Sprite.Pixel.Rgba32 source = previous ? OnionSettings.PreviousTint : OnionSettings.NextTint;
        using ColorDialog dialog = new() { FullOpen = true, Color = Color.FromArgb(source.R, source.G, source.B) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Playroma.Sprite.Pixel.Rgba32 value = new(dialog.Color.R, dialog.Color.G, dialog.Color.B, 255);
        if (previous) OnionSettings.PreviousTint = value; else OnionSettings.NextTint = value;
        RefreshTintButtons(); OnionSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshTintButtons()
    {
        SetButtonColor(previousColor, OnionSettings.PreviousTint); SetButtonColor(nextColor, OnionSettings.NextTint);
    }

    private static void SetButtonColor(Button button, Playroma.Sprite.Pixel.Rgba32 value)
    {
        button.BackColor = Color.FromArgb(value.R, value.G, value.B); button.ForeColor = button.BackColor.GetBrightness() < .5f ? Color.White : Color.Black;
    }

    private static Button Button(string text, EventHandler handler) { Button button = new() { Text = text, AutoSize = true }; button.Click += handler; return button; }
    private static CheckBox Check(string text, bool value = false) => new() { Text = text, Checked = value, AutoSize = true, Dock = DockStyle.Fill };
    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value) => new() { Minimum = minimum, Maximum = maximum, Value = value, Dock = DockStyle.Fill };
    private static void AddRow(TableLayoutPanel table, ref int row, string label, Control control) { table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 6, 3) }, 0, row); table.Controls.Add(control, 1, row++); }
    private static void AddFull(TableLayoutPanel table, ref int row, Control control) { table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(control, 0, row); table.SetColumnSpan(control, 2); row++; }
}
