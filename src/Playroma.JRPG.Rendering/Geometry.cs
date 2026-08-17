namespace Playroma.JRPG.Rendering;

public readonly record struct PixelPoint(int X, int Y)
{
    public static PixelPoint Zero { get; } = new(0, 0);
    public static PixelPoint operator +(PixelPoint left, PixelPoint right) => new(left.X + right.X, left.Y + right.Y);
    public static PixelPoint operator -(PixelPoint left, PixelPoint right) => new(left.X - right.X, left.Y - right.Y);
}

public readonly record struct PixelSize(int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public PixelPoint Location => new(X, Y);
    public PixelSize Size => new(Width, Height);

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public bool Intersects(PixelRect other) =>
        !IsEmpty && !other.IsEmpty && Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    public PixelRect Offset(int x, int y) => new(checked(X + x), checked(Y + y), Width, Height);

    public static PixelRect Intersect(PixelRect left, PixelRect right)
    {
        int x = Math.Max(left.Left, right.Left);
        int y = Math.Max(left.Top, right.Top);
        int r = Math.Min(left.Right, right.Right);
        int b = Math.Min(left.Bottom, right.Bottom);
        return r <= x || b <= y ? new PixelRect(x, y, 0, 0) : new PixelRect(x, y, r - x, b - y);
    }
}

public readonly record struct TileRect(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public PixelRect ToPixels(int tileWidth, int tileHeight) =>
        new(checked(X * tileWidth), checked(Y * tileHeight), checked(Width * tileWidth), checked(Height * tileHeight));
}
