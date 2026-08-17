using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Rendering.Windows;

public sealed class WindowsRenderAssetLoader
{
    private readonly IAssetContentSource _content;
    private readonly string _runtimePrefix;

    public WindowsRenderAssetLoader(IAssetContentSource content, string runtimePrefix)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _runtimePrefix = Normalize(runtimePrefix).Trim('/');
    }

    public async ValueTask<MapRenderAssetSet> LoadAsync(
        MapRenderSnapshot map,
        TilesetRenderSnapshot tileset,
        CancellationToken cancellationToken = default)
    {
        Dictionary<TileSheetKind, RgbaImage> sheets = [];
        Dictionary<string, RgbaImage> characters = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, RgbaImage> parallaxes = new(StringComparer.OrdinalIgnoreCase);
        List<ProjectDiagnostic> diagnostics = [];
        StringBuilder identity = new();

        foreach (TileSheetKind sheet in Enum.GetValues<TileSheetKind>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = tileset.GetSheetName(sheet);
            if (string.IsNullOrWhiteSpace(name)) continue;
            string relativePath = Combine("img/tilesets", name + ".png");
            ImageLoadResult result = await TryLoadAsync(relativePath, cancellationToken).ConfigureAwait(false);
            if (result.Image is not null)
            {
                sheets[sheet] = result.Image;
                identity.Append(sheet).Append('=').Append(result.Hash).Append(';');
                ValidateSheetDimensions(sheet, name, result.Image, relativePath, diagnostics);
            }
            else
            {
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.Phase6RenderAssetMissing,
                    $"Tileset image '{name}.png' could not be loaded.",
                    new ProjectLocation(relativePath, RecordId: tileset.Id),
                    result.Error));
            }
        }

        foreach (string name in map.Events.Select(item => item.Graphic.CharacterName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Combine("img/characters", name + ".png");
            ImageLoadResult result = await TryLoadAsync(relativePath, cancellationToken).ConfigureAwait(false);
            if (result.Image is not null)
            {
                characters[name] = result.Image;
                identity.Append("char:").Append(name).Append('=').Append(result.Hash).Append(';');
            }
            else
            {
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.Phase6RenderAssetMissing,
                    $"Character image '{name}.png' could not be loaded.",
                    new ProjectLocation(relativePath, MapId: map.MapId),
                    result.Error));
            }
        }

        if (map.Parallax.HasImage)
        {
            string relativePath = Combine("img/parallaxes", map.Parallax.Name + ".png");
            ImageLoadResult result = await TryLoadAsync(relativePath, cancellationToken).ConfigureAwait(false);
            if (result.Image is not null)
            {
                parallaxes[map.Parallax.Name] = result.Image;
                identity.Append("parallax:").Append(map.Parallax.Name).Append('=').Append(result.Hash).Append(';');
            }
            else
            {
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.Phase6RenderAssetMissing,
                    $"Parallax image '{map.Parallax.Name}.png' could not be loaded.",
                    new ProjectLocation(relativePath, MapId: map.MapId),
                    result.Error));
            }
        }

        return new MapRenderAssetSet(sheets, characters, parallaxes, diagnostics, identity.ToString());
    }

    private async ValueTask<ImageLoadResult> TryLoadAsync(string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            if (_content is IAssetOverlayContentSource overlay && !overlay.Exists(relativePath)) return new ImageLoadResult(null, null, "The file does not exist in the project or staged file overlay.");
            await using Stream input = await _content.OpenReadAsync(relativePath, cancellationToken).ConfigureAwait(false);
            using MemoryStream buffer = new();
            await input.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            byte[] bytes = buffer.ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            using MemoryStream decodeStream = new(bytes, writable: false);
            using Image decoded = Image.FromStream(decodeStream, useEmbeddedColorManagement: false, validateImageData: true);
            using Bitmap owned = new(decoded.Width, decoded.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(owned))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.DrawImage(decoded, new Rectangle(0, 0, owned.Width, owned.Height));
            }
            return new ImageLoadResult(BitmapPixelConverter.ToRgbaImage(owned), hash, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            return new ImageLoadResult(null, null, exception.Message);
        }
    }

    private static void ValidateSheetDimensions(
        TileSheetKind sheet,
        string name,
        RgbaImage image,
        string relativePath,
        List<ProjectDiagnostic> diagnostics)
    {
        bool valid = sheet switch
        {
            TileSheetKind.A1 => image.Width == 768 && image.Height == 576,
            TileSheetKind.A2 => image.Width == 768 && image.Height == 576,
            TileSheetKind.A3 => image.Width == 768 && image.Height == 384,
            TileSheetKind.A4 => image.Width == 768 && image.Height == 720,
            TileSheetKind.A5 => image.Width == 384 && image.Height == 768,
            _ => image.Width == 768 && image.Height == 768
        };
        if (!valid)
        {
            diagnostics.Add(ProjectDiagnostic.Warning(
                DiagnosticCodes.Phase6TilesetDimensions,
                $"Tileset sheet {sheet} ('{name}') has unusual dimensions {image.Width}×{image.Height}; source rectangles outside the image will be diagnosed during rendering.",
                new ProjectLocation(relativePath)));
        }
    }

    private string Combine(string folder, string filename)
    {
        string relative = folder.Trim('/') + "/" + filename.TrimStart('/');
        return string.IsNullOrEmpty(_runtimePrefix) ? relative : _runtimePrefix + "/" + relative;
    }

    private static string Normalize(string path) => (path ?? string.Empty).Replace('\\', '/');
    private sealed record ImageLoadResult(RgbaImage? Image, string? Hash, string? Error);
}
