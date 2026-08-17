using Playroma.JRPG.Rendering;

namespace Playroma.JRPG.Mapping;

[Flags]
public enum AutotileNeighbours
{
    None = 0,
    North = 1 << 0,
    East = 1 << 1,
    South = 1 << 2,
    West = 1 << 3,
    NorthEast = 1 << 4,
    SouthEast = 1 << 5,
    SouthWest = 1 << 6,
    NorthWest = 1 << 7
}

public sealed class AutotileTopologyResolver
{
    private static readonly Dictionary<QuarterSignature, int> FloorShapes = BuildFloorShapes();
    private static readonly Dictionary<QuarterSignature, int> WallShapes = BuildWallShapes();
    private static readonly Dictionary<QuarterSignature, int> WaterfallShapes = BuildWaterfallShapes();

    public int ResolveShape(AutotileNeighbours neighbours, int sampleTileId)
    {
        if (!MvTileRules.IsAutotile(sampleTileId)) throw new ArgumentOutOfRangeException(nameof(sampleTileId));
        if (MvTileRules.IsWaterfallTypeAutotile(sampleTileId)) return ResolveWaterfall(neighbours);
        if (MvTileRules.IsWallTypeAutotile(sampleTileId)) return ResolveWall(neighbours);
        return ResolveFloor(neighbours);
    }

    public int ResolveTileId(IReadOnlyMapBuffer map, int x, int y, int layer, int sampleTileId)
    {
        ArgumentNullException.ThrowIfNull(map);
        int kind = MvTileRules.GetAutotileKind(sampleTileId);
        AutotileNeighbours neighbours = GetNeighbours(map, x, y, layer, kind);
        return MvTileRules.MakeAutotileId(kind, ResolveShape(neighbours, sampleTileId));
    }

