namespace Playroma.JRPG.Mapping;

public readonly record struct MapDataChange(int Index, int Before, int After);

public sealed record MapDataRun(int StartIndex, int[] Before, int[] After)
{
    public int Length => After.Length;

    public MapDataRun Validate()
    {
        if (StartIndex < 0) throw new ArgumentOutOfRangeException(nameof(StartIndex));
        ArgumentNullException.ThrowIfNull(Before);
        ArgumentNullException.ThrowIfNull(After);
        if (Before.Length == 0 || Before.Length != After.Length)
        {
            throw new ArgumentException("A map-data run requires equally sized non-empty before and after arrays.");
        }
        return this;
    }
}

public sealed class MapDataPatch
{
    private readonly MapDataRun[] _runs;

    public MapDataPatch(IEnumerable<MapDataRun> runs, MapDirtyBounds dirtyBounds = default)
    {
        ArgumentNullException.ThrowIfNull(runs);
        _runs = runs.Select(run => run.Validate()).OrderBy(run => run.StartIndex).ToArray();
        if (_runs.Length == 0) throw new ArgumentException("A map-data patch requires at least one run.", nameof(runs));
        int previousEnd = -1;
        foreach (MapDataRun run in _runs)
        {
            if (run.StartIndex < previousEnd) throw new ArgumentException("Map-data runs cannot overlap.", nameof(runs));
            previousEnd = checked(run.StartIndex + run.Length);
        }
        DirtyBounds = dirtyBounds;
    }

    public IReadOnlyList<MapDataRun> Runs => _runs;
    public MapDirtyBounds DirtyBounds { get; }
    public int ChangedValueCount => _runs.Sum(run => run.Length);

    public static MapDataPatch FromChanges(
        IEnumerable<MapDataChange> changes,
        int width = 0,
        int height = 0)
    {
        ArgumentNullException.ThrowIfNull(changes);
        MapDataChange[] ordered = changes
            .Where(change => change.Before != change.After)
            .GroupBy(change => change.Index)
            .Select(group => group.Last())
            .OrderBy(change => change.Index)
            .ToArray();
        if (ordered.Length == 0) throw new ArgumentException("No effective map-data changes were supplied.", nameof(changes));

        List<MapDataRun> runs = [];
        int start = ordered[0].Index;
        List<int> before = [ordered[0].Before];
        List<int> after = [ordered[0].After];
        int previous = start;
        for (int index = 1; index < ordered.Length; index++)
        {
            MapDataChange change = ordered[index];
            if (change.Index == previous + 1)
            {
                before.Add(change.Before);
                after.Add(change.After);
            }
            else
            {
                runs.Add(new MapDataRun(start, before.ToArray(), after.ToArray()));
                start = change.Index;
                before = [change.Before];
                after = [change.After];
            }
            previous = change.Index;
        }
        runs.Add(new MapDataRun(start, before.ToArray(), after.ToArray()));

        MapDirtyBounds bounds = CalculateBounds(ordered, width, height);
        return new MapDataPatch(runs, bounds);
    }

    public MapDataPatch Reverse() => new(
        _runs.Select(run => new MapDataRun(run.StartIndex, run.After.ToArray(), run.Before.ToArray())),
        DirtyBounds);

    public void Apply(Span<int> destination, bool verifyBefore = true)
    {
        foreach (MapDataRun run in _runs)
        {
            if (run.StartIndex > destination.Length - run.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "A map-data patch extends past the destination buffer.");
            }
            for (int offset = 0; offset < run.Length; offset++)
            {
                int index = run.StartIndex + offset;
                if (verifyBefore && destination[index] != run.Before[offset])
                {
                    throw new InvalidOperationException($"Map data changed unexpectedly at index {index}.");
                }
                destination[index] = run.After[offset];
            }
        }
    }

    public IEnumerable<MapDataChange> Expand()
    {
        foreach (MapDataRun run in _runs)
        {
            for (int offset = 0; offset < run.Length; offset++)
            {
                yield return new MapDataChange(run.StartIndex + offset, run.Before[offset], run.After[offset]);
            }
        }
    }

    private static MapDirtyBounds CalculateBounds(IReadOnlyList<MapDataChange> changes, int width, int height)
    {
        if (width <= 0 || height <= 0) return default;
        int cellsPerLayer = checked(width * height);
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;
        foreach (MapDataChange change in changes)
        {
            int cellIndex = change.Index % cellsPerLayer;
            int x = cellIndex % width;
            int y = cellIndex / width;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x + 1);
            bottom = Math.Max(bottom, y + 1);
        }
        return left == int.MaxValue ? default : new MapDirtyBounds(left, top, right, bottom);
    }
}
