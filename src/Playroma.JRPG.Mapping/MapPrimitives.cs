namespace Playroma.JRPG.Mapping;

public readonly record struct MapPoint(int X, int Y)
{
    public static MapPoint operator +(MapPoint left, MapPoint right) => new(left.X + right.X, left.Y + right.Y);
    public static MapPoint operator -(MapPoint left, MapPoint right) => new(left.X - right.X, left.Y - right.Y);
}

public readonly record struct MapSize(int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct MapRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static MapRect FromPoints(MapPoint first, MapPoint second)
    {
        int left = Math.Min(first.X, second.X);
        int top = Math.Min(first.Y, second.Y);
        int right = Math.Max(first.X, second.X);
        int bottom = Math.Max(first.Y, second.Y);
        return new MapRect(left, top, checked(right - left + 1), checked(bottom - top + 1));
    }

    public bool Contains(MapPoint point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public MapRect Intersect(MapRect other)
    {
        int left = Math.Max(Left, other.Left);
        int top = Math.Max(Top, other.Top);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? default : new MapRect(left, top, right - left, bottom - top);
    }

    public MapRect Union(MapRect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        int left = Math.Min(Left, other.Left);
        int top = Math.Min(Top, other.Top);
        int right = Math.Max(Right, other.Right);
        int bottom = Math.Max(Bottom, other.Bottom);
        return new MapRect(left, top, right - left, bottom - top);
    }

    public IEnumerable<MapPoint> Cells()
    {
        for (int y = Top; y < Bottom; y++)
        {
            for (int x = Left; x < Right; x++) yield return new MapPoint(x, y);
        }
    }
}

public enum MapLayerKind
{
    Lower0 = 0,
    Lower1 = 1,
    Upper0 = 2,
    Upper1 = 3,
    Shadow = 4,
    Region = 5,
    Events = 6
}

public enum MapEditMode
{
    Automatic,
    ExplicitLayer
}

public enum MapToolKind
{
    Pencil,
    Rectangle,
    Ellipse,
    Line,
    FloodFill,
    Eraser,
    Stamp,
    Pattern,
    Random,
    Selection,
    MagicWand,
    Region,
    Shadow,
    Eyedropper,
    Events
}

public enum MapResizeAnchor
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Centre,
    Right,
    BottomLeft,
    Bottom,
    BottomRight
}

public readonly record struct MapDirtyBounds(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Right <= Left || Bottom <= Top;
    public MapRect Rect => IsEmpty ? default : new MapRect(Left, Top, Right - Left, Bottom - Top);

    public static MapDirtyBounds FromRect(MapRect rect) => rect.IsEmpty
        ? default
        : new MapDirtyBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);

    public MapDirtyBounds Union(MapDirtyBounds other) => FromRect(Rect.Union(other.Rect));
}

public readonly record struct MapLayerCell(int X, int Y, int Layer)
{
    public MapPoint Point => new(X, Y);
}
