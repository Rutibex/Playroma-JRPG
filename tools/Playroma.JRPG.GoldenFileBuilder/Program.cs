using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.Rendering;
using Playroma.JRPG.Rendering.Windows;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The Phase 6 golden-file tool requires Windows because it decodes MV PNG assets through System.Drawing.");
    return 4;
}

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return args.Length == 0 ? 3 : 0;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "build" => await BuildAsync(args[1..]).ConfigureAwait(false),
        "probe" => await ProbeAsync(args[1..]).ConfigureAwait(false),
        "compare" => Compare(args[1..]),
        _ => InvalidCommand(args[0])
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operation cancelled.");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 2;
}

static async Task<int> BuildAsync(string[] arguments)
{
    if (arguments.Length < 2)
    {
        PrintUsage();
        return 3;
    }

    string projectPath = Path.GetFullPath(arguments[0]);
    string outputDirectory = Path.GetFullPath(arguments[1]);
    int frame = ReadIntOption(arguments, "--frame", 0, 0, 11);
    int chunkPixels = ReadIntOption(arguments, "--chunk-pixels", 1536, 48, 8192);
    long maximumWholePixels = ReadLongOption(arguments, "--max-whole-pixels", 64_000_000, 1, int.MaxValue);
    HashSet<int>? selectedMaps = ReadMapSelection(arguments);
    Directory.CreateDirectory(outputDirectory);

    OperationResult<MvProjectSession> opened = await MvProjectSession.OpenAsync(
        projectPath,
        analysisLoadMode: ProjectAnalysisLoadMode.Deferred).ConfigureAwait(false);
    if (opened.Value is null)
    {
        WriteDiagnostics(opened.Diagnostics);
        return 2;
    }

    await using MvProjectSession session = opened.Value;
    MvProjectMapRenderLoader loader = new();
    MapPixelRenderer renderer = new();
    List<GoldenMapManifest> maps = [];
    IEnumerable<Playroma.JRPG.RpgMakerMV.MvMapDescriptor> descriptors = session.Workspace.Snapshot.Maps.OrderBy(item => item.Id);
    if (selectedMaps is not null) descriptors = descriptors.Where(item => selectedMaps.Contains(item.Id));

    foreach (Playroma.JRPG.RpgMakerMV.MvMapDescriptor descriptor in descriptors)
    {
        Console.Write($"Map {descriptor.Id:D3} {descriptor.Name} ... ");
        LoadedMapRenderContext context = await loader.LoadAsync(session, descriptor.Id).ConfigureAwait(false);
        long pixelCount = checked((long)context.Map.PixelSize.Width * context.Map.PixelSize.Height);
        List<GoldenOutputFile> files = [];
        if (pixelCount <= maximumWholePixels)
        {
            MapRenderResult result = renderer.RenderWholeMap(context.Map, context.Tileset, context.Assets, frame);
            string relative = $"Map{descriptor.Id:D3}.png";
            files.Add(WriteImage(outputDirectory, relative, result.Image, new PixelRect(0, 0, result.Image.Width, result.Image.Height)));
            Console.WriteLine("full image");
        }
        else
        {
            string mapFolder = $"Map{descriptor.Id:D3}";
            int columns = DivideRoundUp(context.Map.PixelSize.Width, chunkPixels);
            int rows = DivideRoundUp(context.Map.PixelSize.Height, chunkPixels);
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int x = column * chunkPixels;
                    int y = row * chunkPixels;
                    PixelRect region = new(
                        x,
                        y,
                        Math.Min(chunkPixels, context.Map.PixelSize.Width - x),
                        Math.Min(chunkPixels, context.Map.PixelSize.Height - y));
                    MapRenderResult result = renderer.RenderRegion(context.Map, context.Tileset, context.Assets, region, frame);
                    string relative = Path.Combine(mapFolder, $"x{column:D2}_y{row:D2}.png").Replace('\\', '/');
                    files.Add(WriteImage(outputDirectory, relative, result.Image, region));
                }
            }
            Console.WriteLine($"{files.Count} chunks");
        }

        ProjectDiagnostic[] diagnostics = context.Diagnostics
            .GroupBy(item => (item.Code, item.Message, item.Location.RelativePath, item.Location.JsonPath))
            .Select(group => group.First())
            .ToArray();
        maps.Add(new GoldenMapManifest(
            descriptor.Id,
            descriptor.Name,
            context.Map.Width,
            context.Map.Height,
            context.Map.TilesetId,
            context.Tileset.Name,
            pixelCount,
            files,
            diagnostics.Select(item => new GoldenDiagnostic(item.Code, item.Severity.ToString(), item.Message, item.Location.RelativePath, item.Location.JsonPath)).ToArray()));
    }

    GoldenManifest manifest = new(
        "playroma-jrpg-phase6-golden-v1",
        DateTimeOffset.UtcNow,
        projectPath,
        frame,
        chunkPixels,
        maximumWholePixels,
        maps);
    string manifestPath = Path.Combine(outputDirectory, "manifest.json");
    await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
    Console.WriteLine($"Wrote {maps.Count} map entries and {maps.Sum(item => item.Files.Count)} PNG files to {outputDirectory}");
    return 0;
}

