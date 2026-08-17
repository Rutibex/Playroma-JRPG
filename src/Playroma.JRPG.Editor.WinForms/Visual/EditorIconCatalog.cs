namespace Playroma.JRPG.Editor.WinForms.Visual;

public enum EditorIconKind
{
    New,
    Open,
    Save,
    Cut,
    Copy,
    Paste,
    Delete,
    Undo,
    Redo,
    Map,
    Events,
    Pencil,
    Rectangle,
    Ellipse,
    Line,
    Fill,
    Eraser,
    Selection,
    MagicWand,
    Eyedropper,
    Pattern,
    Random,
    Region,
    Shadow,
    ZoomIn,
    ZoomOut,
    ActualSize,
    Fit,
    Database,
    Resources,
    Music,
    Sound,
    Tile,
    Sprite,
    TextStudio,
    Plugins,
    Properties,
    Validation,
    Playtest,
    Back,
    Forward,
    Search,
    Add,
    Duplicate,
    MoveUp,
    MoveDown,
    Warning,
    Error,
    Information,
    Success,
    Modified,
    Folder,
    Refresh,
    Settings
}

/// <summary>
/// Clean-room procedural Playroma toolbar glyphs. The icons are drawn from primitive geometry at the
/// requested size and contain no RPG Maker artwork or copied bitmap data.
/// </summary>
public sealed class EditorIconCatalog : IDisposable
{
    private readonly Dictionary<(EditorIconKind Kind, int Size, int ForeArgb, int AccentArgb), Bitmap> _cache = [];

    public Image Get(EditorIconKind kind, int size, Color foreground, Color accent)
    {
        size = Math.Clamp(size, 12, 64);
        var key = (kind, size, foreground.ToArgb(), accent.ToArgb());
        if (_cache.TryGetValue(key, out Bitmap? cached)) return cached;
        Bitmap bitmap = Draw(kind, size, foreground, accent);
        _cache.Add(key, bitmap);
        return bitmap;
    }

    public void Dispose()
    {
        foreach (Bitmap bitmap in _cache.Values) bitmap.Dispose();
        _cache.Clear();
    }

