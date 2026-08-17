using System.Text;
using Playroma.JRPG.Core;
using Playroma.JRPG.Rendering;

List<(string Name, Action Test)> tests =
[
    ("MV tile constants and classification", TileConstants),
    ("All floor autotile shapes decode", FloorAutotileShapes),
    ("Wall and waterfall shapes decode", WallAndWaterfallShapes),
    ("Normal tile source rectangles", NormalTileSourceRectangles),
    ("Invalid family shape is diagnosed", InvalidFamilyShape),
    ("Map snapshot preserves and diagnoses unusual shadow bits", SnapshotShadowBits),
    ("Looping map reads wrap coordinates", LoopingReads),
    ("Non-looping map reads safely reject negative neighbours", NonLoopingOutOfBoundsReads),
    ("Lower layers remain below characters even when flag data contains star", LowerLayerIgnoresStar),
    ("Render pass places upper tiles above same-priority events", UpperTileLayering),
    ("Object character sheet frame renders at tile origin", ObjectCharacterRendering),
    ("Missing assets render deterministic placeholder", MissingAssetPlaceholder),
    ("Minimap builder renders bounded overview by chunks", MinimapBuilder),
    ("PNG output is deterministic", DeterministicPng),
    ("Visual comparison creates a useful diff", VisualComparison)
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
Console.WriteLine($"{tests.Count - failures}/{tests.Count} Phase 6 tests passed.");
return failures == 0 ? 0 : 1;

static void TileConstants()
{
    Equal(0, MvTileRules.TileIdB);
    Equal(256, MvTileRules.TileIdC);
    Equal(512, MvTileRules.TileIdD);
    Equal(768, MvTileRules.TileIdE);
    Equal(1536, MvTileRules.TileIdA5);
    Equal(2048, MvTileRules.TileIdA1);
    Equal(2816, MvTileRules.TileIdA2);
    Equal(4352, MvTileRules.TileIdA3);
    Equal(5888, MvTileRules.TileIdA4);
    Equal(8192, MvTileRules.TileIdMax);
    True(MvTileRules.IsTileA1(2048));
    True(MvTileRules.IsTileA2(2816));
    True(MvTileRules.IsTileA3(4352));
    True(MvTileRules.IsTileA4(5888));
    True(MvTileRules.IsTileA5(1536));
    True(MvTileRules.IsWaterfallTypeAutotile(MvTileRules.MakeAutotileId(5, 0)));
}

static void FloorAutotileShapes()
{
    MvTileInstructionDecoder decoder = new();
    for (int shape = 0; shape < 48; shape++)
    {
        List<TileRenderInstruction> output = [];
        int sequence = 0;
        decoder.AppendTile(MvTileRules.TileIdA2 + shape, 0, 0, RenderPass.LowerTiles, TileLayer.Lower0, 0, false, output, ref sequence);
        Equal(4, output.Count);
        True(output.All(item => item.Image.Sheet == TileSheetKind.A2));
        True(output.All(item => item.Source.Width == 24 && item.Source.Height == 24));
    }
}

static void WallAndWaterfallShapes()
{
    MvTileInstructionDecoder decoder = new();
    for (int shape = 0; shape < 16; shape++)
    {
        List<TileRenderInstruction> output = [];
        int sequence = 0;
        decoder.AppendTile(MvTileRules.TileIdA3 + shape, 0, 0, RenderPass.LowerTiles, TileLayer.Lower0, 0, false, output, ref sequence);
        Equal(4, output.Count);
        True(output.All(item => item.Image.Sheet == TileSheetKind.A3));
    }
    for (int shape = 0; shape < 4; shape++)
    {
        List<TileRenderInstruction> output = [];
        int sequence = 0;
        int waterfall = MvTileRules.MakeAutotileId(5, shape);
        decoder.AppendTile(waterfall, 0, 0, RenderPass.LowerTiles, TileLayer.Lower0, 2, false, output, ref sequence);
        Equal(4, output.Count);
        True(output.All(item => item.Image.Sheet == TileSheetKind.A1));
    }
}

static void NormalTileSourceRectangles()
{
    MvTileInstructionDecoder decoder = new();
    List<TileRenderInstruction> output = [];
    int sequence = 0;
    decoder.AppendTile(1, 0, 0, RenderPass.LowerTiles, TileLayer.Lower0, 0, false, output, ref sequence);
    decoder.AppendTile(128, 48, 0, RenderPass.LowerTiles, TileLayer.Lower0, 0, false, output, ref sequence);
    decoder.AppendTile(MvTileRules.TileIdA5, 96, 0, RenderPass.LowerTiles, TileLayer.Lower0, 0, false, output, ref sequence);
    Equal(TileSheetKind.B, output[0].Image.Sheet!.Value);
    Equal(new PixelRect(48, 0, 48, 48), output[0].Source);
    Equal(new PixelRect(384, 0, 48, 48), output[1].Source);
    Equal(TileSheetKind.A5, output[2].Image.Sheet!.Value);
    Equal(new PixelRect(0, 0, 48, 48), output[2].Source);
}

static void InvalidFamilyShape()
{
    MvTileInstructionDecoder decoder = new();
    List<TileRenderInstruction> output = [];
    List<ProjectDiagnostic> diagnostics = [];
    int sequence = 0;
    decoder.AppendTile(MvTileRules.TileIdA3 + 16, 0, 0, RenderPass.LowerTiles, TileLayer.Lower0, 0, false, output, ref sequence, diagnostics);
    Equal(0, output.Count);
    True(diagnostics.Any(item => item.Code == DiagnosticCodes.Phase6InvalidAutotileShape));
}

static void SnapshotShadowBits()
{
    string json = """
        {
          "width": 2,
          "height": 1,
          "tilesetId": 1,
          "scrollType": 0,
          "data": [0,0, 0,0, 0,0, 0,0, 1073741829,0, 255,0],
          "events": [null],
          "parallaxName": ""
        }
        """;
    MapRenderSnapshot map = MvRenderSnapshotParser.ParseMap(1, "Fixture", "data/Map001.json", Encoding.UTF8.GetBytes(json));
    Equal(1073741829, map.ReadShadow(0, 0));
    Equal(5, map.ReadShadow(0, 0) & 0x0f);
    Equal(255, map.ReadRegion(0, 0));
    True(map.Diagnostics.Any(item => item.Code == DiagnosticCodes.Phase6ShadowHighBits));
}

static void LoopingReads()
{
    int[] data = new int[2 * 2 * 6];
    data[0] = 10;
    data[1] = 11;
    data[2] = 12;
    data[3] = 13;
    MapRenderSnapshot map = new(1, "Loop", "data/Map001.json", 2, 2, 1, 3, data, [], ParallaxRenderSnapshot.Empty);
    Equal(13, map.Read(-1, -1, 0));
    Equal(10, map.Read(2, 2, 0));
}

static void NonLoopingOutOfBoundsReads()
{
    int[] data = new int[2 * 2 * 6];
    data[0] = 10;
    MapRenderSnapshot map = new(1, "Bounded", "data/Map001.json", 2, 2, 1, 0, data, [], ParallaxRenderSnapshot.Empty);
    Equal(0, map.Read(0, -1, 1));
    Equal(0, map.Read(-1, 0, 0));
    Equal(0, map.Read(0, 0, -1));
    Equal(0, map.Read(2, 0, 0));
    Equal(0, map.Read(0, 2, 0));
}

static void LowerLayerIgnoresStar()
{
    int[] data = new int[6];
    data[0] = 1;
    MapEventRenderSnapshot mapEvent = new(1, "Door", 0, 0, 1, 0, new EventGraphicSnapshot(0, "!$Door", 0, 2, 1));
    MapRenderSnapshot map = new(1, "Layer", "data/Map001.json", 1, 1, 1, 0, data, [mapEvent], ParallaxRenderSnapshot.Empty);
    int[] flags = new int[MvTileRules.TileIdMax];
    flags[1] = 0x10;
    TilesetRenderSnapshot tileset = new(1, "Fixture", 1, "data/Tilesets.json", new string[9], flags);
    RgbaImage sheet = new(768, 768);
    sheet.Fill(new PixelRect(48, 0, 48, 48), new Rgba32(220, 20, 20, 255), blend: false);
    RgbaImage character = CreateSingleCharacterSheet(new Rgba32(20, 220, 20, 255));
    MapRenderAssetSet assets = new(
        new Dictionary<TileSheetKind, RgbaImage> { [TileSheetKind.B] = sheet },
        new Dictionary<string, RgbaImage> { ["!$Door"] = character });
    MapRenderResult result = new MapPixelRenderer().RenderWholeMap(map, tileset, assets);
    Rgba32 centre = result.Image.GetPixel(24, 24);
    True(centre.G > 180 && centre.R < 80);
}

static void UpperTileLayering()
{
    int[] data = new int[6];
    data[2] = 1;
    MapEventRenderSnapshot mapEvent = new(1, "Door", 0, 0, 1, 0, new EventGraphicSnapshot(0, "!$Door", 0, 2, 1));
    MapRenderSnapshot map = new(1, "Layer", "data/Map001.json", 1, 1, 1, 0, data, [mapEvent], ParallaxRenderSnapshot.Empty);
    int[] flags = new int[MvTileRules.TileIdMax];
    flags[1] = 0x10;
    TilesetRenderSnapshot tileset = new(1, "Fixture", 1, "data/Tilesets.json", new string[9], flags);
    RgbaImage sheet = new(768, 768);
    sheet.Fill(new PixelRect(48, 0, 48, 48), new Rgba32(220, 20, 20, 255), blend: false);
    RgbaImage character = CreateSingleCharacterSheet(new Rgba32(20, 220, 20, 255));
    MapRenderAssetSet assets = new(
        new Dictionary<TileSheetKind, RgbaImage> { [TileSheetKind.B] = sheet },
        new Dictionary<string, RgbaImage> { ["!$Door"] = character });
    MapRenderResult result = new MapPixelRenderer().RenderWholeMap(map, tileset, assets);
    Rgba32 centre = result.Image.GetPixel(24, 24);
    True(centre.R > 180 && centre.G < 80);
}

static void ObjectCharacterRendering()
{
    int[] data = new int[6];
    MapEventRenderSnapshot mapEvent = new(1, "Door", 0, 0, 1, 0, new EventGraphicSnapshot(0, "!$Door", 0, 2, 1));
    MapRenderSnapshot map = new(1, "Character", "data/Map001.json", 1, 1, 1, 0, data, [mapEvent], ParallaxRenderSnapshot.Empty);
    TilesetRenderSnapshot tileset = new(1, "Fixture", 1, "data/Tilesets.json", new string[9], new int[MvTileRules.TileIdMax]);
    RgbaImage character = CreateSingleCharacterSheet(new Rgba32(20, 220, 20, 255));
    MapRenderAssetSet assets = new(characters: new Dictionary<string, RgbaImage> { ["!$Door"] = character });
    MapRenderResult result = new MapPixelRenderer().RenderWholeMap(map, tileset, assets);
    Rgba32 centre = result.Image.GetPixel(24, 24);
    True(centre.G > 180 && centre.R < 80);
}

static void MissingAssetPlaceholder()
{
    int[] data = new int[6];
    data[0] = 1;
    MapRenderSnapshot map = new(1, "Missing", "data/Map001.json", 1, 1, 1, 0, data, [], ParallaxRenderSnapshot.Empty);
    TilesetRenderSnapshot tileset = new(1, "Fixture", 1, "data/Tilesets.json", new string[9], new int[MvTileRules.TileIdMax]);
    MapRenderResult result = new MapPixelRenderer().RenderWholeMap(map, tileset, new MapRenderAssetSet());
    True(result.Diagnostics.Any(item => item.Code == DiagnosticCodes.Phase6RenderAssetMissing));
    Equal(new Rgba32(255, 0, 255, 255), result.Image.GetPixel(0, 0));
}

static void MinimapBuilder()
{
    int[] data = new int[2 * 6];
    data[0] = 1;
    data[1] = 2;
    MapRenderSnapshot map = new(1, "Mini", "data/Map001.json", 2, 1, 1, 0, data, [], ParallaxRenderSnapshot.Empty);
    TilesetRenderSnapshot tileset = new(1, "Fixture", 1, "data/Tilesets.json", new string[9], new int[MvTileRules.TileIdMax]);
    RgbaImage sheet = new(768, 768);
    sheet.Fill(new PixelRect(48, 0, 48, 48), new Rgba32(220, 20, 20, 255), blend: false);
    sheet.Fill(new PixelRect(96, 0, 48, 48), new Rgba32(20, 220, 20, 255), blend: false);
    MapRenderAssetSet assets = new(new Dictionary<TileSheetKind, RgbaImage> { [TileSheetKind.B] = sheet });
    MapMinimapResult result = new MapMinimapBuilder().Build(map, tileset, assets, 2, 1);
    Equal(2, result.Image.Width);
    Equal(1, result.Image.Height);
    True(result.Image.GetPixel(0, 0).R > 180);
    True(result.Image.GetPixel(1, 0).G > 180);
    Equal(1, result.RenderedChunkCount);
}

static void DeterministicPng()
{
    RgbaImage image = new(3, 2);
    image.SetPixel(0, 0, new Rgba32(1, 2, 3, 4));
    image.SetPixel(2, 1, new Rgba32(250, 240, 230, 220));
    byte[] first = image.ToPng();
    byte[] second = image.ToPng();
    True(first.SequenceEqual(second));
    True(first.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
}

static void VisualComparison()
{
    RgbaImage expected = new(2, 1);
    RgbaImage actual = new(2, 1);
    expected.Clear(new Rgba32(10, 10, 10, 255));
    actual.Clear(new Rgba32(10, 10, 10, 255));
    actual.SetPixel(1, 0, new Rgba32(20, 10, 10, 255));
    ImageComparisonResult result = RgbaImageComparer.Compare(expected, actual);
    True(!result.IsMatch);
    Equal(1, result.ChangedPixels);
    Equal(10, result.MaximumChannelDifference);
    True(result.DifferenceImage.GetPixel(1, 0).R > result.DifferenceImage.GetPixel(0, 0).R);
}

static RgbaImage CreateSingleCharacterSheet(Rgba32 frameColour)
{
    RgbaImage image = new(144, 192);
    image.Fill(new PixelRect(48, 0, 48, 48), frameColour, blend: false);
    return image;
}

static void True(bool condition)
{
    if (!condition) throw new InvalidOperationException("Assertion failed.");
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}; received {actual}.");
}
