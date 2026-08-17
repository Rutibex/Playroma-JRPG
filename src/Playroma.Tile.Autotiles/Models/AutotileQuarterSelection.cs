using System.Drawing;

namespace Playroma.Tile.Autotiles.Models;

public sealed record AutotileQuarterSelection(
    int Frame,
    int QuarterX,
    int QuarterY,
    Rectangle Bounds,
    string Label);
