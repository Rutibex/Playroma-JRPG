using Playroma.JRPG.Core;

namespace Playroma.JRPG.Rendering;

public interface IRenderImageProvider
{
    bool TryGetImage(RenderImageKey key, out RgbaImage? image);
}

public sealed class MapRenderAssetSet : IRenderImageProvider
{
    private readonly Dictionary<TileSheetKind, RgbaImage> _tilesets;
    private readonly Dictionary<string, RgbaImage> _characters;
    private readonly Dictionary<string, RgbaImage> _parallaxes;

    public MapRenderAssetSet(
        IReadOnlyDictionary<TileSheetKind, RgbaImage>? tilesets = null,
        IReadOnlyDictionary<string, RgbaImage>? characters = null,
        IReadOnlyDictionary<string, RgbaImage>? parallaxes = null,
        IReadOnlyList<ProjectDiagnostic>? diagnostics = null,
        string cacheIdentity = "")
    {
        _tilesets = tilesets is null ? [] : new Dictionary<TileSheetKind, RgbaImage>(tilesets);
        _characters = characters is null ? new Dictionary<string, RgbaImage>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, RgbaImage>(characters, StringComparer.OrdinalIgnoreCase);
        _parallaxes = parallaxes is null ? new Dictionary<string, RgbaImage>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, RgbaImage>(parallaxes, StringComparer.OrdinalIgnoreCase);
        Diagnostics = diagnostics ?? [];
        CacheIdentity = cacheIdentity ?? string.Empty;
    }

    public IReadOnlyDictionary<TileSheetKind, RgbaImage> Tilesets => _tilesets;
    public IReadOnlyDictionary<string, RgbaImage> Characters => _characters;
    public IReadOnlyDictionary<string, RgbaImage> Parallaxes => _parallaxes;
    public IReadOnlyList<ProjectDiagnostic> Diagnostics { get; }
    public string CacheIdentity { get; }

    public bool TryGetImage(RenderImageKey key, out RgbaImage? image)
    {
        switch (key.Kind)
        {
            case RenderImageKind.Tileset when key.Sheet.HasValue:
                return _tilesets.TryGetValue(key.Sheet.Value, out image);
            case RenderImageKind.Character:
                return _characters.TryGetValue(key.Name, out image);
            case RenderImageKind.Parallax:
                return _parallaxes.TryGetValue(key.Name, out image);
            default:
                image = null;
                return false;
        }
    }
}

public sealed record MapRenderStatistics(
    int BlitCount,
    int FillCount,
    int CharacterCount,
    TimeSpan Elapsed);

public sealed record MapRenderResult(
    RgbaImage Image,
    IReadOnlyList<ProjectDiagnostic> Diagnostics,
    MapRenderStatistics Statistics);
