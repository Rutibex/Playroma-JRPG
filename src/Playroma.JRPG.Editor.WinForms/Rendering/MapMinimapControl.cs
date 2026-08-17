using System.Drawing.Drawing2D;
using Playroma.JRPG.Rendering;
using Playroma.JRPG.Rendering.Windows;

namespace Playroma.JRPG.Editor.WinForms.Rendering;

public sealed class MapPointRequestedEventArgs : EventArgs
{
    public MapPointRequestedEventArgs(PixelPoint mapPixel) => MapPixel = mapPixel;
    public PixelPoint MapPixel { get; }
}

/// <summary>Bounded read-only minimap with a live viewport indicator and click navigation.</summary>
public sealed class MapMinimapControl : Control
{
    private Bitmap? _bitmap;
    private PixelSize _mapSize;
    private PixelRect _viewport;

    public MapMinimapControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 26, 29);
        ForeColor = Color.White;
        MinimumSize = new Size(120, 120);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    public event EventHandler<MapPointRequestedEventArgs>? MapPointRequested;

    public void SetImage(MapMinimapResult? result)
    {
        Bitmap? replacement = result is null ? null : BitmapPixelConverter.ToBitmap(result.Image);
        Bitmap? previous = _bitmap;
        _bitmap = replacement;
        _mapSize = result?.MapPixelSize ?? default;
        previous?.Dispose();
        Invalidate();
    }

    public void SetViewport(PixelRect viewport)
    {
        _viewport = viewport;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (_bitmap is null || _mapSize.IsEmpty)
        {
            TextRenderer.DrawText(e.Graphics, "Minimap", Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        Rectangle destination = ImageDestination();
        if (destination.IsEmpty) return;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_bitmap, destination, new Rectangle(0, 0, _bitmap.Width, _bitmap.Height), GraphicsUnit.Pixel);
        using Pen border = new(Color.FromArgb(190, 255, 255, 255));
        e.Graphics.DrawRectangle(border, destination);

        if (!_viewport.IsEmpty)
        {
            Rectangle viewport = new(
                destination.Left + Scale(_viewport.X, destination.Width, _mapSize.Width),
                destination.Top + Scale(_viewport.Y, destination.Height, _mapSize.Height),
                Math.Max(2, Scale(_viewport.Width, destination.Width, _mapSize.Width)),
                Math.Max(2, Scale(_viewport.Height, destination.Height, _mapSize.Height)));
            viewport = Rectangle.Intersect(destination, viewport);
            if (viewport.Width > 0 && viewport.Height > 0)
            {
                using SolidBrush fill = new(Color.FromArgb(42, 0, 191, 255));
                using Pen outline = new(Color.DeepSkyBlue, 2f);
                e.Graphics.FillRectangle(fill, viewport);
                e.Graphics.DrawRectangle(outline, viewport);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _bitmap is null || _mapSize.IsEmpty) return;
        Rectangle destination = ImageDestination();
        if (!destination.Contains(e.Location)) return;
        int x = Math.Clamp(checked((int)((long)(e.X - destination.Left) * _mapSize.Width / Math.Max(1, destination.Width))), 0, _mapSize.Width - 1);
        int y = Math.Clamp(checked((int)((long)(e.Y - destination.Top) * _mapSize.Height / Math.Max(1, destination.Height))), 0, _mapSize.Height - 1);
        MapPointRequested?.Invoke(this, new MapPointRequestedEventArgs(new PixelPoint(x, y)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
        base.Dispose(disposing);
    }

    private Rectangle ImageDestination()
    {
        if (_bitmap is null) return Rectangle.Empty;
        Rectangle available = Rectangle.Inflate(ClientRectangle, -6, -6);
        if (available.Width <= 0 || available.Height <= 0) return Rectangle.Empty;
        double scale = Math.Min(available.Width / (double)_bitmap.Width, available.Height / (double)_bitmap.Height);
        int width = Math.Max(1, checked((int)Math.Round(_bitmap.Width * scale)));
        int height = Math.Max(1, checked((int)Math.Round(_bitmap.Height * scale)));
        return new Rectangle(available.Left + (available.Width - width) / 2, available.Top + (available.Height - height) / 2, width, height);
    }

    private static int Scale(int value, int targetLength, int sourceLength) =>
        sourceLength <= 0 ? 0 : checked((int)Math.Round(value * targetLength / (double)sourceLength));
}