static async Task<int> ProbeAsync(string[] arguments)
{
    if (arguments.Length < 1)
    {
        PrintUsage();
        return 3;
    }

    string projectPath = Path.GetFullPath(arguments[0]);
    int frame = ReadIntOption(arguments, "--frame", 0, 0, 11);
    int chunkPixels = ReadIntOption(arguments, "--chunk-pixels", 768, 48, 4096);
    HashSet<int>? selectedMaps = ReadMapSelection(arguments);
    OperationResult<MvProjectSession> opened = await MvProjectSession.OpenAsync(
        projectPath,
        analysisLoadMode: ProjectAnalysisLoadMode.Deferred).ConfigureAwait(false);
    if (opened.Value is null)
    {
        WriteDiagnostics(opened.Diagnostics);
        return 2;
    }

    await using MvProjectSession session = opened.Value;
    MvProjectMapRenderLoader loader = new();
    MapPixelRenderer renderer = new();
    int failures = 0;
    int renderedChunks = 0;
    IEnumerable<Playroma.JRPG.RpgMakerMV.MvMapDescriptor> descriptors = session.Workspace.Snapshot.Maps.OrderBy(item => item.Id);
    if (selectedMaps is not null) descriptors = descriptors.Where(item => selectedMaps.Contains(item.Id));
    foreach (Playroma.JRPG.RpgMakerMV.MvMapDescriptor descriptor in descriptors)
    {
        try
        {
            LoadedMapRenderContext context = await loader.LoadAsync(session, descriptor.Id).ConfigureAwait(false);
            int columns = DivideRoundUp(context.Map.PixelSize.Width, chunkPixels);
            int rows = DivideRoundUp(context.Map.PixelSize.Height, chunkPixels);
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int x = column * chunkPixels;
                    int y = row * chunkPixels;
                    PixelRect region = new(
                        x,
                        y,
                        Math.Min(chunkPixels, context.Map.PixelSize.Width - x),
                        Math.Min(chunkPixels, context.Map.PixelSize.Height - y));
                    _ = renderer.RenderRegion(context.Map, context.Tileset, context.Assets, region, frame);
                    renderedChunks++;
                }
            }
            int warnings = context.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Warning);
            int errors = context.Diagnostics.Count(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);
            Console.WriteLine($"PASS Map {descriptor.Id:D3} {descriptor.Name}: {columns * rows} chunks, {warnings} warnings, {errors} errors");
            if (errors > 0) failures++;
        }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine($"FAIL Map {descriptor.Id:D3} {descriptor.Name}: {exception.Message}");
        }
    }
    Console.WriteLine($"Rendered {renderedChunks:N0} chunks; {failures:N0} map failures.");
    return failures == 0 ? 0 : 1;
}

static int Compare(string[] arguments)
{
    if (arguments.Length < 2)
    {
        PrintUsage();
        return 3;
    }

    string expectedPath = Path.GetFullPath(arguments[0]);
    string actualPath = Path.GetFullPath(arguments[1]);
    string? differencePath = ReadStringOption(arguments, "--diff");
    byte tolerance = checked((byte)ReadIntOption(arguments, "--tolerance", 0, 0, 255));
    RgbaImage expected = LoadOwned(expectedPath);
    RgbaImage actual = LoadOwned(actualPath);
    ImageComparisonResult comparison = RgbaImageComparer.Compare(expected, actual, tolerance);
    Console.WriteLine($"Dimensions match: {comparison.DimensionsMatch}");
    Console.WriteLine($"Changed pixels: {comparison.ChangedPixels:N0} ({comparison.ChangedFraction:P6})");
    Console.WriteLine($"Maximum channel difference: {comparison.MaximumChannelDifference}");
    if (!string.IsNullOrWhiteSpace(differencePath))
    {
        string full = Path.GetFullPath(differencePath);
        EnsureParentDirectory(full);
        File.WriteAllBytes(full, comparison.DifferenceImage.ToPng());
        Console.WriteLine("Difference image: " + full);
    }
    return comparison.IsMatch ? 0 : 1;
}

