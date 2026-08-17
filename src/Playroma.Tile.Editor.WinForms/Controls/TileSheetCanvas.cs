using System.Drawing.Drawing2D;
using Playroma.Tile.RpgMakerMV.Formats;

namespace Playroma.Tile.Editor.WinForms.Controls;

public sealed class TileSheetCanvas : Control
{
    private Bitmap? _image;
    private IReadOnlyList<MvRegionDefinition> _regions = Array.Empty<MvRegionDefinition>();
    private double _zoom = 1.0;
    private int _selectedRegionIndex = -1;
    private int _hoveredRegionIndex = -1;

    public TileSheetCanvas()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.FromArgb(45, 45, 48);
        TabStop = true;
    }

    public event EventHandler? SelectedRegionChanged;
    public event EventHandler<ImageCoordinateEventArgs>? ImageCoordinateChanged;

    public Bitmap? SheetImage
    {
        get => _image;
        set
        {
            _image = value;
            _selectedRegionIndex = -1;
            _hoveredRegionIndex = -1;
            UpdateCanvasSize();
            Invalidate();
        }
    }

    public IReadOnlyList<MvRegionDefinition> Regions
    {
        get => _regions;
        set
        {
            _regions = value ?? Array.Empty<MvRegionDefinition>();
            _selectedRegionIndex = _regions.Count > 0 ? 0 : -1;
            Invalidate();
            SelectedRegionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowGrid { get; set; } = true;
    public bool ShowRegionNumbers { get; set; } = true;

    public double Zoom
    {
        get => _zoom;
        set
        {
            var clamped = Math.Clamp(value, 0.1, 8.0);
            if (Math.Abs(_zoom - clamped) < 0.0001)
            {
                return;
            }

            _zoom = clamped;
            UpdateCanvasSize();
            Invalidate();
        }
    }

    public int SelectedRegionIndex
    {
        get => _selectedRegionIndex;
        set
        {
            var normalized = value >= 0 && value < _regions.Count ? value : -1;
            if (_selectedRegionIndex == normalized)
            {
                return;
            }

            _selectedRegionIndex = normalized;
            Invalidate();
            SelectedRegionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public MvRegionDefinition? SelectedRegion =>
        _selectedRegionIndex >= 0 && _selectedRegionIndex < _regions.Count
            ? _regions[_selectedRegionIndex]
            : null;

    public void SelectRegionByLogicalIndex(int logicalIndex)
    {
        var position = _regions.ToList().FindIndex(region => region.Index == logicalIndex);
        SelectedRegionIndex = position;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (_image is null)
        {
            DrawCenteredMessage(e.Graphics, "Open or import an RPG Maker MV tilesheet.");
            return;
        }

        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.SmoothingMode = SmoothingMode.None;

        var destination = new Rectangle(0, 0, ScaleDimension(_image.Width), ScaleDimension(_image.Height));
        DrawCheckerboard(e.Graphics, destination);
        e.Graphics.DrawImage(_image, destination, new Rectangle(0, 0, _image.Width, _image.Height), GraphicsUnit.Pixel);

        if (ShowGrid)
        {
            DrawRegions(e.Graphics);
        }

        DrawHoverAndSelection(e.Graphics);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var regionPosition = HitTestRegion(e.Location);
        if (regionPosition >= 0)
        {
            SelectedRegionIndex = regionPosition;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var imagePoint = ToImagePoint(e.Location);
        ImageCoordinateChanged?.Invoke(this, new ImageCoordinateEventArgs(imagePoint));
        var hit = HitTestRegion(e.Location);
        if (_hoveredRegionIndex != hit)
        {
            _hoveredRegionIndex = hit;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredRegionIndex = -1;
        Invalidate();
    }

    private void DrawRegions(Graphics graphics)
    {
        using var pen = new Pen(Color.FromArgb(135, 255, 255, 255), 1f);
        using var textBrush = new SolidBrush(Color.White);
        using var textBackground = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        using var font = new Font(Font.FontFamily, Math.Max(7f, Font.Size - 1f), FontStyle.Bold);

        foreach (var region in _regions)
        {
            var rectangle = Scale(region.Bounds);
            graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, Math.Max(0, rectangle.Width - 1), Math.Max(0, rectangle.Height - 1));
            if (ShowRegionNumbers && Zoom >= 0.75 && rectangle.Width >= 28 && rectangle.Height >= 22)
            {
                var label = (region.Index + 1).ToString();
                var size = graphics.MeasureString(label, font);
                var background = new RectangleF(rectangle.X + 2, rectangle.Y + 2, size.Width + 4, size.Height + 1);
                graphics.FillRectangle(textBackground, background);
                graphics.DrawString(label, font, textBrush, rectangle.X + 4, rectangle.Y + 2);
            }
        }
    }

    private void DrawHoverAndSelection(Graphics graphics)
    {
        if (_hoveredRegionIndex >= 0 && _hoveredRegionIndex < _regions.Count &&
            _hoveredRegionIndex != _selectedRegionIndex)
        {
            var rectangle = Scale(_regions[_hoveredRegionIndex].Bounds);
            using var hoverBrush = new SolidBrush(Color.FromArgb(45, Color.White));
            using var hoverPen = new Pen(Color.White, 2f);
            graphics.FillRectangle(hoverBrush, rectangle);
            graphics.DrawRectangle(hoverPen, rectangle.X + 1, rectangle.Y + 1,
                Math.Max(0, rectangle.Width - 3), Math.Max(0, rectangle.Height - 3));
        }

        if (_selectedRegionIndex >= 0 && _selectedRegionIndex < _regions.Count)
        {
            var rectangle = Scale(_regions[_selectedRegionIndex].Bounds);
            using var selectionBrush = new SolidBrush(Color.FromArgb(54, 30, 144, 255));
            using var selectionPen = new Pen(Color.DeepSkyBlue, 3f);
            graphics.FillRectangle(selectionBrush, rectangle);
            graphics.DrawRectangle(selectionPen, rectangle.X + 1, rectangle.Y + 1,
                Math.Max(0, rectangle.Width - 3), Math.Max(0, rectangle.Height - 3));
        }
    }

    private static void DrawCheckerboard(Graphics graphics, Rectangle bounds)
    {
        const int square = 12;
        using var light = new SolidBrush(Color.FromArgb(210, 210, 210));
        using var dark = new SolidBrush(Color.FromArgb(165, 165, 165));
        for (var y = bounds.Top; y < bounds.Bottom; y += square)
        {
            for (var x = bounds.Left; x < bounds.Right; x += square)
            {
                var brush = ((x / square) + (y / square)) % 2 == 0 ? light : dark;
                graphics.FillRectangle(brush, x, y,
                    Math.Min(square, bounds.Right - x),
                    Math.Min(square, bounds.Bottom - y));
            }
        }
    }

    private void DrawCenteredMessage(Graphics graphics, string text)
    {
        using var brush = new SolidBrush(Color.Gainsboro);
        var size = graphics.MeasureString(text, Font);
        graphics.DrawString(text, Font, brush,
            Math.Max(8, (ClientSize.Width - size.Width) / 2),
            Math.Max(8, (ClientSize.Height - size.Height) / 2));
    }

    private int HitTestRegion(Point controlPoint)
    {
        if (_image is null)
        {
            return -1;
        }

        var imagePoint = ToImagePoint(controlPoint);
        for (var index = 0; index < _regions.Count; index++)
        {
            if (_regions[index].Bounds.Contains(imagePoint))
            {
                return index;
            }
        }

        return -1;
    }

    private Point ToImagePoint(Point controlPoint)
    {
        return new Point(
            (int)Math.Floor(controlPoint.X / Zoom),
            (int)Math.Floor(controlPoint.Y / Zoom));
    }

    private Rectangle Scale(Rectangle rectangle)
    {
        return new Rectangle(
            ScaleCoordinate(rectangle.X),
            ScaleCoordinate(rectangle.Y),
            ScaleDimension(rectangle.Width),
            ScaleDimension(rectangle.Height));
    }

    private int ScaleCoordinate(int value) => (int)Math.Round(value * Zoom);

    private int ScaleDimension(int value) => Math.Max(1, (int)Math.Round(value * Zoom));

    private void UpdateCanvasSize()
    {
        if (_image is null)
        {
            Size = new Size(640, 480);
            return;
        }

        Size = new Size(ScaleDimension(_image.Width), ScaleDimension(_image.Height));
    }
}

public sealed class ImageCoordinateEventArgs(Point imagePoint) : EventArgs
{
    public Point ImagePoint { get; } = imagePoint;
}
