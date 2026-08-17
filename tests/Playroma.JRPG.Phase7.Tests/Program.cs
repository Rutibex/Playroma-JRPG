using System.Text;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Mapping;
using Playroma.JRPG.Rendering;
using Playroma.JRPG.RpgMakerMV;

List<(string Name, Action Test)> tests =
[
    ("Map data patches compress adjacent values and reverse exactly", PatchCompression),
    ("Editable preview never mutates working data before commit", PreviewIsolation),
    ("Pencil line, rectangle and ellipse algorithms are deterministic", DrawingAlgorithms),
    ("Flood fill respects connected boundaries", FloodFill),
    ("Autotile topology resolves isolated and fully connected shapes", AutotileShapes),
    ("Autotile paint reshapes neighbours", AutotilePainting),
    ("Map selection capture, paste, mirror and rotation preserve values", SelectionTransforms),
    ("Clipboard round-trip preserves all six layers", ClipboardRoundTrip),
    ("Map sidecar metadata normalizes favorites and tags", MapMetadata),
    ("Pattern and random value brushes remain deterministic", ValueBrushes),
    ("Resize planner preserves anchored data and handles cropped events", ResizePlanner),
    ("Event page condition preview evaluates every standard condition", EventConditions),
    ("Batch lossless array mutation changes only requested values", BatchLosslessMutation),
    ("Seeded edit fuzzer reverses to exact original buffer", EditFuzzer)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + exception);
    }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} Phase 7 tests passed.");
return failures == 0 ? 0 : 1;

static void PatchCompression()
{
    MapDataPatch patch = MapDataPatch.FromChanges(
    [
        new MapDataChange(2, 0, 10),
        new MapDataChange(3, 0, 11),
        new MapDataChange(4, 0, 12),
        new MapDataChange(9, 0, 99)
    ], 5, 2);
    Equal(2, patch.Runs.Count);
    Equal(4, patch.ChangedValueCount);
    int[] values = new int[12];
    patch.Apply(values);
    Equal(10, values[2]);
    Equal(99, values[9]);
    patch.Reverse().Apply(values);
    True(values.All(value => value == 0));
}

static void PreviewIsolation()
{
    EditableMapBuffer map = new(3, 2, 6, new int[36]);
    map.BeginPreview();
    map.PreviewSet(1, 1, 0, 25);
    Equal(0, map.Get(1, 1, 0));
    Equal(25, map.GetEffective(1, 1, 0));
    MapDataPatch patch = map.CommitPreview();
    Equal(25, map.Get(1, 1, 0));
    map.ApplyPatch(patch.Reverse());
    Equal(0, map.Get(1, 1, 0));
}

static void DrawingAlgorithms()
{
    Equal(6, MapDrawingAlgorithms.Line(new MapPoint(0, 0), new MapPoint(5, 3)).Count);
    Equal(12, MapDrawingAlgorithms.Rectangle(new MapPoint(0, 0), new MapPoint(3, 2), true).Count);
    Equal(10, MapDrawingAlgorithms.Rectangle(new MapPoint(0, 0), new MapPoint(3, 2), false).Count);
    IReadOnlyList<MapPoint> first = MapDrawingAlgorithms.Ellipse(new MapPoint(0, 0), new MapPoint(8, 6), false);
    IReadOnlyList<MapPoint> second = MapDrawingAlgorithms.Ellipse(new MapPoint(0, 0), new MapPoint(8, 6), false);
    True(first.SequenceEqual(second));
    True(first.Count > 0);
}

static void FloodFill()
{
    int[] values = new int[5 * 4 * 6];
    EditableMapBuffer map = new(5, 4, 6, values);
    map.BeginPreview();
    for (int y = 0; y < 4; y++) map.PreviewSet(2, y, 0, 1);
    map.CommitPreview();
    IReadOnlyList<MapPoint> left = MapDrawingAlgorithms.FloodFill(map, new MapPoint(0, 0), 0, value => value == 0);
    Equal(8, left.Count);
    True(left.All(point => point.X < 2));
}

