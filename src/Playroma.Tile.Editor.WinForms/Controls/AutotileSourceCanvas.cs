using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Playroma.Tile.Editor.WinForms.Controls;

public enum AutotilePaintTool
{
    Pencil,
    Eraser,
    Fill,
    Eyedropper
}

public sealed class AutotileSourceCanvas : Control
{
    private Bitmap? _bitmap;
    private bool _painting;
    private Point _lastPixel = new(-1, -1);
    private Rectangle? _selectedQuarterBounds;

    public AutotileSourceCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(45, 45, 48);
        Zoom = 4;
        ForeColorValue = Color.White;
        Tool = AutotilePaintTool.Pencil;
        TabStop = true;
    }

    public event EventHandler? EditStarted;
    public event EventHandler? ImageChanged;
    public event EventHandler<ColorPickedEventArgs>? ColorPicked;
    public event EventHandler<SourcePixelSelectedEventArgs>? SourcePixelSelected;

    public Bitmap? Bitmap
    {
        get => _bitmap;
        set
        {
            _bitmap = value;
            UpdateSize();
            Invalidate();
        }
    }

    public int Zoom { get; set; }
    public Color ForeColorValue { get; set; }
    public AutotilePaintTool Tool { get; set; }
    public bool ShowQuarterGrid { get; set; } = true;
    public bool ShowPixelGrid { get; set; }

    public Rectangle? SelectedQuarterBounds
    {
        get => _selectedQuarterBounds;
        set { _selectedQuarterBounds = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (_bitmap is null)
        {
            using var brush = new SolidBrush(Color.Gainsboro);
            e.Graphics.DrawString("Create or select an autotile.", Font, brush, 16, 16);
            return;
        }

        var destination = new Rectangle(0, 0, _bitmap.Width * Zoom, _bitmap.Height * Zoom);
        DrawCheckerboard(e.Graphics, destination);
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_bitmap, destination, new Rectangle(Point.Empty, _bitmap.Size), GraphicsUnit.Pixel);

        if (ShowPixelGrid && Zoom >= 6)
        {
            using var pen = new Pen(Color.FromArgb(45, Color.Black));
            for (var x = 0; x <= _bitmap.Width; x++) e.Graphics.DrawLine(pen, x * Zoom, 0, x * Zoom, destination.Height);
            for (var y = 0; y <= _bitmap.Height; y++) e.Graphics.DrawLine(pen, 0, y * Zoom, destination.Width, y * Zoom);
        }
        if (ShowQuarterGrid)
        {
            using var pen = new Pen(Color.FromArgb(220, 255, 210, 40), 2f);
            for (var x = 0; x <= _bitmap.Width; x += 24) e.Graphics.DrawLine(pen, x * Zoom, 0, x * Zoom, destination.Height);
            for (var y = 0; y <= _bitmap.Height; y += 24) e.Graphics.DrawLine(pen, 0, y * Zoom, destination.Width, y * Zoom);
        }
        if (_selectedQuarterBounds is Rectangle selected)
        {
            using var fill = new SolidBrush(Color.FromArgb(35, Color.DeepSkyBlue));
            using var pen = new Pen(Color.DeepSkyBlue, 2f);
            var scaled = new Rectangle(selected.X * Zoom + 1, selected.Y * Zoom + 1,
                selected.Width * Zoom - 2, selected.Height * Zoom - 2);
            e.Graphics.FillRectangle(fill, scaled);
            e.Graphics.DrawRectangle(pen, scaled);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_bitmap is null) return;
        var point = ToPixel(e.Location);
        if (!Contains(point)) return;
        SourcePixelSelected?.Invoke(this, new SourcePixelSelectedEventArgs(point));
        if (e.Button == MouseButtons.Right || Tool == AutotilePaintTool.Eyedropper)
        {
            Pick(point);
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        EditStarted?.Invoke(this, EventArgs.Empty);
        _painting = true;
        _lastPixel = point;
        Apply(point);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_painting || _bitmap is null || e.Button != MouseButtons.Left) return;
        var point = ToPixel(e.Location);
        if (!Contains(point) || point == _lastPixel) return;
        DrawLine(_lastPixel, point);
        _lastPixel = point;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _painting = false;
        _lastPixel = new(-1, -1);
    }

    private void Apply(Point point)
    {
        if (_bitmap is null) return;
        if (Tool == AutotilePaintTool.Fill)
        {
            FloodFill(point, ForeColorValue);
            _painting = false;
            ImageChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (Tool == AutotilePaintTool.Eyedropper)
        {
            Pick(point);
            return;
        }
        ApplyPixel(point);
        InvalidatePixel(point);
        ImageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyPixel(Point point)
    {
        if (_bitmap is null) return;
        _bitmap.SetPixel(point.X, point.Y,
            Tool == AutotilePaintTool.Eraser ? Color.Transparent : ForeColorValue);
    }

    private void DrawLine(Point from, Point to)
    {
        var dx = Math.Abs(to.X - from.X);
        var sx = from.X < to.X ? 1 : -1;
        var dy = -Math.Abs(to.Y - from.Y);
        var sy = from.Y < to.Y ? 1 : -1;
        var error = dx + dy;
        var current = from;
        var minX = Math.Min(from.X, to.X);
        var minY = Math.Min(from.Y, to.Y);
        var maxX = Math.Max(from.X, to.X);
        var maxY = Math.Max(from.Y, to.Y);
        while (true)
        {
            ApplyPixel(current);
            if (current == to) break;
            var e2 = 2 * error;
            if (e2 >= dy) { error += dy; current.X += sx; }
            if (e2 <= dx) { error += dx; current.Y += sy; }
        }
        Invalidate(new Rectangle(minX * Zoom, minY * Zoom,
            (maxX - minX + 1) * Zoom + 1, (maxY - minY + 1) * Zoom + 1));
        ImageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FloodFill(Point start, Color replacement)
    {
        if (_bitmap is null) return;
        var target = _bitmap.GetPixel(start.X, start.Y);
        if (target.ToArgb() == replacement.ToArgb()) return;
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (!Contains(p) || _bitmap.GetPixel(p.X, p.Y).ToArgb() != target.ToArgb()) continue;
            _bitmap.SetPixel(p.X, p.Y, replacement);
            queue.Enqueue(new(p.X - 1, p.Y)); queue.Enqueue(new(p.X + 1, p.Y));
            queue.Enqueue(new(p.X, p.Y - 1)); queue.Enqueue(new(p.X, p.Y + 1));
        }
        Invalidate();
    }

    private void Pick(Point point)
    {
        if (_bitmap is null) return;
        ForeColorValue = _bitmap.GetPixel(point.X, point.Y);
        ColorPicked?.Invoke(this, new ColorPickedEventArgs(ForeColorValue));
    }

    private Point ToPixel(Point point) => new(point.X / Math.Max(1, Zoom), point.Y / Math.Max(1, Zoom));
    private bool Contains(Point point) => _bitmap is not null && point.X >= 0 && point.Y >= 0 && point.X < _bitmap.Width && point.Y < _bitmap.Height;
    private void InvalidatePixel(Point point) => Invalidate(new Rectangle(point.X * Zoom, point.Y * Zoom, Zoom + 1, Zoom + 1));

    private void UpdateSize()
    {
        Size = _bitmap is null ? new Size(640, 480) : new Size(_bitmap.Width * Zoom + 1, _bitmap.Height * Zoom + 1);
    }

    private static void DrawCheckerboard(Graphics graphics, Rectangle bounds)
    {
        const int square = 12;
        using var light = new SolidBrush(Color.FromArgb(205, 205, 205));
        using var dark = new SolidBrush(Color.FromArgb(155, 155, 155));
        for (var y = 0; y < bounds.Height; y += square)
        for (var x = 0; x < bounds.Width; x += square)
            graphics.FillRectangle(((x / square + y / square) & 1) == 0 ? light : dark,
                x, y, Math.Min(square, bounds.Width - x), Math.Min(square, bounds.Height - y));
    }
}

public sealed class ColorPickedEventArgs(Color color) : EventArgs
{
    public Color Color { get; } = color;
}

public sealed class SourcePixelSelectedEventArgs(Point pixel) : EventArgs
{
    public Point Pixel { get; } = pixel;
}
