using Playroma.JRPG.AnimationEditing;

namespace Playroma.JRPG.Editor.WinForms.Database;

internal sealed class AnimationFrameSelectedEventArgs(int frameIndex, bool additive) : EventArgs
{
    public int FrameIndex { get; } = frameIndex;
    public bool Additive { get; } = additive;
}

internal sealed class AnimationTimelineControl : ScrollableControl
{
    private const int FrameWidth = 94;
    private const int FrameHeight = 82;
    private MvAnimationSnapshot? _animation;
    private Bitmap? _sheet1;
    private Bitmap? _sheet2;
    private readonly HashSet<int> _selectedFrames = [];
    private int _currentFrame;

    public AnimationTimelineControl()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(42, 45, 52);
        ForeColor = Color.Gainsboro;
        Height = 122;
        TabStop = true;
    }

    public event EventHandler<AnimationFrameSelectedEventArgs>? FrameSelected;
    public event EventHandler<AnimationFrameSelectedEventArgs>? FrameActivated;

    public int CurrentFrame
    {
        get => _currentFrame;
        set
        {
            _currentFrame = Math.Clamp(value, 0, Math.Max(0, (_animation?.Frames.Count ?? 1) - 1));
            if (_selectedFrames.Count == 0) _selectedFrames.Add(_currentFrame);
            EnsureVisible(_currentFrame);
            Invalidate();
        }
    }

    public IReadOnlyCollection<int> SelectedFrames => _selectedFrames;

    public void SetAnimation(MvAnimationSnapshot? animation, Bitmap? sheet1, Bitmap? sheet2)
    {
        _animation = animation;
        _sheet1 = sheet1;
        _sheet2 = sheet2;
        _selectedFrames.RemoveWhere(index => animation is null || index >= animation.Frames.Count);
        if (animation is not null && animation.Frames.Count > 0 && _selectedFrames.Count == 0) _selectedFrames.Add(Math.Clamp(CurrentFrame, 0, animation.Frames.Count - 1));
        AutoScrollMinSize = new Size(Math.Max(1, animation?.Frames.Count ?? 0) * FrameWidth + 12, FrameHeight + 24);
        Invalidate();
    }

    public void SelectFrame(int frameIndex, bool additive)
    {
        if (_animation is null || frameIndex < 0 || frameIndex >= _animation.Frames.Count) return;
        if (!additive) _selectedFrames.Clear();
        if (additive && _selectedFrames.Contains(frameIndex)) _selectedFrames.Remove(frameIndex);
        else _selectedFrames.Add(frameIndex);
        _currentFrame = frameIndex;
        EnsureVisible(frameIndex);
        FrameSelected?.Invoke(this, new AnimationFrameSelectedEventArgs(frameIndex, additive));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        if (_animation is null) return;
        using Pen normal = new(Color.FromArgb(100, 180, 190, 205));
        using Pen selected = new(Color.Gold, 3f);
        using Pen current = new(Color.Cyan, 2f);
        using SolidBrush background = new(Color.FromArgb(60, 64, 74));
        using SolidBrush text = new(Color.WhiteSmoke);
        using SolidBrush sound = new(Color.MediumSeaGreen);
        using SolidBrush flash = new(Color.OrangeRed);
        for (int index = 0; index < _animation.Frames.Count; index++)
        {
            Rectangle frameBox = new(index * FrameWidth + 4, 4, FrameWidth - 8, FrameHeight - 8);
            e.Graphics.FillRectangle(background, frameBox);
            DrawThumbnail(e.Graphics, _animation.Frames[index], frameBox);
            e.Graphics.DrawRectangle(_selectedFrames.Contains(index) ? selected : index == CurrentFrame ? current : normal, frameBox);
            e.Graphics.DrawString((index + 1).ToString(), Font, text, frameBox.Left + 3, frameBox.Top + 3);
            bool hasSound = _animation.Timings.Any(timing => timing.Frame == index && !string.IsNullOrWhiteSpace(timing.SoundEffect?.Name));
            bool hasFlash = _animation.Timings.Any(timing => timing.Frame == index && timing.FlashScope > 0);
            if (hasSound) e.Graphics.FillEllipse(sound, frameBox.Left + 4, frameBox.Bottom - 12, 8, 8);
            if (hasFlash) e.Graphics.FillEllipse(flash, frameBox.Left + 15, frameBox.Bottom - 12, 8, 8);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int x = e.X - AutoScrollPosition.X;
        int frame = x / FrameWidth;
        bool additive = (ModifierKeys & Keys.Control) != 0;
        SelectFrame(frame, additive);
        if (e.Clicks > 1) FrameActivated?.Invoke(this, new AnimationFrameSelectedEventArgs(frame, additive));
    }

    private void DrawThumbnail(Graphics graphics, MvAnimationFrameSnapshot frame, Rectangle bounds)
    {
        int centerX = bounds.Left + bounds.Width / 2;
        int centerY = bounds.Top + bounds.Height / 2 + 4;
        foreach (MvAnimationCellSnapshot cell in frame.Cells.Take(MvAnimationConstants.RuntimeCellCount).Where(cell => cell.IsVisible))
        {
            MvAnimationPatternCell pattern = MvAnimationSheetMapper.Map(cell.Pattern);
            Bitmap? sheet = pattern.UsesSecondSheet ? _sheet2 : _sheet1;
            if (sheet is null || pattern.SourceX + pattern.Width > sheet.Width || pattern.SourceY + pattern.Height > sheet.Height) continue;
            float scale = Math.Clamp((float)(cell.Scale / 100d * 0.18), 0.04f, 0.5f);
            int size = Math.Max(6, (int)Math.Round(MvAnimationConstants.CellSize * scale));
            Rectangle destination = new(centerX + (int)(cell.X * 0.12) - size / 2, centerY + (int)(cell.Y * 0.12) - size / 2, size, size);
            graphics.DrawImage(sheet, destination, new Rectangle(pattern.SourceX, pattern.SourceY, pattern.Width, pattern.Height), GraphicsUnit.Pixel);
        }
    }

    private void EnsureVisible(int frameIndex)
    {
        int left = frameIndex * FrameWidth;
        int viewportLeft = -AutoScrollPosition.X;
        if (left < viewportLeft) AutoScrollPosition = new Point(left, 0);
        else if (left + FrameWidth > viewportLeft + ClientSize.Width) AutoScrollPosition = new Point(left + FrameWidth - ClientSize.Width, 0);
    }
}