static void AutotileShapes()
{
    AutotileTopologyResolver resolver = new();
    int floor = MvTileRules.TileIdA2;
    Equal(46, resolver.ResolveShape(AutotileNeighbours.None, floor));
    Equal(0, resolver.ResolveShape(
        AutotileNeighbours.North | AutotileNeighbours.East | AutotileNeighbours.South | AutotileNeighbours.West
        | AutotileNeighbours.NorthEast | AutotileNeighbours.SouthEast | AutotileNeighbours.SouthWest | AutotileNeighbours.NorthWest,
        floor));
    Equal(15, resolver.ResolveShape(AutotileNeighbours.None, MvTileRules.TileIdA3));
    Equal(3, resolver.ResolveShape(AutotileNeighbours.None, MvTileRules.MakeAutotileId(5, 0)));

    HashSet<int> naturalFloorShapes = [];
    for (int raw = 0; raw < 256; raw++)
    {
        AutotileNeighbours neighbours = (AutotileNeighbours)raw;
        bool invalidDiagonal =
            Has(AutotileNeighbours.NorthEast) && !(Has(AutotileNeighbours.North) && Has(AutotileNeighbours.East))
            || Has(AutotileNeighbours.SouthEast) && !(Has(AutotileNeighbours.South) && Has(AutotileNeighbours.East))
            || Has(AutotileNeighbours.SouthWest) && !(Has(AutotileNeighbours.South) && Has(AutotileNeighbours.West))
            || Has(AutotileNeighbours.NorthWest) && !(Has(AutotileNeighbours.North) && Has(AutotileNeighbours.West));
        if (!invalidDiagonal) naturalFloorShapes.Add(resolver.ResolveShape(neighbours, floor));

        bool Has(AutotileNeighbours flag) => (neighbours & flag) != 0;
    }
    Equal(47, naturalFloorShapes.Count);
    True(Enumerable.Range(0, 47).All(naturalFloorShapes.Contains));
    False(naturalFloorShapes.Contains(47));
}

static void AutotilePainting()
{
    EditableMapBuffer map = new(3, 1, 6, new int[18]);
    MapBrushEngine brush = new();
    int sample = MvTileRules.TileIdA2;
    map.BeginPreview();
    brush.PreviewCells(map, [new MapPoint(0, 0), new MapPoint(1, 0), new MapPoint(2, 0)], 0, sample);
    map.CommitPreview();
    int left = map.Get(0, 0, 0);
    int middle = map.Get(1, 0, 0);
    int right = map.Get(2, 0, 0);
    True(MvTileRules.IsTileA2(left));
    True(MvTileRules.GetAutotileShape(left) != MvTileRules.GetAutotileShape(middle));
    True(MvTileRules.GetAutotileShape(right) == MvTileRules.GetAutotileShape(left));
}

static void SelectionTransforms()
{
    int[] values = new int[3 * 2 * 6];
    for (int y = 0; y < 2; y++)
    {
        for (int x = 0; x < 3; x++) values[y * 3 + x] = y * 10 + x + 1;
    }
    EditableMapBuffer map = new(3, 2, 6, values);
    MapSelectionData selection = MapSelectionData.Capture(map, new MapRect(0, 0, 3, 2), [0]);
    MapSelectionData mirrored = selection.MirrorHorizontal();
    Equal(3, mirrored.Layers[0][0]);
    Equal(1, mirrored.Layers[0][2]);
    MapSelectionData rotated = selection.RotateClockwise();
    Equal(2, rotated.Bounds.Width);
    Equal(3, rotated.Bounds.Height);
    EditableMapBuffer target = new(5, 5, 6, new int[150]);
    target.BeginPreview();
    rotated.PreviewPaste(target, new MapPoint(1, 1));
    target.CommitPreview();
    Equal(11, target.Get(1, 1, 0));
}

static void ClipboardRoundTrip()
{
    Dictionary<int, int[]> layers = [];
    for (int layer = 0; layer < 6; layer++) layers[layer] = Enumerable.Range(layer * 4, 4).ToArray();
    MapClipboardPayload payload = new()
    {
        Format = MapClipboardPayload.CurrentFormat,
        SourceProjectId = Guid.NewGuid(),
        SourceMapId = 7,
        SourceTilesetId = 2,
        TilesetFingerprint = "fixture",
        Width = 2,
        Height = 2,
        AnchorX = 0,
        AnchorY = 0,
        Layers = layers,
        Events = [new MapClipboardEvent(1, 0, 0, "{\"id\":1}")]
    };
    MapClipboardPayload decoded = MapClipboardPayload.Deserialize(payload.Serialize());
    Equal(6, decoded.Layers.Count);
    True(decoded.Layers[5].SequenceEqual(layers[5]));
    Equal(1, decoded.Events.Count);
}

static void MapMetadata()
{
    MapEditorMetadataDocument metadata = new();
    metadata = metadata.Set(7, new MapEditorMetadataEntry { Favorite = true, Tags = ["Town", " town ", "Quest"] });
    MapEditorMetadataDocument decoded = MapEditorMetadataDocument.Deserialize(metadata.Serialize());
    True(decoded.Get(7).Favorite);
    Equal(2, decoded.Get(7).Tags.Length);
    metadata = decoded.Set(7, new MapEditorMetadataEntry());
    Equal(0, metadata.Maps.Count);
}

