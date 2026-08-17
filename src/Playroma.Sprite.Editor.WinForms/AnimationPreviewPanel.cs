using System.Diagnostics;
using Playroma.Sprite.Animation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;
using Playroma.Sprite.RpgMakerMV;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class AnimationPreviewPanel : UserControl
{
    private readonly SpriteEditorSession session;
    private readonly AnimationInspectorPanel inspector;
    private readonly PictureBox picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(38, 40, 46) };
    private readonly ComboBox modeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly ComboBox itemBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox characterBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly NumericUpDown speedBox = new() { Minimum = 5, Maximum = 800, Value = 100, Increment = 5, Width = 65 };
    private readonly CheckBox loopBox = new() { Text = "Loop", Checked = true, AutoSize = true };
    private readonly Button playButton = new() { Text = "▶", AutoSize = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private readonly Stopwatch stopwatch = new();
    private int sequenceCursor;
    private double elapsed;
    private bool playing;
    private bool suppressRebuild;
    private IReadOnlyList<int> frameSequence = [];
    private IReadOnlyList<IntRect> cropSequence = [];

    public AnimationPreviewPanel(SpriteEditorSession editorSession, AnimationInspectorPanel animationInspector)
    {
        session = editorSession ?? throw new ArgumentNullException(nameof(editorSession)); inspector = animationInspector ?? throw new ArgumentNullException(nameof(animationInspector));
        Dock = DockStyle.Fill;
        modeBox.Items.AddRange(["Document Animation", "RPG Maker MV Motion"]); modeBox.SelectedIndex = 0;
        for (int index = 0; index < 8; index++) characterBox.Items.Add($"Character {index + 1}"); characterBox.SelectedIndex = 0;
        FlowLayoutPanel commands = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), WrapContents = true };
        commands.Controls.Add(new Label { Text = "Preview", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }); commands.Controls.Add(modeBox);
        commands.Controls.Add(itemBox); commands.Controls.Add(characterBox); commands.Controls.Add(playButton); commands.Controls.Add(loopBox);
        commands.Controls.Add(new Label { Text = "Speed %", AutoSize = true, Margin = new Padding(8, 8, 3, 3) }); commands.Controls.Add(speedBox);
        Controls.Add(picture); Controls.Add(commands);
        itemBox.FormattingEnabled = true;
        itemBox.Format += (_, e) => e.Value = e.ListItem switch
        {
            SpriteAnimationTag tag => tag.Name,
            MvPreviewMotion motion => motion.DisplayName,
            _ => e.ListItem?.ToString() ?? string.Empty
        };
        modeBox.SelectedIndexChanged += (_, _) => RebuildSequence(); itemBox.SelectedIndexChanged += (_, _) => { if (!suppressRebuild) RebuildSequence(false); }; characterBox.SelectedIndexChanged += (_, _) => RebuildSequence();
        playButton.Click += (_, _) => Toggle(); loopBox.CheckedChanged += (_, _) => { };
        timer.Tick += (_, _) => TickPreview();
        session.Changed += (_, _) => { if (!playing) RebuildSequence(); };
        inspector.ActiveTagChanged += (_, _) => RebuildSequence();
        RebuildSequence();
    }

    private void RebuildSequence(bool rebuildItems = true)
    {
        if (suppressRebuild) return;
        bool mv = modeBox.SelectedIndex == 1;
        characterBox.Visible = mv && string.Equals(session.Document.Metadata.RpgMakerMvProfileId, "mv-character-standard", StringComparison.OrdinalIgnoreCase);
        if (rebuildItems)
        {
            suppressRebuild = true;
            itemBox.BeginUpdate();
            try
            {
                string? priorId = itemBox.SelectedItem switch
                {
                    SpriteAnimationTag tag => "tag:" + tag.Id,
                    MvPreviewMotion motion => "motion:" + motion.Id,
                    string value => "text:" + value,
                    _ => null
                };
                itemBox.Items.Clear();
                if (!mv)
                {
                    itemBox.Items.Add("All Frames");
                    foreach (SpriteAnimationTag tag in session.Document.Tags) itemBox.Items.Add(tag);
                }
                else
                {
                    IReadOnlyList<MvPreviewMotion> motions = MvAnimationPreview.GetMotions(session.Document.Metadata.RpgMakerMvProfileId, characterBox.SelectedIndex);
                    foreach (MvPreviewMotion motion in motions) itemBox.Items.Add(motion);
                }
                int priorIndex = -1;
                if (priorId is not null)
                    for (int index = 0; index < itemBox.Items.Count; index++)
                    {
                        string id = itemBox.Items[index] switch
                        {
                            SpriteAnimationTag tag => "tag:" + tag.Id,
                            MvPreviewMotion motion => "motion:" + motion.Id,
                            string value => "text:" + value,
                            _ => string.Empty
                        };
                        if (id == priorId) { priorIndex = index; break; }
                    }
                if (itemBox.Items.Count > 0) itemBox.SelectedIndex = priorIndex >= 0 ? priorIndex : 0;
            }
            finally { itemBox.EndUpdate(); suppressRebuild = false; }
        }

        if (!mv)
        {
            SpriteAnimationTag? tag = itemBox.SelectedItem as SpriteAnimationTag;
            frameSequence = tag is null ? Enumerable.Range(0, session.Document.Frames.Count).ToArray() : AnimationSequenceBuilder.Build(tag);
            cropSequence = [];
        }
        else if (itemBox.SelectedItem is MvPreviewMotion motion)
        {
            cropSequence = MvAnimationPreview.ResolveSourceRects(session.Document.Metadata.RpgMakerMvProfileId, session.Document.Canvas.Width, session.Document.Canvas.Height, motion);
            frameSequence = [];
        }
        else { frameSequence = []; cropSequence = []; }
        sequenceCursor = 0; elapsed = 0; RenderCurrent();
    }

    private void Toggle()
    {
        playing = !playing;
        if (playing) { stopwatch.Restart(); timer.Start(); playButton.Text = "❚❚"; }
        else Stop();
    }

    private void Stop()
    {
        playing = false; timer.Stop(); stopwatch.Reset(); playButton.Text = "▶";
    }

    private void TickPreview()
    {
        double delta = stopwatch.Elapsed.TotalMilliseconds * Decimal.ToDouble(speedBox.Value) / 100d; stopwatch.Restart(); elapsed += delta;
        int duration = CurrentDuration();
        if (elapsed < duration) return;
        elapsed %= Math.Max(1, duration); sequenceCursor++;
        int count = Math.Max(frameSequence.Count, cropSequence.Count);
        if (sequenceCursor >= count)
        {
            if (loopBox.Checked) sequenceCursor = 0;
            else { sequenceCursor = Math.Max(0, count - 1); Stop(); }
        }
        RenderCurrent();
    }

    private int CurrentDuration()
    {
        if (frameSequence.Count > 0) return session.Document.Frames[frameSequence[Math.Clamp(sequenceCursor, 0, frameSequence.Count - 1)]].DurationMilliseconds;
        if (itemBox.SelectedItem is MvPreviewMotion motion) return motion.DefaultFrameMilliseconds;
        return 100;
    }

    private void RenderCurrent()
    {
        Image? old = picture.Image;
        if (frameSequence.Count > 0)
        {
            int frameIndex = frameSequence[Math.Clamp(sequenceCursor, 0, frameSequence.Count - 1)];
            picture.Image = PixelBufferBitmap.ToBitmap(session.CompositeFrame(frameIndex));
        }
        else if (cropSequence.Count > 0)
        {
            IntRect source = cropSequence[Math.Clamp(sequenceCursor, 0, cropSequence.Count - 1)];
            picture.Image = PixelBufferBitmap.Crop(session.CompositeCurrentFrame(), source);
        }
        else picture.Image = null;
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); picture.Image?.Dispose(); }
        base.Dispose(disposing);
    }
}
