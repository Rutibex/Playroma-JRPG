using System.Drawing;
using Playroma.Tile.Autotiles.Models;

namespace Playroma.Tile.Autotiles.Services;

public sealed class AutotileShapeResolver
{
    public int Resolve(AutotileFamily family, NeighborMask neighbors) => family switch
    {
        AutotileFamily.Floor => ResolveFloor(neighbors),
        AutotileFamily.Wall => ResolveWall(neighbors),
        AutotileFamily.Waterfall => ResolveWaterfall(neighbors),
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
    };

    public NeighborMask NormalizeFloorMask(NeighborMask mask)
    {
        if (!Has(mask, NeighborMask.North) || !Has(mask, NeighborMask.West)) mask &= ~NeighborMask.NorthWest;
        if (!Has(mask, NeighborMask.North) || !Has(mask, NeighborMask.East)) mask &= ~NeighborMask.NorthEast;
        if (!Has(mask, NeighborMask.South) || !Has(mask, NeighborMask.West)) mask &= ~NeighborMask.SouthWest;
        if (!Has(mask, NeighborMask.South) || !Has(mask, NeighborMask.East)) mask &= ~NeighborMask.SouthEast;
        return mask;
    }

    private int ResolveFloor(NeighborMask raw)
    {
        var mask = NormalizeFloorMask(raw);
        var cardinals = mask & (NeighborMask.North | NeighborMask.East | NeighborMask.South | NeighborMask.West);
        if (cardinals == NeighborMask.None)
        {
            return 46;
        }

        var target = new[]
        {
            FloorTopLeft(mask), FloorTopRight(mask), FloorBottomLeft(mask), FloorBottomRight(mask)
        };
        return FindShape(MvAutotileTables.Floor, target);
    }

    private static int ResolveWall(NeighborMask mask)
    {
        var target = new[]
        {
            WallTopLeft(mask), WallTopRight(mask), WallBottomLeft(mask), WallBottomRight(mask)
        };
        return FindShape(MvAutotileTables.Wall, target);
    }

    private static int ResolveWaterfall(NeighborMask mask)
    {
        var left = Has(mask, NeighborMask.West);
        var right = Has(mask, NeighborMask.East);
        var target = new[]
        {
            left ? new Point(2, 0) : new Point(0, 0),
            right ? new Point(1, 0) : new Point(3, 0),
            left ? new Point(2, 1) : new Point(0, 1),
            right ? new Point(1, 1) : new Point(3, 1)
        };
        return FindShape(MvAutotileTables.Waterfall, target);
    }

    private static Point FloorTopLeft(NeighborMask m)
    {
        var n = Has(m, NeighborMask.North); var w = Has(m, NeighborMask.West);
        if (n && w) return Has(m, NeighborMask.NorthWest) ? new(2, 4) : new(2, 0);
        if (n) return new(0, 4);
        if (w) return new(2, 2);
        return new(0, 2);
    }

    private static Point FloorTopRight(NeighborMask m)
    {
        var n = Has(m, NeighborMask.North); var e = Has(m, NeighborMask.East);
        if (n && e) return Has(m, NeighborMask.NorthEast) ? new(1, 4) : new(3, 0);
        if (n) return new(3, 4);
        if (e) return new(1, 2);
        return new(3, 2);
    }

    private static Point FloorBottomLeft(NeighborMask m)
    {
        var s = Has(m, NeighborMask.South); var w = Has(m, NeighborMask.West);
        if (s && w) return Has(m, NeighborMask.SouthWest) ? new(2, 3) : new(2, 1);
        if (s) return new(0, 3);
        if (w) return new(2, 5);
        return new(0, 5);
    }

    private static Point FloorBottomRight(NeighborMask m)
    {
        var s = Has(m, NeighborMask.South); var e = Has(m, NeighborMask.East);
        if (s && e) return Has(m, NeighborMask.SouthEast) ? new(1, 3) : new(3, 1);
        if (s) return new(3, 3);
        if (e) return new(1, 5);
        return new(3, 5);
    }

    private static Point WallTopLeft(NeighborMask m) =>
        Has(m, NeighborMask.North) ? (Has(m, NeighborMask.West) ? new(2, 2) : new(0, 2))
                                    : (Has(m, NeighborMask.West) ? new(2, 0) : new(0, 0));
    private static Point WallTopRight(NeighborMask m) =>
        Has(m, NeighborMask.North) ? (Has(m, NeighborMask.East) ? new(1, 2) : new(3, 2))
                                    : (Has(m, NeighborMask.East) ? new(1, 0) : new(3, 0));
    private static Point WallBottomLeft(NeighborMask m) =>
        Has(m, NeighborMask.South) ? (Has(m, NeighborMask.West) ? new(2, 1) : new(0, 1))
                                    : (Has(m, NeighborMask.West) ? new(2, 3) : new(0, 3));
    private static Point WallBottomRight(NeighborMask m) =>
        Has(m, NeighborMask.South) ? (Has(m, NeighborMask.East) ? new(1, 1) : new(3, 1))
                                    : (Has(m, NeighborMask.East) ? new(1, 3) : new(3, 3));

    private static int FindShape(Point[][] table, IReadOnlyList<Point> target)
    {
        for (var shape = 0; shape < table.Length; shape++)
        {
            var candidate = table[shape];
            if (candidate[0] == target[0] && candidate[1] == target[1] &&
                candidate[2] == target[2] && candidate[3] == target[3])
            {
                return shape;
            }
        }
        throw new InvalidOperationException("The neighbour mask did not resolve to an MV autotile shape.");
    }

    private static bool Has(NeighborMask value, NeighborMask flag) => (value & flag) != 0;
}