static void ValueBrushes()
{
    EditableMapBuffer first = new(4, 4, 6, new int[96]);
    EditableMapBuffer second = new(4, 4, 6, new int[96]);
    MapBrushEngine brush = new();
    (MapPoint Point, int Value)[] values = new MapRect(0, 0, 4, 4).Cells()
        .Select(point => (point, 100 + ((point.X * 31 + point.Y * 17 + 9) & 3)))
        .ToArray();
    first.BeginPreview();
    second.BeginPreview();
    brush.PreviewValues(first, values, 0, reshapeAutotiles: false);
    brush.PreviewValues(second, values, 0, reshapeAutotiles: false);
    first.CommitPreview();
    second.CommitPreview();
    True(first.CaptureValues().SequenceEqual(second.CaptureValues()));
}

static void ResizePlanner()
{
    int[] values = new int[4 * 3 * 6];
    for (int y = 0; y < 3; y++)
    {
        for (int x = 0; x < 4; x++) values[y * 4 + x] = y * 10 + x + 1;
    }
    EditableMapBuffer map = new(4, 3, 6, values);
    MapResizePlan plan = new MapResizePlanner().CreatePlan(
        map,
        3,
        2,
        MapResizeAnchor.BottomRight,
        [new MapEventPosition(1, 3, 2), new MapEventPosition(2, 0, 0)],
        deleteCroppedEvents: true);
    Equal(-1, plan.OffsetX);
    Equal(-1, plan.OffsetY);
    Equal(12, plan.NewData[0]);
    True(plan.Events.Single(item => item.Id == 1).After == new MapPoint(2, 1));
    True(plan.Events.Single(item => item.Id == 2).IsDeleted);
}

static void EventConditions()
{
    EventPageConditionSnapshot condition = new(true, 1, true, 2, true, 3, 10, true, "A", true, 5, true, 7);
    EventPreviewContext context = new(
        new Dictionary<int, bool> { [1] = true, [2] = true },
        new Dictionary<int, int> { [3] = 10 },
        new Dictionary<(int, int, string), bool> { [(4, 9, "A")] = true },
        new HashSet<int> { 7 },
        new Dictionary<int, int> { [5] = 1 });
    True(EventPageConditionEvaluator.IsActive(condition, context, 4, 9));
    False(EventPageConditionEvaluator.IsActive(condition, context with { Inventory = new Dictionary<int, int>() }, 4, 9));
}

static void BatchLosslessMutation()
{
    byte[] bytes = Encoding.UTF8.GetBytes("{\n  \"data\": [0, 1, 2, 3],\n  \"pluginField\": {\"keep\":true}\n}\n");
    string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    MutableJsonProjectDocument document = MutableJsonProjectDocument.Create(
        "data/Map001.json",
        MvDocumentKind.Map,
        bytes,
        new ContentFingerprint(bytes.Length, DateTimeOffset.UtcNow, hash));
    document.SetInt32ArrayValues(
        JsonPath.Root.Append("data"),
        [new IndexedInt32Value(1, 10, 1), new IndexedInt32Value(3, 30, 3)],
        "fixture");
    string generated = Encoding.UTF8.GetString(document.CaptureGeneratedBytes());
    True(generated.Contains("\"pluginField\": {\"keep\":true}", StringComparison.Ordinal));
    True(generated.Contains("[0, 10, 2, 30]", StringComparison.Ordinal));
    document.Dispose();
}

static void EditFuzzer()
{
    const int width = 32;
    const int height = 24;
    int[] original = new int[width * height * 6];
    EditableMapBuffer map = new(width, height, 6, original);
    Random random = new(731947);
    List<MapDataPatch> history = [];
    for (int operation = 0; operation < 1_000; operation++)
    {
        int x = random.Next(width);
        int y = random.Next(height);
        int layer = random.Next(6);
        int value = layer switch { 4 => random.Next(16), 5 => random.Next(256), _ => random.Next(MvTileRules.TileIdMax) };
        map.BeginPreview();
        map.PreviewSet(x, y, layer, value);
        if (!map.HasPreview) continue;
        history.Add(map.CommitPreview());
    }
    for (int index = history.Count - 1; index >= 0; index--) map.ApplyPatch(history[index].Reverse());
    True(map.CaptureValues().SequenceEqual(original));
    foreach (MapDataPatch patch in history) map.ApplyPatch(patch);
    True(map.IsModified);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true.");
}

static void False(bool value)
{
    if (value) throw new InvalidOperationException("Expected false.");
}
