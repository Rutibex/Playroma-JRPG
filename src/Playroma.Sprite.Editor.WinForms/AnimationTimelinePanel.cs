using System.Diagnostics;
using Playroma.Sprite.Animation;
using Playroma.Sprite.Core;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class AnimationTimelinePanel : UserControl
{
    private readonly SpriteEditorSession session;
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        RowHeadersWidth = 180,
        ColumnHeadersHeight = 62,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        BackgroundColor = SystemColors.ControlDarkDark,
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly NumericUpDown durationBox = new() { Minimum = 1, Maximum = 60_000, Value = 100, Width = 72 };
    private readonly ToolStripButton playButton = new("▶");
    private readonly AnimationPlaybackController playback = new();
    private readonly System.Windows.Forms.Timer playbackTimer = new() { Interval = 15 };
    private readonly Stopwatch stopwatch = new();
    private readonly Dictionary<int, Bitmap> thumbnails = [];
    private bool refreshing;
    private SpriteAnimationTag? activeTag;

    public AnimationTimelinePanel(SpriteEditorSession editorSession)
    {
        session = editorSession ?? throw new ArgumentNullException(nameof(editorSession));
        Dock = DockStyle.Fill; MinimumSize = new Size(500, 170);
        ToolStrip strip = BuildToolbar();
        Controls.Add(grid); Controls.Add(strip);
        grid.CellClick += Grid_CellClick;
        grid.CellPainting += Grid_CellPainting;
        grid.SelectionChanged += (_, _) => RefreshDurationEditor();
        grid.KeyDown += Grid_KeyDown;
        session.Changed += (_, _) => RefreshGrid();
        playbackTimer.Tick += (_, _) => PlaybackTick();
        RefreshGrid();
    }

    public IReadOnlyList<int> SelectedFrameIndexes
    {
        get
        {
            int[] selected = grid.SelectedCells.Cast<DataGridViewCell>().Where(cell => cell.ColumnIndex >= 0).Select(cell => cell.ColumnIndex).Distinct().OrderBy(index => index).ToArray();
            return selected.Length == 0 ? [session.ActiveFrameIndex] : selected;
        }
    }

    public IReadOnlyList<CelSelection> SelectedCels => grid.SelectedCells.Cast<DataGridViewCell>()
        .Where(cell => cell.RowIndex >= 0 && cell.ColumnIndex >= 0 && grid.Rows[cell.RowIndex].Tag is Guid)
        .Select(cell => new CelSelection((Guid)grid.Rows[cell.RowIndex].Tag!, session.Document.Frames[cell.ColumnIndex].Id))
        .Where(selection => session.Document.Layers.First(layer => layer.Id == selection.LayerId).Cels.Any(cel => cel.FrameId == selection.FrameId))
        .Distinct().ToArray();

    public SpriteAnimationTag? ActiveTag => activeTag;
    public event EventHandler? ActiveFrameChanged;

    public void SetActiveTag(SpriteAnimationTag? tag)
    {
        activeTag = tag;
        playback.Configure(session.Document, activeTag);
        RefreshGrid();
    }

    public void TogglePlayback() => TogglePlaybackCore();
    public void PreviousFrame() => SelectFrame(Math.Max(0, session.ActiveFrameIndex - 1));
    public void NextFrame() => SelectFrame(Math.Min(session.Document.Frames.Count - 1, session.ActiveFrameIndex + 1));

    public void StopPlayback()
    {
        playback.Pause(); playbackTimer.Stop(); stopwatch.Reset(); playButton.Text = "▶";
    }

    private ToolStrip BuildToolbar()
    {
        ToolStrip strip = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, CanOverflow = true };
        Add(strip, "|◀", (_, _) => SelectFrame(0));
        Add(strip, "◀", (_, _) => SelectFrame(Math.Max(0, session.ActiveFrameIndex - 1)));
        playButton.Click += (_, _) => TogglePlaybackCore(); strip.Items.Add(playButton);
        Add(strip, "▶", (_, _) => SelectFrame(Math.Min(session.Document.Frames.Count - 1, session.ActiveFrameIndex + 1)));
        Add(strip, "▶|", (_, _) => SelectFrame(session.Document.Frames.Count - 1));
        strip.Items.Add(new ToolStripSeparator());
        Add(strip, "+ Empty", (_, _) => session.AddFrame(FrameInsertionMode.Empty));
        Add(strip, "+ Copy", (_, _) => session.AddFrame(FrameInsertionMode.CopyIndependent));
        Add(strip, "+ Linked", (_, _) => session.AddFrame(FrameInsertionMode.CopyLinked));
        Add(strip, "Duplicate", (_, _) => session.DuplicateFrame());
        Add(strip, "Delete", (_, _) => TryAction(() => session.RemoveFrames(SelectedFrameIndexes.ToArray())));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(new ToolStripLabel("Duration ms"));
        ToolStripControlHost durationHost = new(durationBox); strip.Items.Add(durationHost);
        Add(strip, "Apply", (_, _) => session.SetFrameDuration(SelectedFrameIndexes.ToArray(), Decimal.ToInt32(durationBox.Value)));
        Add(strip, "Reverse", (_, _) => ReverseSelected());
        Add(strip, "← Move", (_, _) => MoveSelected(-1));
        Add(strip, "Move →", (_, _) => MoveSelected(1));
        strip.Items.Add(new ToolStripSeparator());
        Add(strip, "Copy Cels...", (_, _) => TransferSelectedCels(move: false, linked: false));
        Add(strip, "Copy Linked...", (_, _) => TransferSelectedCels(move: false, linked: true));
        Add(strip, "Move Cels...", (_, _) => TransferSelectedCels(move: true, linked: false));
        Add(strip, "Link Cels", (_, _) => TryAction(() => session.LinkCels(SelectedCels.ToArray())));
        Add(strip, "Unlink", (_, _) => TryAction(() => session.UnlinkCels(SelectedCels.ToArray())));
        Add(strip, "Clear Cels", (_, _) => TryAction(() => session.ClearCels(SelectedCels.ToArray())));
        return strip;
    }

    private void RefreshGrid()
    {
        if (IsDisposed || refreshing) return;
        refreshing = true;
        try
        {
            DisposeThumbnails();
            grid.SuspendLayout(); grid.Columns.Clear(); grid.Rows.Clear();
            foreach (SpriteFrame frame in session.Document.Frames.OrderBy(frame => frame.Number))
            {
                DataGridViewTextBoxColumn column = new() { Width = 72, SortMode = DataGridViewColumnSortMode.NotSortable, HeaderText = $"{frame.Number + 1}\n{frame.DurationMilliseconds} ms", Tag = frame.Id };
                grid.Columns.Add(column);
            }
            foreach (SpriteLayer layer in session.Document.Layers.Where(layer => layer.Kind != SpriteLayerKind.Group).Reverse())
            {
                int rowIndex = grid.Rows.Add(); DataGridViewRow row = grid.Rows[rowIndex]; row.Tag = layer.Id;
                row.HeaderCell.Value = $"{(layer.Visible ? "●" : "○")} {layer.Name}";
                for (int frameIndex = 0; frameIndex < session.Document.Frames.Count; frameIndex++)
                {
                    Guid frameId = session.Document.Frames[frameIndex].Id;
                    SpriteCel? cel = layer.Cels.FirstOrDefault(item => item.FrameId == frameId);
                    if (cel is null) { row.Cells[frameIndex].Value = string.Empty; continue; }
                    int linkedCount = session.Document.Layers.SelectMany(item => item.Cels).Count(item => item.PixelContentId == cel.PixelContentId);
                    row.Cells[frameIndex].Value = linkedCount > 1 ? "◆" : "●";
                    row.Cells[frameIndex].ToolTipText = linkedCount > 1 ? $"Linked cel shared by {linkedCount} frames/layers" : "Independent cel";
                    row.Cells[frameIndex].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                }
            }
            HighlightActiveTag();
            int activeColumn = Math.Clamp(session.ActiveFrameIndex, 0, grid.Columns.Count - 1);
            int activeRow = grid.Rows.Cast<DataGridViewRow>().ToList().FindIndex(row => row.Tag is Guid id && id == session.SelectedLayerId);
            if (activeRow < 0) activeRow = grid.Rows.Cast<DataGridViewRow>().ToList().FindIndex(row => row.Tag is Guid id && id == session.ActiveLayerId);
            if (activeRow >= 0 && activeColumn >= 0)
            {
                grid.CurrentCell = grid.Rows[activeRow].Cells[activeColumn];
                grid.Rows[activeRow].Cells[activeColumn].Selected = true;
            }
            RefreshDurationEditor();
        }
        finally { grid.ResumeLayout(); refreshing = false; }
    }

    private void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (refreshing || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (grid.Rows[e.RowIndex].Tag is Guid layerId) session.SetActiveLayer(layerId);
        session.SetActiveFrame(e.ColumnIndex); ActiveFrameChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex != -1 || e.ColumnIndex < 0) return;
        e.PaintBackground(e.CellBounds, true);
        try
        {
            if (!thumbnails.TryGetValue(e.ColumnIndex, out Bitmap? thumbnail))
            {
                using Bitmap original = PixelBufferBitmap.ToBitmap(session.CompositeFrame(e.ColumnIndex));
                thumbnail = new Bitmap(original, new Size(32, 32)); thumbnails[e.ColumnIndex] = thumbnail;
            }
            Rectangle imageRect = new(e.CellBounds.X + (e.CellBounds.Width - 32) / 2, e.CellBounds.Y + 2, 32, 32);
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            e.Graphics.DrawImage(thumbnail, imageRect);
            string text = $"{e.ColumnIndex + 1}  {session.Document.Frames[e.ColumnIndex].DurationMilliseconds}ms";
            TextRenderer.DrawText(e.Graphics, text, grid.Font, new Rectangle(e.CellBounds.X + 1, e.CellBounds.Y + 37, e.CellBounds.Width - 2, 22), grid.ColumnHeadersDefaultCellStyle.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
        catch { e.PaintContent(e.CellBounds); }
        e.Handled = true;
    }

    private void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete) { TryAction(() => session.ClearCels(SelectedCels.ToArray())); e.Handled = true; }
        else if (e.KeyCode == Keys.Space) { TogglePlaybackCore(); e.Handled = true; }
    }

    private void TogglePlaybackCore()
    {
        if (playback.Sequence.Count == 0) playback.Configure(session.Document, activeTag);
        playback.Toggle();
        if (playback.IsPlaying) { playback.SeekFrame(session.ActiveFrameIndex); stopwatch.Restart(); playbackTimer.Start(); playButton.Text = "❚❚"; }
        else StopPlayback();
    }

    private void PlaybackTick()
    {
        double elapsed = stopwatch.Elapsed.TotalMilliseconds; stopwatch.Restart();
        if (playback.Advance(session.Document, elapsed)) SelectFrame(playback.CurrentFrameIndex, stopPlayback: false);
        if (!playback.IsPlaying) StopPlayback();
    }

    private void SelectFrame(int index, bool stopPlayback = true)
    {
        if (stopPlayback) StopPlayback();
        session.SetActiveFrame(index); ActiveFrameChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReverseSelected()
    {
        IReadOnlyList<int> frames = SelectedFrameIndexes;
        if (frames.Count == 0) return;
        session.ReverseFrames(frames.Min(), frames.Max());
    }

    private void MoveSelected(int direction)
    {
        int[] frames = SelectedFrameIndexes.ToArray();
        if (frames.Length == 0) return;
        int destination = direction < 0 ? Math.Max(0, frames.Min() - 1) : Math.Min(session.Document.Frames.Count, frames.Max() + 2);
        session.MoveFrames(frames, destination);
    }


    private void TransferSelectedCels(bool move, bool linked)
    {
        CelSelection[] selected = SelectedCels.ToArray();
        if (selected.Length == 0) return;
        Dictionary<Guid, int> frameIndexes = session.Document.Frames.Select((frame, index) => (Id: frame.Id, Index: index)).ToDictionary(item => item.Id, item => item.Index);
        int[] sourceFrames = selected.Select(item => frameIndexes[item.FrameId]).Distinct().OrderBy(index => index).ToArray();
        Guid[] layers = selected.Select(item => item.LayerId).Distinct().ToArray();
        int maximumStart = session.Document.Frames.Count - sourceFrames.Length;
        using TextPromptDialog dialog = new(move ? "Move Cels" : linked ? "Copy Linked Cels" : "Copy Cels", $"Destination first frame (1-{maximumStart + 1})", (Math.Min(maximumStart, sourceFrames[0] + 1) + 1).ToString());
        if (dialog.ShowDialog(this) != DialogResult.OK || !int.TryParse(dialog.Value, out int oneBased)) return;
        int destination = oneBased - 1;
        if (destination < 0 || destination > maximumStart)
        {
            MessageBox.Show(this, "The destination range does not fit in the timeline.", "Cel transfer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        TryAction(() =>
        {
            if (move) session.MoveCels(layers, sourceFrames, destination);
            else session.CopyCels(layers, sourceFrames, destination, linked);
        });
    }

    private void RefreshDurationEditor()
    {
        if (session.Document.Frames.Count == 0) return;
        int first = SelectedFrameIndexes.FirstOrDefault(session.ActiveFrameIndex);
        durationBox.Value = Math.Clamp(session.Document.Frames[first].DurationMilliseconds, (int)durationBox.Minimum, (int)durationBox.Maximum);
    }

    private void HighlightActiveTag()
    {
        if (activeTag is null) return;
        Color color;
        try { color = string.IsNullOrWhiteSpace(activeTag.Color) ? Color.FromArgb(46, 73, 105) : ColorTranslator.FromHtml(activeTag.Color); }
        catch { color = Color.FromArgb(46, 73, 105); }
        Color background = grid.DefaultCellStyle.BackColor.IsEmpty ? SystemColors.Window : grid.DefaultCellStyle.BackColor;
        Color highlight = Color.FromArgb((color.R + background.R * 2) / 3, (color.G + background.G * 2) / 3, (color.B + background.B * 2) / 3);
        for (int column = activeTag.FromFrame; column <= activeTag.ToFrame && column < grid.Columns.Count; column++)
            foreach (DataGridViewRow row in grid.Rows) row.Cells[column].Style.BackColor = highlight;
    }

    private void DisposeThumbnails()
    {
        foreach (Bitmap bitmap in thumbnails.Values) bitmap.Dispose(); thumbnails.Clear();
    }

    private void TryAction(Action action)
    {
        try { action(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Animation timeline", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    private static ToolStripButton Add(ToolStrip strip, string text, EventHandler click)
    {
        ToolStripButton button = new(text) { DisplayStyle = ToolStripItemDisplayStyle.Text }; button.Click += click; strip.Items.Add(button); return button;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { playbackTimer.Dispose(); DisposeThumbnails(); }
        base.Dispose(disposing);
    }
}
