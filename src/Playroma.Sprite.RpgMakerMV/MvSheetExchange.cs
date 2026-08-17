using Playroma.Sprite.Animation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.RpgMakerMV;

public sealed record MvSheetLayout(int Columns, int Rows, int CellWidth, int CellHeight)
{
    public int CellCount => checked(Columns * Rows);
    public int Width => checked(Columns * CellWidth);
    public int Height => checked(Rows * CellHeight);
}

public sealed record MvSheetAssemblyOptions(
    int? Columns = null,
    bool PadIncompleteRow = true,
    Rgba32? PaddingColor = null,
    bool RequireExactProfileCellCount = true);

public static class MvSheetExchange
{
    public static MvSheetLayout ResolveLayout(RpgMakerMvImageProfile profile, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        MvGridRule grid = profile.Grid ?? throw new InvalidOperationException($"Profile '{profile.Id}' does not define a sheet grid.");

        int columns;
        int rows;
        int cellWidth;
        int cellHeight;

        if (grid.CellWidth is int fixedCellWidth)
        {
            if (width % fixedCellWidth != 0) throw new InvalidDataException($"Width {width} is not divisible by {fixedCellWidth}.");
            cellWidth = fixedCellWidth;
            columns = width / fixedCellWidth;
        }
        else
        {
            columns = grid.Columns ?? throw new InvalidDataException("The profile does not define columns or cell width.");
            if (width % columns != 0) throw new InvalidDataException($"Width {width} is not divisible by {columns} columns.");
            cellWidth = width / columns;
        }

        if (grid.CellHeight is int fixedCellHeight)
        {
            if (height % fixedCellHeight != 0) throw new InvalidDataException($"Height {height} is not divisible by {fixedCellHeight}.");
            cellHeight = fixedCellHeight;
            rows = height / fixedCellHeight;
        }
        else
        {
            rows = grid.Rows ?? throw new InvalidDataException("The profile does not define rows or cell height.");
            if (height % rows != 0) throw new InvalidDataException($"Height {height} is not divisible by {rows} rows.");
            cellHeight = height / rows;
        }

        if (grid.Columns is int exactColumns && columns != exactColumns)
            throw new InvalidDataException($"Profile requires exactly {exactColumns} columns, found {columns}.");
        if (grid.Rows is int exactRows && rows != exactRows)
            throw new InvalidDataException($"Profile requires exactly {exactRows} rows, found {rows}.");
        if (grid.MinimumRows is int minimumRows && rows < minimumRows)
            throw new InvalidDataException($"Profile requires at least {minimumRows} rows.");
        if (grid.MaximumRows is int maximumRows && rows > maximumRows)
            throw new InvalidDataException($"Profile permits at most {maximumRows} rows.");
        if (grid.MaximumCells is int maximumCells && checked(columns * rows) > maximumCells)
            throw new InvalidDataException($"Profile permits at most {maximumCells} cells.");

        return new MvSheetLayout(columns, rows, cellWidth, cellHeight);
    }

