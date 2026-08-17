using System.Drawing;

namespace Playroma.Tile.Autotiles.Services;

public static class MvAutotileTables
{
    public static readonly Point[][] Floor =
    [
        [new(2,4),new(1,4),new(2,3),new(1,3)], [new(2,0),new(1,4),new(2,3),new(1,3)],
        [new(2,4),new(3,0),new(2,3),new(1,3)], [new(2,0),new(3,0),new(2,3),new(1,3)],
        [new(2,4),new(1,4),new(2,3),new(3,1)], [new(2,0),new(1,4),new(2,3),new(3,1)],
        [new(2,4),new(3,0),new(2,3),new(3,1)], [new(2,0),new(3,0),new(2,3),new(3,1)],
        [new(2,4),new(1,4),new(2,1),new(1,3)], [new(2,0),new(1,4),new(2,1),new(1,3)],
        [new(2,4),new(3,0),new(2,1),new(1,3)], [new(2,0),new(3,0),new(2,1),new(1,3)],
        [new(2,4),new(1,4),new(2,1),new(3,1)], [new(2,0),new(1,4),new(2,1),new(3,1)],
        [new(2,4),new(3,0),new(2,1),new(3,1)], [new(2,0),new(3,0),new(2,1),new(3,1)],
        [new(0,4),new(1,4),new(0,3),new(1,3)], [new(0,4),new(3,0),new(0,3),new(1,3)],
        [new(0,4),new(1,4),new(0,3),new(3,1)], [new(0,4),new(3,0),new(0,3),new(3,1)],
        [new(2,2),new(1,2),new(2,3),new(1,3)], [new(2,2),new(1,2),new(2,3),new(3,1)],
        [new(2,2),new(1,2),new(2,1),new(1,3)], [new(2,2),new(1,2),new(2,1),new(3,1)],
        [new(2,4),new(3,4),new(2,3),new(3,3)], [new(2,4),new(3,4),new(2,1),new(3,3)],
        [new(2,0),new(3,4),new(2,3),new(3,3)], [new(2,0),new(3,4),new(2,1),new(3,3)],
        [new(2,4),new(1,4),new(2,5),new(1,5)], [new(2,0),new(1,4),new(2,5),new(1,5)],
        [new(2,4),new(3,0),new(2,5),new(1,5)], [new(2,0),new(3,0),new(2,5),new(1,5)],
        [new(0,4),new(3,4),new(0,3),new(3,3)], [new(2,2),new(1,2),new(2,5),new(1,5)],
        [new(0,2),new(1,2),new(0,3),new(1,3)], [new(0,2),new(1,2),new(0,3),new(3,1)],
        [new(2,2),new(3,2),new(2,3),new(3,3)], [new(2,2),new(3,2),new(2,1),new(3,3)],
        [new(2,4),new(3,4),new(2,5),new(3,5)], [new(2,0),new(3,4),new(2,5),new(3,5)],
        [new(0,4),new(1,4),new(0,5),new(1,5)], [new(0,4),new(3,0),new(0,5),new(1,5)],
        [new(0,2),new(3,2),new(0,3),new(3,3)], [new(0,2),new(1,2),new(0,5),new(1,5)],
        [new(0,4),new(3,4),new(0,5),new(3,5)], [new(2,2),new(3,2),new(2,5),new(3,5)],
        [new(0,2),new(3,2),new(0,5),new(3,5)], [new(0,0),new(1,0),new(0,1),new(1,1)]
    ];

    public static readonly Point[][] Wall =
    [
        [new(2,2),new(1,2),new(2,1),new(1,1)], [new(0,2),new(1,2),new(0,1),new(1,1)],
        [new(2,0),new(1,0),new(2,1),new(1,1)], [new(0,0),new(1,0),new(0,1),new(1,1)],
        [new(2,2),new(3,2),new(2,1),new(3,1)], [new(0,2),new(3,2),new(0,1),new(3,1)],
        [new(2,0),new(3,0),new(2,1),new(3,1)], [new(0,0),new(3,0),new(0,1),new(3,1)],
        [new(2,2),new(1,2),new(2,3),new(1,3)], [new(0,2),new(1,2),new(0,3),new(1,3)],
        [new(2,0),new(1,0),new(2,3),new(1,3)], [new(0,0),new(1,0),new(0,3),new(1,3)],
        [new(2,2),new(3,2),new(2,3),new(3,3)], [new(0,2),new(3,2),new(0,3),new(3,3)],
        [new(2,0),new(3,0),new(2,3),new(3,3)], [new(0,0),new(3,0),new(0,3),new(3,3)]
    ];

    public static readonly Point[][] Waterfall =
    [
        [new(2,0),new(1,0),new(2,1),new(1,1)], [new(0,0),new(1,0),new(0,1),new(1,1)],
        [new(2,0),new(3,0),new(2,1),new(3,1)], [new(0,0),new(3,0),new(0,1),new(3,1)]
    ];
}