    public IReadOnlyCollection<MapPoint> RecalculateConnectedArea(
        EditableMapBuffer map,
        MapPoint changed,
        int layer,
        int maximumCells = 100_000)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.IsInside(changed.X, changed.Y)) return [];
        int tile = map.GetEffective(changed.X, changed.Y, layer);
        if (!MvTileRules.IsAutotile(tile)) return Neighbourhood(changed, map).ToArray();
        int targetKind = MvTileRules.GetAutotileKind(tile);
        Queue<MapPoint> pending = new();
        HashSet<MapPoint> result = [];
        pending.Enqueue(changed);
        while (pending.Count > 0)
        {
            MapPoint point = pending.Dequeue();
            if (!map.IsInside(point.X, point.Y) || !result.Add(point)) continue;
            int current = map.GetEffective(point.X, point.Y, layer);
            if (!MvTileRules.IsAutotile(current) || MvTileRules.GetAutotileKind(current) != targetKind) continue;
            if (result.Count > maximumCells) throw new InvalidOperationException("Autotile recalculation exceeded the safety limit.");
            pending.Enqueue(new MapPoint(point.X, point.Y - 1));
            pending.Enqueue(new MapPoint(point.X + 1, point.Y));
            pending.Enqueue(new MapPoint(point.X, point.Y + 1));
            pending.Enqueue(new MapPoint(point.X - 1, point.Y));
        }
        HashSet<MapPoint> expanded = [];
        foreach (MapPoint point in result)
        {
            foreach (MapPoint candidate in Neighbourhood(point, map)) expanded.Add(candidate);
        }
        return expanded;
    }

    public void Reshape(EditableMapBuffer map, IEnumerable<MapPoint> cells, int layer)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(cells);
        foreach (MapPoint point in cells.Distinct())
        {
            if (!map.IsInside(point.X, point.Y)) continue;
            int tile = map.GetEffective(point.X, point.Y, layer);
            if (!MvTileRules.IsAutotile(tile)) continue;
            int resolved = ResolveTileId(new EffectiveMapBuffer(map), point.X, point.Y, layer, tile);
            map.PreviewSet(point.X, point.Y, layer, resolved);
        }
    }

    public static AutotileNeighbours GetNeighbours(IReadOnlyMapBuffer map, int x, int y, int layer, int kind)
    {
        AutotileNeighbours result = AutotileNeighbours.None;
        Add(0, -1, AutotileNeighbours.North);
        Add(1, 0, AutotileNeighbours.East);
        Add(0, 1, AutotileNeighbours.South);
        Add(-1, 0, AutotileNeighbours.West);
        Add(1, -1, AutotileNeighbours.NorthEast);
        Add(1, 1, AutotileNeighbours.SouthEast);
        Add(-1, 1, AutotileNeighbours.SouthWest);
        Add(-1, -1, AutotileNeighbours.NorthWest);
        return result;

        void Add(int offsetX, int offsetY, AutotileNeighbours flag)
        {
            int candidateX = x + offsetX;
            int candidateY = y + offsetY;
            if (!map.IsInside(candidateX, candidateY)) return;
            int candidate = map.Get(candidateX, candidateY, layer);
            if (MvTileRules.IsAutotile(candidate) && MvTileRules.GetAutotileKind(candidate) == kind) result |= flag;
        }
    }

    private static int ResolveFloor(AutotileNeighbours neighbours)
    {
        bool north = Has(AutotileNeighbours.North);
        bool east = Has(AutotileNeighbours.East);
        bool south = Has(AutotileNeighbours.South);
        bool west = Has(AutotileNeighbours.West);
        QuarterSignature signature = new(
            FloorTopLeft(north, west, Has(AutotileNeighbours.NorthWest)),
            FloorTopRight(north, east, Has(AutotileNeighbours.NorthEast)),
            FloorBottomLeft(south, west, Has(AutotileNeighbours.SouthWest)),
            FloorBottomRight(south, east, Has(AutotileNeighbours.SouthEast)));
        return FloorShapes.TryGetValue(signature, out int shape) ? shape : 46;
        bool Has(AutotileNeighbours flag) => (neighbours & flag) != 0;
    }

    private static int ResolveWall(AutotileNeighbours neighbours)
    {
        bool north = (neighbours & AutotileNeighbours.North) != 0;
        bool east = (neighbours & AutotileNeighbours.East) != 0;
        bool south = (neighbours & AutotileNeighbours.South) != 0;
        bool west = (neighbours & AutotileNeighbours.West) != 0;
        QuarterSignature signature = new(
            new QuarterCoordinate(west ? 2 : 0, north ? 2 : 0),
            new QuarterCoordinate(east ? 1 : 3, north ? 2 : 0),
            new QuarterCoordinate(west ? 2 : 0, south ? 1 : 3),
            new QuarterCoordinate(east ? 1 : 3, south ? 1 : 3));
        return WallShapes.TryGetValue(signature, out int shape) ? shape : 15;
    }

    private static int ResolveWaterfall(AutotileNeighbours neighbours)
    {
        bool east = (neighbours & AutotileNeighbours.East) != 0;
        bool west = (neighbours & AutotileNeighbours.West) != 0;
        QuarterSignature signature = new(
            new QuarterCoordinate(west ? 2 : 0, 0),
            new QuarterCoordinate(east ? 1 : 3, 0),
            new QuarterCoordinate(west ? 2 : 0, 1),
            new QuarterCoordinate(east ? 1 : 3, 1));
        return WaterfallShapes.TryGetValue(signature, out int shape) ? shape : 3;
    }

    private static QuarterCoordinate FloorTopLeft(bool north, bool west, bool diagonal) =>
        north && west ? (diagonal ? new(2, 4) : new(2, 0)) :
        north ? new(0, 4) :
        west ? new(2, 2) :
        new(0, 2);

    private static QuarterCoordinate FloorTopRight(bool north, bool east, bool diagonal) =>
        north && east ? (diagonal ? new(1, 4) : new(3, 0)) :
        north ? new(3, 4) :
        east ? new(1, 2) :
        new(3, 2);

    private static QuarterCoordinate FloorBottomLeft(bool south, bool west, bool diagonal) =>
        south && west ? (diagonal ? new(2, 3) : new(2, 1)) :
        south ? new(0, 3) :
        west ? new(2, 5) :
        new(0, 5);

    private static QuarterCoordinate FloorBottomRight(bool south, bool east, bool diagonal) =>
        south && east ? (diagonal ? new(1, 3) : new(3, 1)) :
        south ? new(3, 3) :
        east ? new(1, 5) :
        new(3, 5);

    private static Dictionary<QuarterSignature, int> BuildFloorShapes() => Build(MvTileRules.FloorAutotileTable);
    private static Dictionary<QuarterSignature, int> BuildWallShapes() => Build(MvTileRules.WallAutotileTable);
    private static Dictionary<QuarterSignature, int> BuildWaterfallShapes() => Build(MvTileRules.WaterfallAutotileTable);

    private static Dictionary<QuarterSignature, int> Build(IReadOnlyList<QuarterCoordinate[]> table)
    {
        Dictionary<QuarterSignature, int> result = [];
        for (int shape = 0; shape < table.Count; shape++)
        {
            QuarterCoordinate[] row = table[shape];
            result[new QuarterSignature(row[0], row[1], row[2], row[3])] = shape;
        }
        return result;
    }

    private static IEnumerable<MapPoint> Neighbourhood(MapPoint centre, IReadOnlyMapBuffer map)
    {
        for (int y = centre.Y - 1; y <= centre.Y + 1; y++)
        {
            for (int x = centre.X - 1; x <= centre.X + 1; x++)
            {
                if (map.IsInside(x, y)) yield return new MapPoint(x, y);
            }
        }
    }

    private readonly record struct QuarterSignature(
        QuarterCoordinate TopLeft,
        QuarterCoordinate TopRight,
        QuarterCoordinate BottomLeft,
        QuarterCoordinate BottomRight);

    private sealed class EffectiveMapBuffer : IReadOnlyMapBuffer
    {
        private readonly EditableMapBuffer _map;
        public EffectiveMapBuffer(EditableMapBuffer map) => _map = map;
        public int Width => _map.Width;
        public int Height => _map.Height;
        public int LayerCount => _map.LayerCount;
        public int Get(int x, int y, int layer) => _map.GetEffective(x, y, layer);
        public bool IsInside(int x, int y) => _map.IsInside(x, y);
    }
}
