using System.Drawing;

namespace Playroma.Tile.RpgMakerMV.Formats;

public sealed record MvRegionDefinition(
    int Index,
    Rectangle Bounds,
    string Kind,
    string DefaultName);
