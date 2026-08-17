using Playroma.JRPG.Assets;

namespace Playroma.JRPG.Editor.WinForms.Assets;

/// <summary>
/// Pixel-art image canvas with logical-cell hit testing. A normal click selects one cell;
/// dragging selects a rectangular group of cells for multi-tile map brushes.
/// </summary>
public sealed class AssetImageCanvas : ScrollableControl
{
    private Image? _image;
    private IReadOnlyList<SheetCell> _cells = [];
    private readonly HashSet<int> _selectedCells = [];
    private int _selectedCell = -1;
    private int _selectionAnchor = -1;
    private bool _dragSelecting;
    private float _zoom = 1f;
    private bool _showGrid = true;

    public AssetImageCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(38, 38, 42);
        AutoScroll = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public Image? Image => _image;
    public IReadOnlyList<SheetCell> Cells => _cells;
    public IReadOnlyList<int> SelectedCells => _selectedCells.OrderBy(index => index).ToArray();

    public float Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 0.1f, 16f);
            UpdateScrollExtent();
            Invalidate();
        }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            _showGrid = value;
            Invalidate();
        }
    }

    public int SelectedCell
    {
        get => _selectedCell;
        set => SelectSingleCell(value);
    }

    public event EventHandler? SelectedCellChanged;
    public event EventHandler? SelectedCellsChanged;

    public void SetImage(Image? image, IReadOnlyList<SheetCell>? cells = null)
    {
        Image? old = _image;
        _image = image;
        _cells = cells ?? [];
        _selectedCells.Clear();
        _selectedCell = _cells.Count > 0 ? 0 : -1;
        _selectionAnchor = _selectedCell;
        if (_selectedCell >= 0) _selectedCells.Add(_selectedCell);
        old?.Dispose();
        UpdateScrollExtent();
        Invalidate();
        RaiseSelectionChanged();
    }

    public void SelectCells(IEnumerable<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        HashSet<int> valid = indices
            .Where(index => index >= 0 && index < _cells.Count)
            .ToHashSet();
        int primary = valid.Count == 0 ? -1 : valid.Min();
        if (_selectedCell == primary && _selectedCells.SetEquals(valid)) return;
        _selectedCells.Clear();
        _selectedCells.UnionWith(valid);
        _selectedCell = primary;
        _selectionAnchor = primary;
        Invalidate();
        RaiseSelectionChanged();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image is null)
        {
            TextRenderer.DrawText(
                e.Graphics,
                "No image preview",
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        (int x, int y, int drawWidth, int drawHeight) = GetDrawBounds();
        DrawCheckerboard(e.Graphics, new Rectangle(x, y, drawWidth, drawHeight));
        e.Graphics.InterpolationMode = _zoom >= 1f
            ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor
            : System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        e.Graphics.DrawImage(_image, new Rectangle(x, y, drawWidth, drawHeight));
        if (!_showGrid || _cells.Count == 0) return;

        using Pen gridPen = new(Color.FromArgb(170, Color.DeepSkyBlue), Math.Max(1f, _zoom < 1f ? 1f : 1.5f));
        using Pen selectedPen = new(Color.Gold, Math.Max(2f, _zoom < 1f ? 2f : 3f));
        using Pen primaryPen = new(Color.OrangeRed, Math.Max(2.5f, _zoom < 1f ? 2.5f : 4f));
        foreach (SheetCell cell in _cells)
        {
            RectangleF rectangle = ToClientRectangle(cell, x, y);
            Pen pen = cell.Index == _selectedCell
                ? primaryPen
                : _selectedCells.Contains(cell.Index) ? selectedPen : gridPen;
            e.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        SheetCell? cell = HitTest(e.Location);
        if (cell is null) return;

        Focus();
        _dragSelecting = true;
        Capture = true;
        if ((ModifierKeys & Keys.Control) != 0)
        {
            if (!_selectedCells.Add(cell.Index)) _selectedCells.Remove(cell.Index);
            _selectedCell = _selectedCells.Contains(cell.Index)
                ? cell.Index
                : (_selectedCells.Count == 0 ? -1 : _selectedCells.Min());
            _selectionAnchor = _selectedCell;
            Invalidate();
            RaiseSelectionChanged();
            return;
        }

        _selectionAnchor = cell.Index;
        SelectRectangularRange(cell.Index);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragSelecting || e.Button != MouseButtons.Left || _selectionAnchor < 0) return;
        SheetCell? cell = HitTest(e.Location);
        if (cell is not null) SelectRectangularRange(cell.Index);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragSelecting = false;
        Capture = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
            _image = null;
        }
        base.Dispose(disposing);
    }

    private void SelectSingleCell(int value)
    {
        int normalized = value >= 0 && value < _cells.Count ? value : -1;
        if (_selectedCell == normalized
            && _selectedCells.Count == (normalized >= 0 ? 1 : 0)
            && (normalized < 0 || _selectedCells.Contains(normalized)))
        {
            return;
        }

        _selectedCells.Clear();
        _selectedCell = normalized;
        _selectionAnchor = normalized;
        if (normalized >= 0) _selectedCells.Add(normalized);
        Invalidate();
        RaiseSelectionChanged();
    }

    private void SelectRectangularRange(int endIndex)
    {
        SheetCell? start = _cells.FirstOrDefault(cell => cell.Index == _selectionAnchor);
        SheetCell? end = _cells.FirstOrDefault(cell => cell.Index == endIndex);
        if (start is null || end is null) return;

        Rectangle bounds = Rectangle.FromLTRB(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Max(start.X + start.Width, end.X + end.Width),
            Math.Max(start.Y + start.Height, end.Y + end.Height));
        HashSet<int> selected = _cells
            .Where(cell => bounds.IntersectsWith(new Rectangle(cell.X, cell.Y, cell.Width, cell.Height)))
            .Select(cell => cell.Index)
            .ToHashSet();
        if (_selectedCell == endIndex && _selectedCells.SetEquals(selected)) return;

        _selectedCells.Clear();
        _selectedCells.UnionWith(selected);
        _selectedCell = endIndex;
        Invalidate();
        RaiseSelectionChanged();
    }

    private SheetCell? HitTest(Point point)
    {
        if (_image is null || _cells.Count == 0) return null;
        (int x, int y, _, _) = GetDrawBounds();
        float imageX = (point.X - x) / _zoom;
        float imageY = (point.Y - y) / _zoom;
        return _cells.FirstOrDefault(candidate =>
            imageX >= candidate.X
            && imageX < candidate.X + candidate.Width
            && imageY >= candidate.Y
            && imageY < candidate.Y + candidate.Height);
    }

    private (int X, int Y, int Width, int Height) GetDrawBounds()
    {
        if (_image is null) return (8, 8, 1, 1);
        int drawWidth = Math.Max(1, checked((int)Math.Round(_image.Width * _zoom)));
        int drawHeight = Math.Max(1, checked((int)Math.Round(_image.Height * _zoom)));
        int x = AutoScrollPosition.X + Math.Max(8, (ClientSize.Width - drawWidth) / 2);
        int y = AutoScrollPosition.Y + Math.Max(8, (ClientSize.Height - drawHeight) / 2);
        return (x, y, drawWidth, drawHeight);
    }

    private void UpdateScrollExtent()
    {
        if (_image is null)
        {
            AutoScrollMinSize = Size.Empty;
            return;
        }
        int width = Math.Max(1, checked((int)Math.Round(_image.Width * _zoom))) + 16;
        int height = Math.Max(1, checked((int)Math.Round(_image.Height * _zoom))) + 16;
        AutoScrollMinSize = new Size(width, height);
    }

    private RectangleF ToClientRectangle(SheetCell cell, int drawX, int drawY) => new(
        drawX + cell.X * _zoom,
        drawY + cell.Y * _zoom,
        cell.Width * _zoom,
        cell.Height * _zoom);

    private void RaiseSelectionChanged()
    {
        SelectedCellChanged?.Invoke(this, EventArgs.Empty);
        SelectedCellsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void DrawCheckerboard(Graphics graphics, Rectangle rectangle)
    {
        const int size = 12;
        using SolidBrush light = new(Color.FromArgb(220, 220, 220));
        using SolidBrush dark = new(Color.FromArgb(175, 175, 175));
        graphics.FillRectangle(light, rectangle);
        for (int y = rectangle.Top; y < rectangle.Bottom; y += size)
        {
            for (int x = rectangle.Left; x < rectangle.Right; x += size)
            {
                if ((((x - rectangle.Left) / size) + ((y - rectangle.Top) / size)) % 2 == 0) continue;
                graphics.FillRectangle(
                    dark,
                    x,
                    y,
                    Math.Min(size, rectangle.Right - x),
                    Math.Min(size, rectangle.Bottom - y));
            }
        }
    }
}
