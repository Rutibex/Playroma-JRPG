using System.Text.Json;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Tilemaps;

public sealed record TileStudioBridgeRequest(
    string ManifestPath,
    string SourceImagePath,
    string SuggestedName,
    int TileWidth,
    int TileHeight,
    string Mode,
    string? RpgMakerSlot,
    string? ReturnDirectory);

public static class TileStudioBridge
{
    public static TileStudioBridgeRequest Export(SpritePackageData package, string bridgeRoot, string suggestedName, int tileWidth, int tileHeight, string mode = "tileset", string? rpgMakerSlot = null, IReadOnlyCollection<int>? frameIndexes = null)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentException.ThrowIfNullOrWhiteSpace(bridgeRoot); ArgumentException.ThrowIfNullOrWhiteSpace(suggestedName);
        Directory.CreateDirectory(bridgeRoot); string token = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]; string directory = Path.Combine(bridgeRoot, token); Directory.CreateDirectory(directory);
        int[] frames = frameIndexes?.Distinct().OrderBy(index => index).ToArray() ?? [0];
        List<string> images = [];
        foreach (int frame in frames)
        {
            if (frame < 0 || frame >= package.Document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(frameIndexes));
            PixelBuffer pixels = LayerCompositor.Composite(package.Document, package.PixelBuffers, package.Document.Frames[frame].Id);
            string path = Path.Combine(directory, frames.Length == 1 ? Sanitize(suggestedName) + ".png" : $"{Sanitize(suggestedName)}-{frame:D3}.png"); PngCodec.Save(pixels, path); images.Add(path);
        }
        string manifestPath = Path.Combine(directory, "sprite-to-tile.json"); string returnDirectory = Path.Combine(directory, "return"); Directory.CreateDirectory(returnDirectory);
        var manifest = new
        {
            schema = "playroma-sprite-tile-bridge/1",
            createdUtc = DateTimeOffset.UtcNow,
            sourceDocumentId = package.Document.Id,
            sourceDocumentName = package.Document.Name,
            suggestedName,
            mode,
            tileWidth,
            tileHeight,
            rpgMakerSlot,
            images,
            returnDirectory,
            palette = package.Document.ActivePaletteId is Guid paletteId ? package.Document.Palettes.FirstOrDefault(item => item.Id == paletteId) : null
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return new(manifestPath, images[0], suggestedName, tileWidth, tileHeight, mode, rpgMakerSlot, returnDirectory);
    }

    public static IReadOnlyList<string> FindReturnedPngFiles(TileStudioBridgeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); if (string.IsNullOrWhiteSpace(request.ReturnDirectory) || !Directory.Exists(request.ReturnDirectory)) return [];
        return Directory.EnumerateFiles(request.ReturnDirectory, "*.png", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars(); string result = new(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()); return string.IsNullOrWhiteSpace(result) ? "SpriteTiles" : result.Trim();
    }
}
