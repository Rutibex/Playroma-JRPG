namespace Playroma.Sprite.Core;

public enum SpriteColorMode { Rgba, Indexed, Grayscale }
public enum SpriteLayerKind { Image, Background, Group, Reference, Tilemap }
public enum AnimationPlaybackDirection { Forward, Reverse, PingPong, PingPongReverse }
[Flags]
public enum SpriteTileTransform { None = 0, FlipHorizontal = 1, FlipVertical = 2, Rotate90 = 4 }
public enum SpriteTileEditMode { Pixel, Tile, Manual, Automatic, Stack }

public sealed class SpriteDocument
{
    public int SchemaVersion { get; set; } = SpriteFormatVersion.Current;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled Sprite";
    public SpriteCanvas Canvas { get; set; } = new();
    public SpriteColorMode ColorMode { get; set; } = SpriteColorMode.Rgba;
    public string ColorProfile { get; set; } = "sRGB";
    public SpriteColorProfileMetadata ColorProfileMetadata { get; set; } = new();
    public Guid? ActivePaletteId { get; set; }
    public int TransparentPaletteIndex { get; set; } = 0;
    public List<SpriteLayer> Layers { get; set; } = [];
    public List<SpriteFrame> Frames { get; set; } = [];
    public List<SpriteAnimationTag> Tags { get; set; } = [];
    public List<SpriteSlice> Slices { get; set; } = [];
    public List<SpritePalette> Palettes { get; set; } = [];
    public List<SpriteTileset> Tilesets { get; set; } = [];
    public List<SpriteTilemapContent> TilemapContents { get; set; } = [];
    public List<SpritePixelContentDescriptor> PixelContents { get; set; } = [];
    public SpriteDocumentMetadata Metadata { get; set; } = new();

    public static SpriteDocument CreateEmpty(string name, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        SpriteFrame frame = new() { Number = 0, DurationMilliseconds = 100 };
        SpritePixelContentDescriptor content = new() { Width = width, Height = height };
        SpriteLayer layer = new()
        {
            Name = "Layer 1",
            Kind = SpriteLayerKind.Image,
            Cels = [new SpriteCel { FrameId = frame.Id, PixelContentId = content.Id }]
        };
        SpriteDocument document = new()
        {
            Name = name.Trim(),
            Canvas = new SpriteCanvas { Width = width, Height = height },
            Layers = [layer],
            Frames = [frame],
            PixelContents = [content]
        };
        SpriteDocumentValidator.ThrowIfInvalid(document);
        return document;
    }
}

public sealed class SpriteCanvas
{
    public int Width { get; set; } = 48;
    public int Height { get; set; } = 48;
    public int PixelAspectWidth { get; set; } = 1;
    public int PixelAspectHeight { get; set; } = 1;
}

public sealed class SpriteLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "Layer";
    public SpriteLayerKind Kind { get; set; } = SpriteLayerKind.Image;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public bool Continuous { get; set; }
    public byte Opacity { get; set; } = byte.MaxValue;
    public string BlendMode { get; set; } = "normal";
    public Guid? TilesetId { get; set; }
    public SpriteTileEditMode TileEditMode { get; set; } = SpriteTileEditMode.Manual;
    public List<SpriteCel> Cels { get; set; } = [];
    public List<SpriteTilemapCel> TilemapCels { get; set; } = [];
}

public sealed class SpriteFrame
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Number { get; set; }
    public int DurationMilliseconds { get; set; } = 100;
}

public sealed class SpriteCel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FrameId { get; set; }
    public Guid PixelContentId { get; set; }
    public IntPoint Position { get; set; }
    public byte Opacity { get; set; } = byte.MaxValue;
    public int ZIndex { get; set; }
}

public sealed class SpriteTilemapCel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FrameId { get; set; }
    public Guid TilemapContentId { get; set; }
    public IntPoint Position { get; set; }
    public byte Opacity { get; set; } = byte.MaxValue;
    public int ZIndex { get; set; }
}

