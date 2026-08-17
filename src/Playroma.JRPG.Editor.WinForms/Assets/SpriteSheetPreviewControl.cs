using System.Drawing.Drawing2D;
using Playroma.JRPG.Assets.Windows;

namespace Playroma.JRPG.Editor.WinForms.Assets;

public sealed class SpriteSheetPreviewControl : Control
{
    private Bitmap? _bitmap;
    private Rectangle? _sourceRegion;
    private IReadOnlyList<Rectangle> _gridCells = [];
    private bool _showGrid;

    public SpriteSheetPreviewControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(36, 36, 36);
        TabStop = true;
    }

    public Bitmap? Bitmap
    {
        get => _bitmap;
        set
        {
            if (ReferenceEquals(_bitmap, value)) return;
            _bitmap?.Dispose();
            _bitmap = value;
            Invalidate();
        }
    }

    public Rectangle? SourceRegion
    {
        get => _sourceRegion;
        set { _sourceRegion = value; Invalidate(); }
    }

    public IReadOnlyList<Rectangle> GridCells
    {
        get => _gridCells;
        set { _gridCells = value ?? []; Invalidate(); }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set { _showGrid = value; Invalidate(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bitmap?.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DrawCheckerboard(e.Graphics, ClientRectangle);
        if (_bitmap is null) return;

        Rectangle source = _sourceRegion ?? new Rectangle(Point.Empty, _bitmap.Size);
        Rectangle destination = Fit(source.Size, ClientRectangle, 12);
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_bitmap, destination, source, GraphicsUnit.Pixel);

        if (_showGrid && !_sourceRegion.HasValue && _gridCells.Count > 0)
        {
            using Pen pen = new(Color.FromArgb(150, Color.DeepSkyBlue));
            double scaleX = destination.Width / (double)source.Width;
            double scaleY = destination.Height / (double)source.Height;
            foreach (Rectangle cell in _gridCells)
            {
                RectangleF mapped = new(
                    destination.Left + (float)(cell.Left * scaleX),
                    destination.Top + (float)(cell.Top * scaleY),
                    (float)(cell.Width * scaleX),
                    (float)(cell.Height * scaleY));
                e.Graphics.DrawRectangle(pen, mapped.X, mapped.Y, mapped.Width, mapped.Height);
            }
        }
    }

    private static Rectangle Fit(Size source, Rectangle bounds, int margin)
    {
        Rectangle inner = Rectangle.Inflate(bounds, -margin, -margin);
        if (source.Width <= 0 || source.Height <= 0 || inner.Width <= 0 || inner.Height <= 0) return Rectangle.Empty;
        double scale = Math.Min(inner.Width / (double)source.Width, inner.Height / (double)source.Height);
        // Pixel art may be enlarged, but keep integral scaling when practical.
        if (scale >= 1d) scale = Math.Max(1d, Math.Floor(scale));
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return new Rectangle(inner.Left + (inner.Width - width) / 2, inner.Top + (inner.Height - height) / 2, width, height);
    }

    private static void DrawCheckerboard(Graphics graphics, Rectangle bounds)
    {
        const int size = 12;
        using SolidBrush light = new(Color.FromArgb(70, 70, 70));
        using SolidBrush dark = new(Color.FromArgb(52, 52, 52));
        for (int y = bounds.Top; y < bounds.Bottom; y += size)
        {
            for (int x = bounds.Left; x < bounds.Right; x += size)
            {
                graphics.FillRectangle((((x - bounds.Left) / size + (y - bounds.Top) / size) & 1) == 0 ? light : dark, x, y, size, size);
            }
        }
    }
}
