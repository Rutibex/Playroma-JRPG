using System.Drawing.Drawing2D;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Rendering;
using Playroma.Tile.Autotiles.Services;

namespace Playroma.Tile.Editor.WinForms.Controls;

public sealed class AutotileTestMapCanvas : Control
{
    public const int Columns = 20;
    public const int Rows = 15;
    private readonly bool[,] _cells = new bool[Columns, Rows];
    private readonly int[,] _fixedShapes = new int[Columns, Rows];
    private readonly MvAutotileRenderer _renderer = new();
    private readonly AutotileShapeResolver _resolver = new();
    private Bitmap? _source;
    private AutotileKind _kind;
    private int _frame;
    private Point _hover = new(-1, -1);

    public AutotileTestMapCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(51, 51, 54);
        Size = new Size(Columns * 48 + 1, Rows * 48 + 1);
        ResetFixedShapes();
    }

    public event EventHandler<AutotileDiagnosticEventArgs>? DiagnosticChanged;
    public bool FixedPatternMode { get; set; }

    public void SetSource(Bitmap? source, AutotileKind kind, int frame)
    {
        _source = source;
        _kind = kind;
        _frame = frame;
        Invalidate();
    }

    public void ClearMap()
    {
        Array.Clear(_cells, 0, _cells.Length);
        ResetFixedShapes();
        Invalidate();
    }

    public void LoadTorturePattern()
    {
        var pattern = AutotileTortureTestRenderer.CreatePattern(_kind);
        Array.Clear(_cells, 0, _cells.Length);
        for (var x = 0; x < Math.Min(Columns, pattern.GetLength(0)); x++)
        for (var y = 0; y < Math.Min(Rows, pattern.GetLength(1)); y++)
            _cells[x, y] = pattern[x, y];
        ResetFixedShapes();
        Invalidate();
    }

    public void Randomize(int seed)
    {
        var random = new Random(seed);
        for (var y = 0; y < Rows; y++)
        for (var x = 0; x < Columns; x++)
            _cells[x, y] = random.NextDouble() < 0.55;
        ResetFixedShapes();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (_source is not null)
        {
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            for (var y = 0; y < Rows; y++)
            for (var x = 0; x < Columns; x++)
            {
                if (!_cells[x, y]) continue;
                var shape = _fixedShapes[x, y] >= 0
                    ? _fixedShapes[x, y]
                    : _resolver.Resolve(AutotileKindInfo.GetFamily(_kind), MvAutotileRenderer.GetNeighborMask(_cells, x, y));
                using var tile = _renderer.RenderShape(_source, _kind, shape, _frame);
                e.Graphics.DrawImageUnscaled(tile, x * 48, y * 48);
            }
        }
        using var gridPen = new Pen(Color.FromArgb(55, Color.White));
        for (var x = 0; x <= Columns; x++) e.Graphics.DrawLine(gridPen, x * 48, 0, x * 48, Rows * 48);
        for (var y = 0; y <= Rows; y++) e.Graphics.DrawLine(gridPen, 0, y * 48, Columns * 48, y * 48);
        if (_hover.X >= 0)
        {
            using var pen = new Pen(Color.DeepSkyBlue, 2f);
            e.Graphics.DrawRectangle(pen, _hover.X * 48 + 1, _hover.Y * 48 + 1, 46, 46);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        PaintAt(e.Location, e.Button == MouseButtons.Left);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var cell = ToCell(e.Location);
        if (cell != _hover)
        {
            _hover = cell;
            UpdateDiagnostic();
            Invalidate();
        }
        if (e.Button == MouseButtons.Left) PaintAt(e.Location, true);
        else if (e.Button == MouseButtons.Right) PaintAt(e.Location, false);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = new(-1, -1);
        DiagnosticChanged?.Invoke(this, new AutotileDiagnosticEventArgs(string.Empty));
        Invalidate();
    }

    private void PaintAt(Point location, bool value)
    {
        var cell = ToCell(location);
        if (cell.X < 0) return;
        _cells[cell.X, cell.Y] = value;
        _fixedShapes[cell.X, cell.Y] = value && FixedPatternMode ? GetFixedShape() : -1;
        Invalidate();
        UpdateDiagnostic();
    }

    private int GetFixedShape() => AutotileKindInfo.GetFamily(_kind) switch
    {
        AutotileFamily.Floor => 47,
        _ => 0
    };

    private void UpdateDiagnostic()
    {
        if (_hover.X < 0 || !_cells[_hover.X, _hover.Y])
        {
            DiagnosticChanged?.Invoke(this, new AutotileDiagnosticEventArgs(_hover.X < 0 ? string.Empty : $"Cell {_hover.X}, {_hover.Y}: empty"));
            return;
        }
        var mask = MvAutotileRenderer.GetNeighborMask(_cells, _hover.X, _hover.Y);
        var shape = _fixedShapes[_hover.X, _hover.Y] >= 0
            ? _fixedShapes[_hover.X, _hover.Y]
            : _resolver.Resolve(AutotileKindInfo.GetFamily(_kind), mask);
        DiagnosticChanged?.Invoke(this, new AutotileDiagnosticEventArgs($"Cell {_hover.X}, {_hover.Y} — neighbours: {mask}; MV shape: {shape}"));
    }

    private Point ToCell(Point location)
    {
        var x = location.X / 48;
        var y = location.Y / 48;
        return x >= 0 && y >= 0 && x < Columns && y < Rows ? new(x, y) : new(-1, -1);
    }

    private void ResetFixedShapes()
    {
        for (var y = 0; y < Rows; y++)
        for (var x = 0; x < Columns; x++)
            _fixedShapes[x, y] = -1;
    }
}

public sealed class AutotileDiagnosticEventArgs(string text) : EventArgs
{
    public string Text { get; } = text;
}
