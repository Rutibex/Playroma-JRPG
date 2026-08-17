namespace Playroma.JRPG.Rendering;

public readonly record struct TileId(int Value)
{
    public bool IsVisible => MvTileRules.IsVisible(Value);
    public bool IsAutotile => MvTileRules.IsAutotile(Value);
    public AutotileKind AutotileKind => new(MvTileRules.GetAutotileKind(Value));
    public AutotileShape AutotileShape => new(MvTileRules.GetAutotileShape(Value));
    public static implicit operator int(TileId value) => value.Value;
    public static explicit operator TileId(int value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct AutotileKind(int Value)
{
    public int X => Value % 8;
    public int Y => Value / 8;
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct AutotileShape(int Value)
{
    public bool IsValidForFloor => Value is >= 0 and < 48;
    public bool IsValidForWall => Value is >= 0 and < 16;
    public bool IsValidForWaterfall => Value is >= 0 and < 4;
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum TileSheetKind
{
    A1 = 0,
    A2 = 1,
    A3 = 2,
    A4 = 3,
    A5 = 4,
    B = 5,
    C = 6,
    D = 7,
    E = 8
}

public enum TileLayer
{
    Lower0 = 0,
    Lower1 = 1,
    Upper0 = 2,
    Upper1 = 3,
    Shadow = 4,
    Region = 5,
    EventBelow = 6,
    EventSame = 7,
    EventAbove = 8
}

[Flags]
public enum PassageDirection
{
    None = 0,
    Down = 0x01,
    Left = 0x02,
    Right = 0x04,
    Up = 0x08
}

public readonly record struct TilesetFlags(int RawValue)
{
    public PassageDirection BlockedDirections => (PassageDirection)(RawValue & 0x0f);
    public bool IsStar => (RawValue & 0x10) != 0;
    public bool IsLadder => (RawValue & 0x20) != 0;
    public bool IsBush => (RawValue & 0x40) != 0;
    public bool IsCounter => (RawValue & 0x80) != 0;
    public bool IsDamageFloor => (RawValue & 0x100) != 0;
    public int TerrainTag => (RawValue >> 12) & 0x0f;
    public bool IsHigher => IsStar;
}

public enum RenderPass
{
    Parallax = 0,
    LowerTiles = 10,
    EventBelow = 20,
    EventSame = 30,
    UpperTiles = 40,
    EventAbove = 50
}

public enum RenderImageKind
{
    Tileset,
    Character,
    Parallax,
    Placeholder
}

public readonly record struct RenderImageKey(RenderImageKind Kind, string Name, TileSheetKind? Sheet = null)
{
    public static RenderImageKey Tileset(TileSheetKind sheet) => new(RenderImageKind.Tileset, sheet.ToString(), sheet);
    public static RenderImageKey Character(string name) => new(RenderImageKind.Character, name);
    public static RenderImageKey Parallax(string name) => new(RenderImageKind.Parallax, name);
    public static RenderImageKey Placeholder(string name) => new(RenderImageKind.Placeholder, name);
}

public sealed record TileRenderInstruction(
    RenderPass Pass,
    int Sequence,
    RenderImageKey Image,
    PixelRect Source,
    PixelRect Destination,
    byte Opacity = 255,
    int TileId = 0,
    TileLayer Layer = TileLayer.Lower0);

public sealed record FillRenderInstruction(
    RenderPass Pass,
    int Sequence,
    PixelRect Destination,
    Rgba32 Colour,
    TileLayer Layer = TileLayer.Shadow);

public sealed record CharacterRenderInstruction(
    RenderPass Pass,
    int Sequence,
    string CharacterName,
    int CharacterIndex,
    int Direction,
    int Pattern,
    int MapX,
    int MapY,
    int EventId);

public sealed record MapRenderPlan(
    PixelRect Region,
    IReadOnlyList<TileRenderInstruction> Blits,
    IReadOnlyList<FillRenderInstruction> Fills,
    IReadOnlyList<CharacterRenderInstruction> Characters,
    IReadOnlyList<Playroma.JRPG.Core.ProjectDiagnostic> Diagnostics);