public sealed class SpriteAnimationTag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Animation";
    public int FromFrame { get; set; }
    public int ToFrame { get; set; }
    public AnimationPlaybackDirection Direction { get; set; } = AnimationPlaybackDirection.Forward;
    public string? Color { get; set; }
    public Dictionary<string, string> UserData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SpriteSlice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Slice";
    public IntRect Bounds { get; set; }
    public IntPoint? Pivot { get; set; }
    public IntRect? Center { get; set; }
    public List<SpriteSliceKey> Keys { get; set; } = [];
    public Dictionary<string, string> UserData { get; set; } = new(StringComparer.Ordinal);

    public SpriteSliceKey Resolve(int frameNumber)
    {
        SpriteSliceKey? key = Keys.Where(item => item.FrameNumber <= frameNumber).OrderByDescending(item => item.FrameNumber).FirstOrDefault()
            ?? Keys.OrderBy(item => item.FrameNumber).FirstOrDefault();
        return key ?? new SpriteSliceKey { FrameNumber = 0, Bounds = Bounds, Pivot = Pivot, Center = Center };
    }
}

public sealed class SpriteSliceKey
{
    public int FrameNumber { get; set; }
    public IntRect Bounds { get; set; }
    public IntPoint? Pivot { get; set; }
    public IntRect? Center { get; set; }
}

public sealed class SpriteDocumentMetadata
{
    public string? RpgMakerMvProfileId { get; set; }
    public string? RuntimeRelativePath { get; set; }
    public string? ExportPresetId { get; set; }
    public Dictionary<string, string> UserData { get; set; } = new(StringComparer.Ordinal);
}


public readonly record struct SpriteColorValue(byte R, byte G, byte B, byte A = byte.MaxValue);

public sealed class SpriteColorProfileMetadata
{
    public string Name { get; set; } = "sRGB";
    public string Kind { get; set; } = "srgb";
    public double Gamma { get; set; } = 2.2;
    public string? EmbeddedProfileEntryName { get; set; }
    public Dictionary<string, string> UserData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SpritePalette
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Palette";
    public List<SpritePaletteEntry> Entries { get; set; } = [];
    public string? SourceFormat { get; set; }
    public Dictionary<string, string> UserData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SpritePaletteEntry
{
    public int Index { get; set; }
    public string? Name { get; set; }
    public SpriteColorValue Color { get; set; }
    public bool Locked { get; set; }
}

public sealed class SpriteTileset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Tileset";
    public int TileWidth { get; set; } = 48;
    public int TileHeight { get; set; } = 48;
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int Margin { get; set; }
    public int Spacing { get; set; }
    public string? SourceImagePath { get; set; }
    public string? SourceTileStudioProjectPath { get; set; }
    public Guid? ExternalTilesetId { get; set; }
    public int Revision { get; set; } = 1;
    public List<SpriteTileDefinition> Tiles { get; set; } = [];
    public List<SpriteAutoTileRule> AutoRules { get; set; } = [];
    public Dictionary<string, string> UserData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SpriteTileDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? PixelContentId { get; set; }
    public bool Empty { get; set; }
    public List<string> Tags { get; set; } = [];
}

public sealed class SpriteAutoTileRule
{
    public int NeighborMask { get; set; }
    public int TileIndex { get; set; }
    public SpriteTileTransform Transform { get; set; }
}

public sealed class SpriteTilemapContent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int EmptyTileIndex { get; set; } = -1;
    public List<SpriteTileCell> Cells { get; set; } = [];

    public int CellCount => checked(Columns * Rows);
    public SpriteTileCell GetCell(int x, int y) => Cells[checked(y * Columns + x)];
    public void SetCell(int x, int y, SpriteTileCell cell) => Cells[checked(y * Columns + x)] = cell;
}

public sealed class SpriteTileCell
{
    public int TileIndex { get; set; } = -1;
    public SpriteTileTransform Transform { get; set; }
    public byte Opacity { get; set; } = byte.MaxValue;
}

public sealed class SpritePixelContentDescriptor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Width { get; set; }
    public int Height { get; set; }
    public string Encoding { get; set; } = "rgba32";
    public string EntryName => $"pixels/{Id:N}.rgba";
}
