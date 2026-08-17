using Playroma.Sprite.Core;

namespace Playroma.Sprite.RpgMakerMV;

public static class MvGuideLayout
{
    public static IReadOnlyList<IntRect> CreateCellRects(RpgMakerMvImageProfile profile, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(profile);
        MvGridRule grid = profile.Grid ?? throw new InvalidOperationException("The profile has no grid.");
        int columns = grid.Columns ?? (grid.CellWidth is int cellWidth ? width / cellWidth : throw new InvalidOperationException("Grid columns are undefined."));
        int rows = grid.Rows ?? (grid.CellHeight is int cellHeight ? height / cellHeight : throw new InvalidOperationException("Grid rows are undefined."));
        if (columns <= 0 || rows <= 0 || width % columns != 0 || height % rows != 0) throw new ArgumentException("Dimensions do not fit the profile grid.");
        int cellW = width / columns, cellH = height / rows;
        List<IntRect> result = new(columns * rows);
        for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++) result.Add(new(column * cellW, row * cellH, cellW, cellH));
        return result;
    }
}