static GoldenOutputFile WriteImage(string outputDirectory, string relativePath, RgbaImage image, PixelRect mapRegion)
{
    string fullPath = Path.Combine(outputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
    EnsureParentDirectory(fullPath);
    byte[] png = image.ToPng();
    File.WriteAllBytes(fullPath, png);
    string hash = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
    return new GoldenOutputFile(relativePath, mapRegion.X, mapRegion.Y, mapRegion.Width, mapRegion.Height, hash, png.LongLength);
}


static void EnsureParentDirectory(string fullPath)
{
    string? directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
}

static RgbaImage LoadOwned(string path)
{
    byte[] bytes = File.ReadAllBytes(path);
    using MemoryStream stream = new(bytes, writable: false);
    using Image image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
    using Bitmap bitmap = new(image.Width, image.Height, PixelFormat.Format32bppArgb);
    using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.DrawImageUnscaled(image, 0, 0);
    return BitmapPixelConverter.ToRgbaImage(bitmap);
}

static HashSet<int>? ReadMapSelection(string[] arguments)
{
    string? raw = ReadStringOption(arguments, "--maps");
    if (string.IsNullOrWhiteSpace(raw)) return null;
    HashSet<int> values = [];
    foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!int.TryParse(token, out int value) || value <= 0) throw new ArgumentException($"Invalid map ID '{token}' in --maps.");
        values.Add(value);
    }
    return values;
}

static int ReadIntOption(string[] arguments, string name, int fallback, int minimum, int maximum)
{
    string? raw = ReadStringOption(arguments, name);
    if (raw is null) return fallback;
    if (!int.TryParse(raw, out int value) || value < minimum || value > maximum)
        throw new ArgumentOutOfRangeException(name, $"{name} must be between {minimum} and {maximum}.");
    return value;
}

static long ReadLongOption(string[] arguments, string name, long fallback, long minimum, long maximum)
{
    string? raw = ReadStringOption(arguments, name);
    if (raw is null) return fallback;
    if (!long.TryParse(raw, out long value) || value < minimum || value > maximum)
        throw new ArgumentOutOfRangeException(name, $"{name} must be between {minimum} and {maximum}.");
    return value;
}

static string? ReadStringOption(string[] arguments, string name)
{
    int index = Array.FindIndex(arguments, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
    if (index < 0) return null;
    if (index + 1 >= arguments.Length) throw new ArgumentException($"{name} requires a value.");
    return arguments[index + 1];
}

static void WriteDiagnostics(IEnumerable<ProjectDiagnostic> diagnostics)
{
    foreach (ProjectDiagnostic diagnostic in diagnostics)
        Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
}

static int InvalidCommand(string command)
{
    Console.Error.WriteLine("Unknown command: " + command);
    PrintUsage();
    return 3;
}

static int DivideRoundUp(int value, int divisor) => checked((value + divisor - 1) / divisor);

static void PrintUsage()
{
    Console.WriteLine("Playroma.JRPG.GoldenFileBuilder build <project> <output> [--maps 1,2,3] [--frame 0] [--chunk-pixels 1536] [--max-whole-pixels 64000000]");
    Console.WriteLine("Playroma.JRPG.GoldenFileBuilder probe <project> [--maps 1,2,3] [--frame 0] [--chunk-pixels 768]");
    Console.WriteLine("Playroma.JRPG.GoldenFileBuilder compare <expected.png> <actual.png> [--diff difference.png] [--tolerance 0]");
}

sealed record GoldenManifest(
    string Format,
    DateTimeOffset GeneratedUtc,
    string ProjectPath,
    int AnimationFrame,
    int ChunkPixels,
    long MaximumWholePixels,
    IReadOnlyList<GoldenMapManifest> Maps);

sealed record GoldenMapManifest(
    int MapId,
    string Name,
    int WidthTiles,
    int HeightTiles,
    int TilesetId,
    string TilesetName,
    long PixelCount,
    IReadOnlyList<GoldenOutputFile> Files,
    IReadOnlyList<GoldenDiagnostic> Diagnostics);

sealed record GoldenOutputFile(
    string RelativePath,
    int MapX,
    int MapY,
    int Width,
    int Height,
    string Sha256,
    long ByteLength);

sealed record GoldenDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? RelativePath,
    string? JsonPath);