    public static IReadOnlyList<PixelBuffer> Split(PixelBuffer sheet, RpgMakerMvImageProfile profile)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        MvSheetLayout layout = ResolveLayout(profile, sheet.Width, sheet.Height);
        PixelBuffer[] cells = new PixelBuffer[layout.CellCount];
        for (int row = 0; row < layout.Rows; row++)
        for (int column = 0; column < layout.Columns; column++)
        {
            PixelBuffer cell = new(layout.CellWidth, layout.CellHeight);
            CopyRegion(sheet, new IntRect(column * layout.CellWidth, row * layout.CellHeight, layout.CellWidth, layout.CellHeight), cell, 0, 0);
            cells[row * layout.Columns + column] = cell;
        }
        return cells;
    }

    public static PixelBuffer Assemble(IReadOnlyList<PixelBuffer> cells, RpgMakerMvImageProfile profile, MvSheetAssemblyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(profile);
        if (cells.Count == 0) throw new ArgumentException("At least one cell is required.", nameof(cells));
        options ??= new MvSheetAssemblyOptions();
        int cellWidth = cells[0].Width;
        int cellHeight = cells[0].Height;
        if (cells.Any(cell => cell.Width != cellWidth || cell.Height != cellHeight))
            throw new InvalidDataException("Every source cell must have identical dimensions.");

        MvGridRule grid = profile.Grid ?? throw new InvalidOperationException($"Profile '{profile.Id}' does not define a sheet grid.");
        if (grid.CellWidth is int requiredCellWidth && requiredCellWidth != cellWidth)
            throw new InvalidDataException($"Profile requires {requiredCellWidth}-pixel-wide cells.");
        if (grid.CellHeight is int requiredCellHeight && requiredCellHeight != cellHeight)
            throw new InvalidDataException($"Profile requires {requiredCellHeight}-pixel-high cells.");

        int columns = options.Columns ?? grid.Columns ?? 1;
        if (columns <= 0) throw new InvalidDataException("Column count must be positive.");
        int rows = checked((cells.Count + columns - 1) / columns);

        if (grid.Rows is int exactRows)
        {
            int exactCells = checked(columns * exactRows);
            if (options.RequireExactProfileCellCount && cells.Count != exactCells)
                throw new InvalidDataException($"Profile requires exactly {exactCells} cells ({columns}×{exactRows}).");
            if (cells.Count > exactCells) throw new InvalidDataException($"Profile has room for only {exactCells} cells.");
            rows = exactRows;
        }
        if (grid.MinimumRows is int minimumRows) rows = Math.Max(rows, minimumRows);
        if (grid.MaximumRows is int maximumRows && rows > maximumRows)
            throw new InvalidDataException($"Profile supports at most {maximumRows} rows.");
        if (grid.MaximumCells is int maximumCells && cells.Count > maximumCells)
            throw new InvalidDataException($"Profile supports at most {maximumCells} cells.");
        if (!options.PadIncompleteRow && cells.Count % columns != 0)
            throw new InvalidDataException("The final row is incomplete and padding is disabled.");

        PixelBuffer sheet = new(checked(columns * cellWidth), checked(rows * cellHeight));
        sheet.Clear(options.PaddingColor ?? Rgba32.Transparent);
        for (int index = 0; index < cells.Count; index++)
        {
            int x = (index % columns) * cellWidth;
            int y = (index / columns) * cellHeight;
            CopyRegion(cells[index], new IntRect(0, 0, cellWidth, cellHeight), sheet, x, y);
        }
        return sheet;
    }

    public static SpritePackageData SplitToAnimationPackage(PixelBuffer sheet, RpgMakerMvImageProfile profile, string name, int frameDurationMilliseconds = 100)
    {
        IReadOnlyList<PixelBuffer> cells = Split(sheet, profile);
        SpritePackageData package = CreateAnimationPackage(name, cells, frameDurationMilliseconds, profile.Id);
        AddSemanticTags(package, profile);
        return package;
    }

    public static PixelBuffer AssemblePackageFrames(SpritePackageData package, RpgMakerMvImageProfile profile, IEnumerable<int>? frameIndexes = null, MvSheetAssemblyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        int[] indexes = (frameIndexes ?? Enumerable.Range(0, package.Document.Frames.Count)).Distinct().OrderBy(index => index).ToArray();
        if (indexes.Length == 0) throw new InvalidDataException("No frames were selected.");
        if (indexes.Any(index => index < 0 || index >= package.Document.Frames.Count)) throw new ArgumentOutOfRangeException(nameof(frameIndexes));
        PixelBuffer[] cells = indexes.Select(index => AnimationFrameRenderer.RenderFrame(package, index)).ToArray();
        return Assemble(cells, profile, options);
    }

    public static SpritePackageData CreateAnimationPackage(string name, IReadOnlyList<PixelBuffer> frames, int durationMilliseconds, string? profileId = null)
    {
        if (frames.Count == 0) throw new InvalidDataException("The animation contains no frames.");
        int width = frames[0].Width;
        int height = frames[0].Height;
        if (frames.Any(frame => frame.Width != width || frame.Height != height))
            throw new InvalidDataException("Every frame must have identical dimensions.");

        SpritePackageData package = SpritePackageData.CreateEmpty(name, width, height);
        SpriteDocument document = package.Document;
        SpriteLayer layer = document.Layers[0];
        document.Frames.Clear();
        document.PixelContents.Clear();
        layer.Cels.Clear();
        package.PixelBuffers.Clear();
        for (int index = 0; index < frames.Count; index++)
        {
            SpriteFrame frame = new() { Number = index, DurationMilliseconds = Math.Max(1, durationMilliseconds) };
            SpritePixelContentDescriptor descriptor = new() { Width = width, Height = height };
            document.Frames.Add(frame);
            document.PixelContents.Add(descriptor);
            package.PixelBuffers[descriptor.Id] = frames[index].Clone();
            layer.Cels.Add(new SpriteCel { FrameId = frame.Id, PixelContentId = descriptor.Id });
        }
        document.Metadata.RpgMakerMvProfileId = profileId;
        SpriteDocumentValidator.ThrowIfInvalid(document);
        return package;
    }

    public static void AddSemanticTags(SpritePackageData package, RpgMakerMvImageProfile profile)
    {
        package.Document.Tags.Clear();
        if (profile.Id.Equals("mv-sv-actor", StringComparison.OrdinalIgnoreCase))
        {
            foreach (MvMotionDefinition motion in profile.Motions)
            {
                int from = motion.StartCell;
                int to = Math.Min(package.Document.Frames.Count - 1, from + motion.FrameCount - 1);
                if (from > to) continue;
                SpriteAnimationTag tag = new()
                {
                    Name = motion.DisplayName,
                    FromFrame = from,
                    ToFrame = to,
                    Direction = motion.Playback.Equals("loop", StringComparison.OrdinalIgnoreCase)
                        ? AnimationPlaybackDirection.PingPong
                        : AnimationPlaybackDirection.Forward
                };
                tag.UserData["mvMotionId"] = motion.Id;
                package.Document.Tags.Add(tag);
            }
            return;
        }

        if (profile.Id is "mv-character-single" or "mv-character-standard")
        {
            int characterCount = profile.Id.Equals("mv-character-standard", StringComparison.OrdinalIgnoreCase) ? 8 : 1;
            string[] directions = ["Down", "Left", "Right", "Up"];
            for (int character = 0; character < characterCount; character++)
            for (int direction = 0; direction < 4; direction++)
            {
                int blockColumn = character % 4;
                int blockRow = character / 4;
                int sheetColumns = characterCount == 8 ? 12 : 3;
                int start = (blockRow * 4 + direction) * sheetColumns + blockColumn * 3;
                SpriteAnimationTag tag = new()
                {
                    Name = characterCount == 1 ? directions[direction] : $"Character {character + 1} {directions[direction]}",
                    FromFrame = start,
                    ToFrame = start + 2,
                    Direction = AnimationPlaybackDirection.PingPong
                };
                tag.UserData["mvDirection"] = directions[direction].ToLowerInvariant();
                tag.UserData["mvCharacterIndex"] = character.ToString(System.Globalization.CultureInfo.InvariantCulture);
                package.Document.Tags.Add(tag);
            }
            return;
        }

        if (package.Document.Frames.Count > 1)
            package.Document.Tags.Add(new SpriteAnimationTag { Name = profile.DisplayName, FromFrame = 0, ToFrame = package.Document.Frames.Count - 1 });
    }

    private static void CopyRegion(PixelBuffer source, IntRect sourceRect, PixelBuffer destination, int destinationX, int destinationY)
    {
        if (sourceRect.X < 0 || sourceRect.Y < 0 || sourceRect.Right > source.Width || sourceRect.Bottom > source.Height)
            throw new ArgumentOutOfRangeException(nameof(sourceRect));
        if (destinationX < 0 || destinationY < 0 || destinationX + sourceRect.Width > destination.Width || destinationY + sourceRect.Height > destination.Height)
            throw new ArgumentOutOfRangeException(nameof(destinationX));
        for (int y = 0; y < sourceRect.Height; y++)
        for (int x = 0; x < sourceRect.Width; x++)
            destination.SetPixel(destinationX + x, destinationY + y, source.GetPixel(sourceRect.X + x, sourceRect.Y + y));
    }
}
