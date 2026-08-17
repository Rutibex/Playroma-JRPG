using System.Drawing.Drawing2D;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Rendering;
using Playroma.Tile.Autotiles.Services;

namespace Playroma.Tile.Editor.WinForms.Controls;

public sealed class AutotileShapeGallery : Control
{
    private readonly MvAutotileRenderer _renderer = new();
    private Bitmap? _source;
    private AutotileKind _kind;
    private int _frame;

    public AutotileShapeGallery()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(37, 37, 38);
        AutoScrollOffset = Point.Empty;
    }

    public void SetSource(Bitmap? source, AutotileKind kind, int frame)
    {
        _source = source;
        _kind = kind;
        _frame = frame;
        var count = GetShapeCount();
        var columns = 8;
        var rows = (int)Math.Ceiling(count / (double)columns);
        Size = new Size(columns * 72 + 16, rows * 78 + 16);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (_source is null) return;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        for (var shape = 0; shape < GetShapeCount(); shape++)
        {
            var x = 8 + shape % 8 * 72;
            var y = 8 + shape / 8 * 78;
            using var tile = _renderer.RenderShape(_source, _kind, shape, _frame);
            e.Graphics.DrawImage(tile, new Rectangle(x, y, 64, 64));
            using var brush = new SolidBrush(Color.Gainsboro);
            e.Graphics.DrawString(shape == 47 ? "47 fixed" : shape.ToString(), Font, brush, x, y + 64);
        }
    }

    private int GetShapeCount() => AutotileKindInfo.GetFamily(_kind) switch
    {
        AutotileFamily.Floor => 48,
        AutotileFamily.Wall => 16,
        _ => 4
    };
}
