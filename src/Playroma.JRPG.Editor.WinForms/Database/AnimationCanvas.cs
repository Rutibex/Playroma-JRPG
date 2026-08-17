using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Playroma.JRPG.AnimationEditing;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class AnimationCellsDragEventArgs(IReadOnlyCollection<int> lanes, double deltaX, double deltaY) : EventArgs
{
    public IReadOnlyCollection<int> Lanes { get; } = lanes;
    public double DeltaX { get; } = deltaX;
    public double DeltaY { get; } = deltaY;
}

public sealed class AnimationCanvas : Control
{
    private MvAnimationSnapshot? _animation;
    private Bitmap? _sheet1;
    private Bitmap? _sheet2;
    private int _frameIndex;
    private readonly HashSet<int> _selected = [];
    private readonly Dictionary<int, RectangleF> _hitBoxes = [];
    private Point _dragOrigin;
    private Point _dragCurrent;
    private bool _dragging;

    public AnimationCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(35, 38, 45);
        ForeColor = Color.Gainsboro;
        TabStop = true;
        PreviewTarget = new AnimationPreviewTarget(330, 250, 156, 260);
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler<AnimationCellsDragEventArgs>? CellsDragCompleted;

    public AnimationPreviewTarget PreviewTarget { get; set; }
    public bool ShowGrid { get; set; } = true;
    public bool ShowPreviousOnion { get; set; }
    public bool ShowNextOnion { get; set; }
    public double OnionOpacity { get; set; } = 0.2;
    public IReadOnlyCollection<int> SelectedLanes => _selected;

    public int FrameIndex
    {
        get => _frameIndex;
        set
        {
            int maximum = Math.Max(0, (_animation?.Frames.Count ?? 1) - 1);
            _frameIndex = Math.Clamp(value, 0, maximum);
            _hitBoxes.Clear();
            Invalidate();
        }
    }

    public void SetAnimation(MvAnimationSnapshot? animation, Bitmap? sheet1, Bitmap? sheet2)
    {
        _animation = animation;
        _sheet1 = sheet1;
        _sheet2 = sheet2;
        FrameIndex = Math.Min(FrameIndex, Math.Max(0, (animation?.Frames.Count ?? 1) - 1));
        _selected.RemoveWhere(lane => animation is null || FrameIndex >= animation.Frames.Count || lane >= animation.Frames[FrameIndex].Cells.Count);
        Invalidate();
    }

