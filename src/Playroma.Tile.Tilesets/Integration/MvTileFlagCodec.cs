using Playroma.Tile.Core.Models;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Integration;

public static class MvTileFlagCodec
{
    public const int Down = 0x01;
    public const int Left = 0x02;
    public const int Right = 0x04;
    public const int Up = 0x08;
    public const int Star = 0x10;
    public const int Ladder = 0x20;
    public const int Bush = 0x40;
    public const int Counter = 0x80;
    public const int DamageFloor = 0x100;
    public const int TerrainTagShift = 12;
    public const int TerrainTagMask = 0x7000;

    public static int Encode(TileGameplayMetadata metadata)
    {
        var flags = metadata.Passage switch
        {
            PassageMode.Impassable => Down | Left | Right | Up,
            PassageMode.AboveCharacters => Star,
            PassageMode.Directional => EncodeDirections(metadata.BlockedDirections),
            _ => 0
        };
        if (metadata.Ladder) flags |= Ladder;
        if (metadata.Bush) flags |= Bush;
        if (metadata.Counter) flags |= Counter;
        if (metadata.DamageFloor) flags |= DamageFloor;
        flags |= (Math.Clamp(metadata.TerrainTag, 0, 7) << TerrainTagShift);
        return flags;
    }

    public static TileGameplayMetadata Decode(int flags)
    {
        var directions = DecodeDirections(flags);
        var passage = (flags & Star) != 0
            ? PassageMode.AboveCharacters
            : directions == DirectionPassageFlags.All
                ? PassageMode.Impassable
                : directions == DirectionPassageFlags.None
                    ? PassageMode.Passable
                    : PassageMode.Directional;
        return new TileGameplayMetadata
        {
            Passage = passage,
            BlockedDirections = directions,
            Ladder = (flags & Ladder) != 0,
            Bush = (flags & Bush) != 0,
            Counter = (flags & Counter) != 0,
            DamageFloor = (flags & DamageFloor) != 0,
            TerrainTag = (flags & TerrainTagMask) >> TerrainTagShift
        };
    }

    public static IEnumerable<int> GetMvTileIds(MvSheetSlot sheet, int logicalIndex)
    {
        var baseId = sheet switch
        {
            MvSheetSlot.B => 0,
            MvSheetSlot.C => 256,
            MvSheetSlot.D => 512,
            MvSheetSlot.E => 768,
            MvSheetSlot.A5 => 1536,
            MvSheetSlot.A1 => 2048,
            MvSheetSlot.A2 => 2816,
            MvSheetSlot.A3 => 4352,
            MvSheetSlot.A4 => 5888,
            _ => 0
        };
        if (sheet is MvSheetSlot.A1 or MvSheetSlot.A2 or MvSheetSlot.A3 or MvSheetSlot.A4)
        {
            for (var shape = 0; shape < 48; shape++) yield return baseId + logicalIndex * 48 + shape;
        }
        else
        {
            yield return baseId + logicalIndex;
        }
    }

    private static int EncodeDirections(DirectionPassageFlags directions)
    {
        var flags = 0;
        if (directions.HasFlag(DirectionPassageFlags.Down)) flags |= Down;
        if (directions.HasFlag(DirectionPassageFlags.Left)) flags |= Left;
        if (directions.HasFlag(DirectionPassageFlags.Right)) flags |= Right;
        if (directions.HasFlag(DirectionPassageFlags.Up)) flags |= Up;
        return flags;
    }

    private static DirectionPassageFlags DecodeDirections(int flags)
    {
        var result = DirectionPassageFlags.None;
        if ((flags & Down) != 0) result |= DirectionPassageFlags.Down;
        if ((flags & Left) != 0) result |= DirectionPassageFlags.Left;
        if ((flags & Right) != 0) result |= DirectionPassageFlags.Right;
        if ((flags & Up) != 0) result |= DirectionPassageFlags.Up;
        return result;
    }
}