    private static Bitmap Draw(EditorIconKind kind, int size, Color foreground, Color accent)
    {
        Bitmap bitmap = new(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        float s = size / 24f;
        using Pen pen = new(foreground, Math.Max(1.4f, 1.8f * s))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        using Pen accentPen = new(accent, Math.Max(1.4f, 1.8f * s))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        using Brush brush = new SolidBrush(foreground);
        using Brush accentBrush = new SolidBrush(accent);
        RectangleF R(float x, float y, float w, float h) => new(x * s, y * s, w * s, h * s);
        PointF P(float x, float y) => new(x * s, y * s);
        void Line(Pen p, float x1, float y1, float x2, float y2) => graphics.DrawLine(p, P(x1, y1), P(x2, y2));
        void Box(Pen p, float x, float y, float w, float h) => graphics.DrawRectangle(p, x * s, y * s, w * s, h * s);
        void Plus(Pen p, float x, float y, float radius)
        {
            Line(p, x - radius, y, x + radius, y);
            Line(p, x, y - radius, x, y + radius);
        }

        switch (kind)
        {
            case EditorIconKind.New:
                Box(pen, 5, 3, 12, 17); Line(pen, 13, 3, 17, 7); Plus(accentPen, 17, 17, 4); break;
            case EditorIconKind.Open:
            case EditorIconKind.Folder:
                graphics.FillPolygon(accentBrush, [P(3, 8), P(9, 8), P(11, 5), P(21, 5), P(21, 19), P(3, 19)]);
                graphics.DrawPolygon(pen, [P(3, 8), P(9, 8), P(11, 5), P(21, 5), P(21, 19), P(3, 19)]); break;
            case EditorIconKind.Save:
                Box(pen, 4, 3, 16, 18); Box(accentPen, 7, 4, 9, 6); Box(pen, 7, 13, 10, 7); break;
            case EditorIconKind.Cut:
                graphics.DrawEllipse(pen, R(3, 3, 6, 6)); graphics.DrawEllipse(pen, R(3, 15, 6, 6)); Line(pen, 8, 8, 20, 18); Line(pen, 8, 16, 20, 6); break;
            case EditorIconKind.Copy:
                Box(accentPen, 8, 4, 12, 14); Box(pen, 4, 8, 12, 13); break;
            case EditorIconKind.Paste:
                Box(pen, 5, 5, 14, 16); Box(accentPen, 8, 3, 8, 5); break;
            case EditorIconKind.Delete:
                Line(pen, 5, 7, 19, 7); Line(pen, 9, 4, 15, 4); Box(pen, 7, 8, 10, 12); Line(accentPen, 10, 11, 10, 17); Line(accentPen, 14, 11, 14, 17); break;
            case EditorIconKind.Undo:
            case EditorIconKind.Back:
                graphics.DrawArc(pen, R(5, 5, 15, 14), 210, 250); graphics.DrawLines(accentPen, [P(8, 4), P(4, 9), P(10, 10)]); break;
            case EditorIconKind.Redo:
            case EditorIconKind.Forward:
                graphics.DrawArc(pen, R(4, 5, 15, 14), 80, 250); graphics.DrawLines(accentPen, [P(16, 4), P(20, 9), P(14, 10)]); break;
            case EditorIconKind.Map:
                graphics.DrawPolygon(pen, [P(3, 5), P(9, 3), P(15, 5), P(21, 3), P(21, 19), P(15, 21), P(9, 19), P(3, 21)]); Line(accentPen, 9, 3, 9, 19); Line(accentPen, 15, 5, 15, 21); break;
            case EditorIconKind.Events:
                graphics.FillEllipse(accentBrush, R(8, 3, 8, 8)); graphics.DrawEllipse(pen, R(8, 3, 8, 8)); graphics.DrawArc(pen, R(5, 10, 14, 11), 190, 160); break;
            case EditorIconKind.Pencil:
                Line(accentPen, 5, 19, 18, 6); graphics.DrawLines(pen, [P(4, 20), P(7, 14), P(17, 4), P(20, 7), P(10, 17), P(4, 20)]); break;
            case EditorIconKind.Rectangle:
                Box(accentPen, 4, 5, 16, 14); break;
            case EditorIconKind.Ellipse:
                graphics.DrawEllipse(accentPen, R(4, 5, 16, 14)); break;
            case EditorIconKind.Line:
                Line(accentPen, 4, 19, 20, 5); break;
            case EditorIconKind.Fill:
                graphics.DrawPolygon(pen, [P(5, 6), P(13, 4), P(19, 10), P(11, 18), P(4, 11)]); graphics.FillEllipse(accentBrush, R(16, 16, 5, 5)); break;
            case EditorIconKind.Eraser:
                graphics.DrawPolygon(pen, [P(5, 15), P(13, 5), P(20, 12), P(12, 20)]); Line(accentPen, 9, 16, 13, 20); break;
            case EditorIconKind.Selection:
                for (int i = 0; i < 4; i++) { Line(pen, 4 + i * 5, 4, 6 + i * 5, 4); Line(pen, 4 + i * 5, 20, 6 + i * 5, 20); Line(pen, 4, 4 + i * 5, 4, 6 + i * 5); Line(pen, 20, 4 + i * 5, 20, 6 + i * 5); } break;
            case EditorIconKind.MagicWand:
                Line(accentPen, 6, 19, 16, 9); for (int i = 0; i < 4; i++) { double a = i * Math.PI / 2; float x = 17 + (float)Math.Cos(a) * 4; float y = 7 + (float)Math.Sin(a) * 4; Plus(pen, x, y, 1.4f); } break;
            case EditorIconKind.Eyedropper:
                Line(pen, 7, 18, 17, 8); graphics.DrawEllipse(accentPen, R(14, 4, 6, 6)); Line(pen, 5, 20, 9, 16); break;
            case EditorIconKind.Pattern:
                for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) graphics.FillRectangle(((x + y) & 1) == 0 ? accentBrush : brush, R(4 + x * 6, 4 + y * 6, 4, 4)); break;
            case EditorIconKind.Random:
                Line(pen, 4, 7, 8, 7); Line(pen, 8, 7, 16, 17); Line(pen, 16, 17, 20, 17); graphics.DrawLines(accentPen, [P(17, 14), P(20, 17), P(17, 20)]); Line(pen, 4, 17, 8, 17); Line(pen, 8, 17, 16, 7); Line(pen, 16, 7, 20, 7); break;
            case EditorIconKind.Region:
                graphics.FillPolygon(accentBrush, [P(4, 17), P(7, 6), P(15, 4), P(20, 10), P(17, 20), P(9, 19)]); graphics.DrawPolygon(pen, [P(4, 17), P(7, 6), P(15, 4), P(20, 10), P(17, 20), P(9, 19)]); break;
            case EditorIconKind.Shadow:
                graphics.FillEllipse(accentBrush, R(5, 4, 14, 16)); graphics.FillEllipse(Brushes.Transparent, R(10, 2, 13, 15)); graphics.DrawArc(pen, R(5, 4, 14, 16), 75, 210); break;
            case EditorIconKind.ZoomIn:
            case EditorIconKind.ZoomOut:
                graphics.DrawEllipse(pen, R(3, 3, 13, 13)); Line(pen, 14, 14, 21, 21); Line(accentPen, 6, 9.5f, 13, 9.5f); if (kind == EditorIconKind.ZoomIn) Line(accentPen, 9.5f, 6, 9.5f, 13); break;
            case EditorIconKind.ActualSize:
                Box(pen, 4, 4, 16, 16); Line(accentPen, 8, 8, 16, 8); Line(accentPen, 8, 12, 13, 12); break;
            case EditorIconKind.Fit:
                graphics.DrawLines(pen, [P(9, 4), P(4, 4), P(4, 9)]); graphics.DrawLines(pen, [P(15, 4), P(20, 4), P(20, 9)]); graphics.DrawLines(pen, [P(4, 15), P(4, 20), P(9, 20)]); graphics.DrawLines(pen, [P(20, 15), P(20, 20), P(15, 20)]); break;
            case EditorIconKind.Database:
                graphics.DrawEllipse(accentPen, R(4, 3, 16, 6)); graphics.DrawArc(pen, R(4, 8, 16, 6), 0, 180); graphics.DrawArc(pen, R(4, 13, 16, 6), 0, 180); Line(pen, 4, 6, 4, 17); Line(pen, 20, 6, 20, 17); break;
            case EditorIconKind.Resources:
                graphics.FillPolygon(accentBrush, [P(3, 8), P(9, 8), P(11, 5), P(21, 5), P(21, 19), P(3, 19)]); graphics.DrawPolygon(pen, [P(3, 8), P(9, 8), P(11, 5), P(21, 5), P(21, 19), P(3, 19)]); graphics.DrawEllipse(pen, R(12, 9, 5, 5)); break;
            case EditorIconKind.Music:
                Line(accentPen, 9, 5, 9, 17); Line(accentPen, 9, 5, 18, 3); Line(accentPen, 18, 3, 18, 15); graphics.FillEllipse(brush, R(4, 15, 6, 5)); graphics.FillEllipse(brush, R(13, 13, 6, 5)); break;
            case EditorIconKind.Sound:
                graphics.FillPolygon(accentBrush, [P(4, 10), P(8, 10), P(13, 6), P(13, 18), P(8, 14), P(4, 14)]);
                graphics.DrawPolygon(pen, [P(4, 10), P(8, 10), P(13, 6), P(13, 18), P(8, 14), P(4, 14)]);
                graphics.DrawArc(pen, R(13, 8, 6, 8), -55, 110);
                graphics.DrawArc(pen, R(13, 5, 9, 14), -55, 110);
                break;
            case EditorIconKind.Tile:
                Box(pen, 3, 3, 18, 18);
                Line(accentPen, 9, 3, 9, 21); Line(accentPen, 15, 3, 15, 21);
                Line(accentPen, 3, 9, 21, 9); Line(accentPen, 3, 15, 21, 15);
                graphics.FillRectangle(accentBrush, R(10, 10, 4, 4));
                break;
            case EditorIconKind.Sprite:
                Box(pen, 4, 4, 16, 16);
                graphics.FillRectangle(accentBrush, R(7, 7, 4, 4));
                graphics.FillRectangle(accentBrush, R(13, 7, 4, 4));
                graphics.FillRectangle(accentBrush, R(10, 12, 4, 5));
                Line(pen, 8, 20, 8, 22); Line(pen, 16, 20, 16, 22);
                break;
            case EditorIconKind.TextStudio:
                Box(pen, 4, 3, 16, 18);
                Line(accentPen, 7, 8, 17, 8); Line(accentPen, 7, 12, 15, 12); Line(accentPen, 7, 16, 13, 16);
                Plus(pen, 18, 5, 2);
                break;
            case EditorIconKind.Plugins:
                graphics.FillRectangle(accentBrush, R(8, 8, 8, 8)); graphics.DrawRectangle(pen, 8 * s, 8 * s, 8 * s, 8 * s); Plus(pen, 12, 5, 2); Plus(pen, 19, 12, 2); Plus(pen, 12, 19, 2); Plus(pen, 5, 12, 2); break;
            case EditorIconKind.Properties:
            case EditorIconKind.Settings:
                graphics.DrawEllipse(pen, R(7, 7, 10, 10)); graphics.FillEllipse(accentBrush, R(10, 10, 4, 4)); for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; Line(pen, 12 + (float)Math.Cos(a) * 6, 12 + (float)Math.Sin(a) * 6, 12 + (float)Math.Cos(a) * 9, 12 + (float)Math.Sin(a) * 9); } break;
            case EditorIconKind.Validation:
            case EditorIconKind.Success:
                graphics.DrawEllipse(pen, R(3, 3, 18, 18)); graphics.DrawLines(accentPen, [P(7, 12), P(11, 16), P(18, 8)]); break;
            case EditorIconKind.Playtest:
                graphics.FillPolygon(accentBrush, [P(7, 4), P(20, 12), P(7, 20)]); graphics.DrawPolygon(pen, [P(7, 4), P(20, 12), P(7, 20)]); break;
            case EditorIconKind.Search:
                graphics.DrawEllipse(pen, R(3, 3, 13, 13)); Line(accentPen, 14, 14, 21, 21); break;
            case EditorIconKind.Add:
                graphics.DrawEllipse(pen, R(3, 3, 18, 18)); Plus(accentPen, 12, 12, 5); break;
            case EditorIconKind.Duplicate:
                Box(pen, 4, 8, 11, 12); Box(accentPen, 9, 4, 11, 12); break;
            case EditorIconKind.MoveUp:
                graphics.DrawLines(accentPen, [P(5, 14), P(12, 7), P(19, 14)]); Line(pen, 12, 7, 12, 20); break;
            case EditorIconKind.MoveDown:
                graphics.DrawLines(accentPen, [P(5, 10), P(12, 17), P(19, 10)]); Line(pen, 12, 4, 12, 17); break;
            case EditorIconKind.Warning:
                graphics.FillPolygon(accentBrush, [P(12, 3), P(22, 21), P(2, 21)]); graphics.DrawPolygon(pen, [P(12, 3), P(22, 21), P(2, 21)]); Line(pen, 12, 8, 12, 14); graphics.FillEllipse(brush, R(11, 17, 2, 2)); break;
            case EditorIconKind.Error:
                graphics.DrawEllipse(accentPen, R(3, 3, 18, 18)); Line(pen, 8, 8, 16, 16); Line(pen, 16, 8, 8, 16); break;
            case EditorIconKind.Information:
                graphics.DrawEllipse(accentPen, R(3, 3, 18, 18)); graphics.FillEllipse(brush, R(11, 6, 2, 2)); Line(pen, 12, 11, 12, 18); break;
            case EditorIconKind.Modified:
                graphics.FillEllipse(accentBrush, R(6, 6, 12, 12)); graphics.DrawEllipse(pen, R(6, 6, 12, 12)); break;
            case EditorIconKind.Refresh:
                graphics.DrawArc(pen, R(4, 4, 16, 16), 25, 280); graphics.DrawLines(accentPen, [P(17, 4), P(20, 8), P(15, 9)]); break;
            default:
                graphics.FillEllipse(accentBrush, R(6, 6, 12, 12)); break;
        }
        return bitmap;
    }
}
