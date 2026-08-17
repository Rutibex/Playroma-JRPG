namespace Playroma.JRPG.Assets;

public sealed record SheetCell(int X, int Y, int Width, int Height, int Index);

public sealed record CharacterSheetDescriptor(
    bool IsSingleCharacter,
    bool IsObjectCharacter,
    int CharacterColumns,
    int CharacterRows,
    int FramesPerCharacterX,
    int FramesPerCharacterY,
    int CellWidth,
    int CellHeight,
    IReadOnlyList<SheetCell> Cells,
    bool IsValid,
    string? ValidationMessage);

public sealed record GridSheetDescriptor(
    int Columns,
    int Rows,
    int CellWidth,
    int CellHeight,
    IReadOnlyList<SheetCell> Cells,
    bool IsValid,
    string? ValidationMessage);

public static class MvSpriteSheetDescriptors
{
    public static CharacterSheetDescriptor DescribeCharacter(string logicalName, int width, int height)
    {
        string fileName = Path.GetFileName(logicalName);
        bool single = fileName.StartsWith('$') || fileName.StartsWith("!$", StringComparison.Ordinal);
        bool objectCharacter = fileName.StartsWith('!');
        int characterColumns = single ? 1 : 4;
        int characterRows = single ? 1 : 2;
        int totalColumns = characterColumns * 3;
        int totalRows = characterRows * 4;
        bool valid = width > 0 && height > 0 && width % totalColumns == 0 && height % totalRows == 0;
        int cellWidth = valid ? width / totalColumns : 0;
        int cellHeight = valid ? height / totalRows : 0;
        return new CharacterSheetDescriptor(
            single,
            objectCharacter,
            characterColumns,
            characterRows,
            3,
            4,
            cellWidth,
            cellHeight,
            valid ? BuildCells(totalColumns, totalRows, cellWidth, cellHeight) : [],
            valid,
            valid ? null : $"The image dimensions {width}×{height} are not divisible by the required {totalColumns}×{totalRows} character grid.");
    }

    public static GridSheetDescriptor DescribeFaces(int width, int height) => DescribeGrid(width, height, 4, 2, "face");

    public static GridSheetDescriptor DescribeIconSet(int width, int height, int iconSize = 32) =>
        width > 0 && height > 0 && width % iconSize == 0 && height % iconSize == 0
            ? DescribeGrid(width, height, width / iconSize, height / iconSize, "icon")
            : new GridSheetDescriptor(0, 0, 0, 0, [], false, $"The image dimensions {width}×{height} are not divisible by {iconSize}.");

    public static GridSheetDescriptor DescribeAnimation(int width, int height, int cellSize = 192)
    {
        if (width <= 0 || height <= 0 || width % cellSize != 0 || height % cellSize != 0)
        {
            return new GridSheetDescriptor(0, 0, 0, 0, [], false, $"The animation sheet dimensions {width}×{height} are not divisible by {cellSize}.");
        }

        return DescribeGrid(width, height, width / cellSize, height / cellSize, "animation");
    }

    public static GridSheetDescriptor DescribeSideViewActor(int width, int height)
    {
        GridSheetDescriptor descriptor = DescribeGrid(width, height, 9, 6, "side-view actor");
        return descriptor;
    }

    public static GridSheetDescriptor DescribeTiles(int width, int height, int tileSize = 48) =>
        width > 0 && height > 0 && width % tileSize == 0 && height % tileSize == 0
            ? DescribeGrid(width, height, width / tileSize, height / tileSize, "tile")
            : new GridSheetDescriptor(0, 0, 0, 0, [], false, $"The tileset dimensions {width}×{height} are not divisible by {tileSize}.");

    private static GridSheetDescriptor DescribeGrid(int width, int height, int columns, int rows, string kind)
    {
        bool valid = width > 0 && height > 0 && columns > 0 && rows > 0 && width % columns == 0 && height % rows == 0;
        int cellWidth = valid ? width / columns : 0;
        int cellHeight = valid ? height / rows : 0;
        return new GridSheetDescriptor(
            columns,
            rows,
            cellWidth,
            cellHeight,
            valid ? BuildCells(columns, rows, cellWidth, cellHeight) : [],
            valid,
            valid ? null : $"The image dimensions {width}×{height} are not divisible by the required {columns}×{rows} {kind} grid.");
    }

    private static IReadOnlyList<SheetCell> BuildCells(int columns, int rows, int cellWidth, int cellHeight)
    {
        List<SheetCell> cells = new(columns * rows);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int index = checked(row * columns + column);
                cells.Add(new SheetCell(column * cellWidth, row * cellHeight, cellWidth, cellHeight, index));
            }
        }

        return cells;
    }
}
