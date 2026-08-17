using System.Drawing.Imaging;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Rendering;
using Playroma.JRPG.Rendering.Windows;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("SKIP Phase 6 Windows map-rendering tests require Windows.");
    return 0;
}

List<(string Name, Func<Task> Test)> tests =
[
    ("RGBA bitmap conversion preserves channels", BitmapConversionAsync),
    ("Tileset asset load releases source handle", TilesetHandleAsync),
    ("Character and parallax assets are owned", CharacterAndParallaxAsync)
];
int failures = 0;
foreach ((string name, Func<Task> test) in tests)
{
    try
    {
        await test().ConfigureAwait(false);
        Console.WriteLine("PASS " + name);
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + exception);
    }
}
return failures == 0 ? 0 : 1;

static Task BitmapConversionAsync()
{
    RgbaImage source = new(2, 1);
    source.SetPixel(0, 0, new Rgba32(10, 20, 30, 255));
    source.SetPixel(1, 0, new Rgba32(200, 180, 160, 255));
    using Bitmap bitmap = BitmapPixelConverter.ToBitmap(source);
    RgbaImage restored = BitmapPixelConverter.ToRgbaImage(bitmap);
    Equal(source.GetPixel(0, 0), restored.GetPixel(0, 0));
    Equal(source.GetPixel(1, 0), restored.GetPixel(1, 0));
    return Task.CompletedTask;
}

static async Task TilesetHandleAsync()
{
    using TemporaryDirectory temp = new();
    string relative = "img/tilesets/Fixture_B.png";
    string full = WritePng(temp.Path, relative, 768, 768, Color.Crimson);
    PhysicalAssetContentSource content = new(temp.Path);
    string[] names = new string[9];
    names[(int)TileSheetKind.B] = "Fixture_B";
    TilesetRenderSnapshot tileset = new(1, "Fixture", 1, "data/Tilesets.json", names, new int[MvTileRules.TileIdMax]);
    MapRenderSnapshot map = new(1, "Fixture", "data/Map001.json", 1, 1, 1, 0, new int[6], [], ParallaxRenderSnapshot.Empty);
    MapRenderAssetSet assets = await new WindowsRenderAssetLoader(content, string.Empty).LoadAsync(map, tileset).ConfigureAwait(false);
    True(assets.Tilesets.ContainsKey(TileSheetKind.B));
    string renamed = Path.Combine(Path.GetDirectoryName(full)!, "Renamed.png");
    File.Move(full, renamed);
    True(File.Exists(renamed));
}

static async Task CharacterAndParallaxAsync()
{
    using TemporaryDirectory temp = new();
    string character = WritePng(temp.Path, "img/characters/!$Door.png", 144, 192, Color.LimeGreen);
    string parallax = WritePng(temp.Path, "img/parallaxes/Clouds.png", 96, 96, Color.SkyBlue);
    PhysicalAssetContentSource content = new(temp.Path);
    MapEventRenderSnapshot mapEvent = new(1, "Door", 0, 0, 1, 0, new EventGraphicSnapshot(0, "!$Door", 0, 2, 1));
    MapRenderSnapshot map = new(1, "Fixture", "data/Map001.json", 1, 1, 1, 0, new int[6], [mapEvent], new ParallaxRenderSnapshot("Clouds", true, true, 0, 0, true, false));
    TilesetRenderSnapshot tileset = new(1, "Fixture", 1, "data/Tilesets.json", new string[9], new int[MvTileRules.TileIdMax]);
    MapRenderAssetSet assets = await new WindowsRenderAssetLoader(content, string.Empty).LoadAsync(map, tileset).ConfigureAwait(false);
    True(assets.Characters.ContainsKey("!$Door"));
    True(assets.Parallaxes.ContainsKey("Clouds"));
    File.Delete(character);
    File.Delete(parallax);
    True(!File.Exists(character) && !File.Exists(parallax));
}

static string WritePng(string root, string relativePath, int width, int height, Color colour)
{
    string full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    using Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
    using Graphics graphics = Graphics.FromImage(bitmap);
    graphics.Clear(colour);
    bitmap.Save(full, ImageFormat.Png);
    return full;
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

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PlayromaJRPG-Phase6-Windows-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}
