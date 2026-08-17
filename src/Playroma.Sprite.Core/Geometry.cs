namespace Playroma.Sprite.Core;

public readonly record struct IntPoint(int X, int Y);

public readonly record struct IntSize
{
    public IntSize(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width; Height = height;
    }
    public int Width { get; }
    public int Height { get; }
}

public readonly record struct IntRect
{
    public IntRect(int x, int y, int width, int height)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        X = x; Y = y; Width = width; Height = height;
    }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
}