    public void SelectLane(int lane, bool additive = false)
    {
        if (!additive) _selected.Clear();
        if (lane >= 0)
        {
            if (additive && _selected.Contains(lane)) _selected.Remove(lane);
            else _selected.Add(lane);
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        using Bitmap buffer = new(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(buffer))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawBackground(graphics);
            if (_animation is null || _animation.Frames.Count == 0)
            {
                TextRenderer.DrawText(graphics, "This animation has no frames.", Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                double scale = Math.Min(ClientSize.Width / (double)PreviewTarget.LogicalWidth, ClientSize.Height / (double)PreviewTarget.LogicalHeight);
                float offsetX = (float)((ClientSize.Width - PreviewTarget.LogicalWidth * scale) / 2d);
                float offsetY = (float)((ClientSize.Height - PreviewTarget.LogicalHeight * scale) / 2d);
                if (!CurrentTimings().Any(timing => timing.FlashScope == (int)MvAnimationFlashScope.HideTarget))
                {
                    DrawTarget(graphics, scale, offsetX, offsetY);
                }
                if (ShowPreviousOnion && FrameIndex > 0) DrawFrame(graphics, buffer, FrameIndex - 1, scale, offsetX, offsetY, OnionOpacity, false);
                if (ShowNextOnion && FrameIndex + 1 < _animation.Frames.Count) DrawFrame(graphics, buffer, FrameIndex + 1, scale, offsetX, offsetY, OnionOpacity, false);
                _hitBoxes.Clear();
                DrawFrame(graphics, buffer, FrameIndex, scale, offsetX, offsetY, 1d, true);
                DrawTimingOverlays(graphics, scale, offsetX, offsetY);
                DrawBaseAnchor(graphics, scale, offsetX, offsetY);
            }
        }
        e.Graphics.DrawImageUnscaled(buffer, 0, 0);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int lane = HitTest(e.Location);
        bool additive = (ModifierKeys & Keys.Control) != 0;
        SelectLane(lane, additive);
        if (lane >= 0 && e.Button == MouseButtons.Left)
        {
            _dragOrigin = e.Location;
            _dragCurrent = e.Location;
            _dragging = true;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        _dragCurrent = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        Capture = false;
        double scale = Math.Min(ClientSize.Width / (double)PreviewTarget.LogicalWidth, ClientSize.Height / (double)PreviewTarget.LogicalHeight);
        if (scale <= 0) return;
        double dx = (_dragCurrent.X - _dragOrigin.X) / scale;
        double dy = (_dragCurrent.Y - _dragOrigin.Y) / scale;
        if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
        {
            CellsDragCompleted?.Invoke(this, new AnimationCellsDragEventArgs(_selected.ToArray(), dx, dy));
        }
        Invalidate();
    }

    private void DrawBackground(Graphics graphics)
    {
        graphics.Clear(BackColor);
        if (!ShowGrid) return;
        using Pen pen = new(Color.FromArgb(30, 210, 220, 230));
        for (int x = 0; x < Width; x += 32) graphics.DrawLine(pen, x, 0, x, Height);
        for (int y = 0; y < Height; y += 32) graphics.DrawLine(pen, 0, y, Width, y);
    }

    private void DrawTarget(Graphics graphics, double scale, float offsetX, float offsetY)
    {
        RectangleF target = LogicalToScreen(new RectangleF((float)PreviewTarget.X, (float)PreviewTarget.Y, (float)PreviewTarget.Width, (float)PreviewTarget.Height), scale, offsetX, offsetY);
        using SolidBrush fill = new(Color.FromArgb(35, 100, 180, 240));
        using Pen outline = new(Color.FromArgb(150, 100, 180, 240), 2f);
        graphics.FillEllipse(fill, target);
        graphics.DrawEllipse(outline, target);
        TextRenderer.DrawText(graphics, "Preview Target", Font, Rectangle.Round(target), Color.FromArgb(180, 220, 235, 250), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawFrame(Graphics graphics, Bitmap destinationBitmap, int frameIndex, double canvasScale, float offsetX, float offsetY, double opacityMultiplier, bool collectHits)
    {
        if (_animation is null) return;
        AnimationRenderFrame projected = MvAnimationFrameProjector.Project(
            _animation,
            frameIndex,
            PreviewTarget,
            _sheet1?.Width ?? 0,
            _sheet1?.Height ?? 0,
            _sheet2?.Width ?? 0,
            _sheet2?.Height ?? 0);
        foreach (AnimationRenderCell cell in projected.Cells)
        {
            Bitmap? sheet = cell.Source.UsesSecondSheet ? _sheet2 : _sheet1;
            PointF screenOrigin = LogicalToScreen(cell.Origin, canvasScale, offsetX, offsetY);
            if (collectHits && _dragging && _selected.Contains(cell.Lane))
            {
                screenOrigin.X += _dragCurrent.X - _dragOrigin.X;
                screenOrigin.Y += _dragCurrent.Y - _dragOrigin.Y;
            }
            if (sheet is null || !cell.SourceInBounds)
            {
                RectangleF missing = new(screenOrigin.X - 24, screenOrigin.Y - 24, 48, 48);
                using Pen error = new(Color.IndianRed, 2f);
                graphics.DrawRectangle(error, Rectangle.Round(missing));
                graphics.DrawLine(error, missing.Left, missing.Top, missing.Right, missing.Bottom);
                graphics.DrawLine(error, missing.Right, missing.Top, missing.Left, missing.Bottom);
                if (collectHits) _hitBoxes[cell.Lane] = missing;
                continue;
            }

            float finalScale = (float)(cell.Scale * canvasScale);
            float width = MvAnimationConstants.CellSize * Math.Abs(finalScale);
            float height = MvAnimationConstants.CellSize * Math.Abs(finalScale);
            RectangleF bounds = new(screenOrigin.X - width / 2f, screenOrigin.Y - height / 2f, width, height);
            using Bitmap layer = new(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppArgb);
            using (Graphics layerGraphics = Graphics.FromImage(layer))
            {
                layerGraphics.SmoothingMode = SmoothingMode.HighQuality;
                layerGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                layerGraphics.TranslateTransform(screenOrigin.X, screenOrigin.Y);
                layerGraphics.RotateTransform((float)cell.Rotation);
                float xScale = cell.Mirror ? -finalScale : finalScale;
                layerGraphics.ScaleTransform(xScale, finalScale);
                Rectangle destination = new(-MvAnimationConstants.CellSize / 2, -MvAnimationConstants.CellSize / 2, MvAnimationConstants.CellSize, MvAnimationConstants.CellSize);
                Rectangle source = new(cell.Source.SourceX, cell.Source.SourceY, cell.Source.Width, cell.Source.Height);
                using ImageAttributes attributes = new();
                ColorMatrix matrix = new() { Matrix33 = (float)Math.Clamp(cell.Opacity * opacityMultiplier, 0d, 1d) };
                attributes.SetColorMatrix(matrix);
                layerGraphics.DrawImage(sheet, destination, source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            BlendLayer(graphics, destinationBitmap, layer, cell.BlendMode);
            if (collectHits)
            {
                _hitBoxes[cell.Lane] = bounds;
                if (_selected.Contains(cell.Lane))
                {
                    using Pen selection = new(Color.Gold, 2f) { DashStyle = DashStyle.Dash };
                    graphics.DrawRectangle(selection, Rectangle.Round(bounds));
                    TextRenderer.DrawText(graphics, $"L{cell.Lane + 1}", Font, Rectangle.Round(bounds), Color.Gold, TextFormatFlags.Left | TextFormatFlags.Top);
                }
            }
        }
    }

    private static void BlendLayer(Graphics destination, Bitmap destinationBitmap, Bitmap layer, int blendMode)
    {
        if (blendMode == (int)MvAnimationBlendMode.Normal)
        {
            destination.DrawImageUnscaled(layer, 0, 0);
            return;
        }
        destination.Flush(FlushIntention.Sync);
        AnimationSoftwareBlend.Composite(destinationBitmap, layer, blendMode);
    }

    private IReadOnlyList<MvAnimationTimingSnapshot> CurrentTimings() =>
        _animation?.Timings.Where(timing => timing.Frame == FrameIndex).ToArray() ?? [];

    private void DrawTimingOverlays(Graphics graphics, double scale, float offsetX, float offsetY)
    {
        foreach (MvAnimationTimingSnapshot timing in CurrentTimings())
        {
            if (timing.FlashScope is not ((int)MvAnimationFlashScope.Target) and not ((int)MvAnimationFlashScope.Screen)) continue;
            int red = ClampColor(timing.FlashColor.ElementAtOrDefault(0));
            int green = ClampColor(timing.FlashColor.ElementAtOrDefault(1));
            int blue = ClampColor(timing.FlashColor.ElementAtOrDefault(2));
            int alpha = ClampColor(timing.FlashColor.ElementAtOrDefault(3));
            using SolidBrush flash = new(Color.FromArgb(alpha, red, green, blue));
            if (timing.FlashScope == (int)MvAnimationFlashScope.Screen)
            {
                RectangleF screen = LogicalToScreen(new RectangleF(0, 0, PreviewTarget.LogicalWidth, PreviewTarget.LogicalHeight), scale, offsetX, offsetY);
                graphics.FillRectangle(flash, screen);
            }
            else
            {
                RectangleF target = LogicalToScreen(new RectangleF((float)PreviewTarget.X, (float)PreviewTarget.Y, (float)PreviewTarget.Width, (float)PreviewTarget.Height), scale, offsetX, offsetY);
                graphics.FillEllipse(flash, target);
            }
        }
    }

    private static int ClampColor(double value) => Math.Clamp((int)Math.Round(value), 0, 255);

    private void DrawBaseAnchor(Graphics graphics, double scale, float offsetX, float offsetY)
    {
        if (_animation is null) return;
        AnimationPoint anchor = MvAnimationPositionCalculator.Calculate(_animation.Position, PreviewTarget);
        PointF screen = LogicalToScreen(anchor, scale, offsetX, offsetY);
        using Pen pen = new(Color.Cyan, 1.5f);
        graphics.DrawLine(pen, screen.X - 8, screen.Y, screen.X + 8, screen.Y);
        graphics.DrawLine(pen, screen.X, screen.Y - 8, screen.X, screen.Y + 8);
    }

    private int HitTest(Point point) => _hitBoxes.Where(pair => pair.Value.Contains(point)).OrderByDescending(pair => pair.Key).Select(pair => pair.Key).FirstOrDefault(-1);

    private static RectangleF LogicalToScreen(RectangleF value, double scale, float offsetX, float offsetY) =>
        new(offsetX + (float)(value.X * scale), offsetY + (float)(value.Y * scale), (float)(value.Width * scale), (float)(value.Height * scale));

    private static PointF LogicalToScreen(AnimationPoint value, double scale, float offsetX, float offsetY) =>
        new(offsetX + (float)(value.X * scale), offsetY + (float)(value.Y * scale));
}

public static class AnimationSoftwareBlend
{
    public static void Composite(Bitmap destination, Bitmap source, int mode)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        if (destination.Width != source.Width || destination.Height != source.Height)
        {
            throw new ArgumentException("Blend surfaces must have the same dimensions.", nameof(source));
        }
        Rectangle rectangle = new(0, 0, destination.Width, destination.Height);
        BitmapData destinationData = destination.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        BitmapData sourceData = source.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int destinationLength = Math.Abs(destinationData.Stride) * destination.Height;
            int sourceLength = Math.Abs(sourceData.Stride) * source.Height;
            byte[] destinationBytes = new byte[destinationLength];
            byte[] sourceBytes = new byte[sourceLength];
            Marshal.Copy(destinationData.Scan0, destinationBytes, 0, destinationBytes.Length);
            Marshal.Copy(sourceData.Scan0, sourceBytes, 0, sourceBytes.Length);
            int rowBytes = destination.Width * 4;
            for (int y = 0; y < destination.Height; y++)
            {
                int destinationRow = destinationData.Stride >= 0 ? y * destinationData.Stride : (destination.Height - 1 - y) * -destinationData.Stride;
                int sourceRow = sourceData.Stride >= 0 ? y * sourceData.Stride : (source.Height - 1 - y) * -sourceData.Stride;
                for (int x = 0; x < rowBytes; x += 4)
                {
                    int di = destinationRow + x;
                    int si = sourceRow + x;
                    byte sourceAlpha = sourceBytes[si + 3];
                    if (sourceAlpha == 0) continue;
                    Color destinationColor = Color.FromArgb(destinationBytes[di + 3], destinationBytes[di + 2], destinationBytes[di + 1], destinationBytes[di]);
                    Color sourceColor = Color.FromArgb(sourceAlpha, sourceBytes[si + 2], sourceBytes[si + 1], sourceBytes[si]);
                    Color result = Blend(destinationColor, sourceColor, mode);
                    destinationBytes[di] = result.B;
                    destinationBytes[di + 1] = result.G;
                    destinationBytes[di + 2] = result.R;
                    destinationBytes[di + 3] = result.A;
                }
            }
            Marshal.Copy(destinationBytes, 0, destinationData.Scan0, destinationBytes.Length);
        }
        finally
        {
            source.UnlockBits(sourceData);
            destination.UnlockBits(destinationData);
        }
    }

    public static Color Blend(Color destination, Color source, int mode)
    {
        double a = source.A / 255d;
        int BlendChannel(int d, int s) => mode switch
        {
            (int)MvAnimationBlendMode.Additive => Math.Min(255, d + s),
            (int)MvAnimationBlendMode.Multiply => d * s / 255,
            (int)MvAnimationBlendMode.Screen => 255 - (255 - d) * (255 - s) / 255,
            _ => s
        };
        int r = (int)Math.Round(destination.R * (1 - a) + BlendChannel(destination.R, source.R) * a);
        int g = (int)Math.Round(destination.G * (1 - a) + BlendChannel(destination.G, source.G) * a);
        int b = (int)Math.Round(destination.B * (1 - a) + BlendChannel(destination.B, source.B) * a);
        int alpha = (int)Math.Round(source.A + destination.A * (1d - a));
        return Color.FromArgb(Math.Clamp(alpha, 0, 255), Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }
}
