namespace Playroma.Sprite.Core;

public sealed record SpriteValidationIssue(string Code, string Severity, string Message, string? Path = null);

public static class SpriteDocumentValidator
{
    public static IReadOnlyList<SpriteValidationIssue> Validate(SpriteDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<SpriteValidationIssue> issues = [];
        if (document.SchemaVersion <= 0 || document.SchemaVersion > SpriteFormatVersion.Current)
            issues.Add(new("PSPR-DOC-001", "error", "Unsupported sprite document schema version.", "schemaVersion"));
        if (document.Id == Guid.Empty)
            issues.Add(new("PSPR-DOC-002", "error", "Document identifier cannot be empty.", "id"));
        if (string.IsNullOrWhiteSpace(document.Name))
            issues.Add(new("PSPR-DOC-003", "error", "Document name is required.", "name"));
        if (document.Canvas is null || document.Canvas.Width <= 0 || document.Canvas.Height <= 0)
            issues.Add(new("PSPR-DOC-004", "error", "Canvas dimensions must be positive.", "canvas"));
        else if (document.Canvas.PixelAspectWidth <= 0 || document.Canvas.PixelAspectHeight <= 0)
            issues.Add(new("PSPR-DOC-005", "error", "Pixel aspect ratio must be positive.", "canvas"));
        if (document.Frames is null || document.Frames.Count == 0)
            issues.Add(new("PSPR-DOC-006", "error", "At least one frame is required.", "frames"));
        if (document.Layers is null || document.Layers.Count == 0)
            issues.Add(new("PSPR-DOC-007", "error", "At least one layer is required.", "layers"));
        if (document.Frames is null || document.Frames.Count == 0 || document.Layers is null || document.Layers.Count == 0) return issues;

        List<SpriteFrame> frames = document.Frames!;
        List<SpriteLayer> layers = document.Layers!;
        if (frames.Any(frame => frame.Id == Guid.Empty))
            issues.Add(new("PSPR-DOC-008", "error", "Frame identifiers cannot be empty.", "frames"));
        if (frames.Any(frame => frame.DurationMilliseconds <= 0))
            issues.Add(new("PSPR-DOC-009", "error", "Frame durations must be positive.", "frames"));
        if (frames.Select(frame => frame.Id).Distinct().Count() != frames.Count)
            issues.Add(new("PSPR-DOC-010", "error", "Frame identifiers must be unique.", "frames"));
        if (frames.Select(frame => frame.Number).Distinct().Count() != frames.Count || !frames.OrderBy(frame => frame.Number).Select(frame => frame.Number).SequenceEqual(Enumerable.Range(0, frames.Count)))
            issues.Add(new("PSPR-DOC-011", "error", "Frame numbers must be unique and contiguous from zero.", "frames"));

        if (layers.Any(layer => layer.Id == Guid.Empty))
            issues.Add(new("PSPR-DOC-012", "error", "Layer identifiers cannot be empty.", "layers"));
        if (layers.Select(layer => layer.Id).Distinct().Count() != layers.Count)
            issues.Add(new("PSPR-DOC-013", "error", "Layer identifiers must be unique.", "layers"));
        HashSet<Guid> layerIds = layers.Select(layer => layer.Id).ToHashSet();
        foreach (SpriteLayer layer in layers)
        {
            if (string.IsNullOrWhiteSpace(layer.Name))
                issues.Add(new("PSPR-DOC-014", "error", "Layer names are required.", "layers"));
            if (layer.ParentId is Guid parent && (!layerIds.Contains(parent) || parent == layer.Id))
                issues.Add(new("PSPR-DOC-015", "error", $"Layer '{layer.Name}' has an invalid parent.", "layers"));
            if (layer.ParentId is Guid validParent && layers.FirstOrDefault(candidate => candidate.Id == validParent)?.Kind != SpriteLayerKind.Group)
                issues.Add(new("PSPR-DOC-034", "error", $"Layer '{layer.Name}' is parented to a non-group layer.", "layers"));
            if (layer.Kind == SpriteLayerKind.Group && ((layer.Cels?.Count ?? 0) != 0 || (layer.TilemapCels?.Count ?? 0) != 0))
                issues.Add(new("PSPR-DOC-035", "error", $"Group layer '{layer.Name}' cannot contain cels.", "layers.cels"));
            if (layer.Kind == SpriteLayerKind.Tilemap && (layer.Cels?.Count ?? 0) != 0)
                issues.Add(new("PSPR-DOC-048", "error", $"Tilemap layer '{layer.Name}' cannot contain image cels.", "layers.cels"));
            if (layer.Kind != SpriteLayerKind.Tilemap && (layer.TilemapCels?.Count ?? 0) != 0)
                issues.Add(new("PSPR-DOC-049", "error", $"Non-tilemap layer '{layer.Name}' cannot contain tilemap cels.", "layers.tilemapCels"));
            if (string.IsNullOrWhiteSpace(layer.BlendMode))
                issues.Add(new("PSPR-DOC-036", "error", $"Layer '{layer.Name}' requires a blend mode.", "layers.blendMode"));
            else if (!SpriteBlendModes.IsSupported(layer.BlendMode))
                issues.Add(new("PSPR-DOC-037", "error", $"Layer '{layer.Name}' uses unsupported blend mode '{layer.BlendMode}'.", "layers.blendMode"));
        }
        if (HasLayerCycle(layers))
            issues.Add(new("PSPR-DOC-016", "error", "Layer hierarchy contains a cycle.", "layers"));

        HashSet<Guid> frameIds = frames.Select(frame => frame.Id).ToHashSet();
        List<SpriteCel> cels = layers.SelectMany(layer => layer.Cels ?? []).ToList();
        if (cels.Any(cel => cel.Id == Guid.Empty || cel.PixelContentId == Guid.Empty))
            issues.Add(new("PSPR-DOC-017", "error", "Cel and pixel-content identifiers cannot be empty.", "layers.cels"));
        if (cels.Select(cel => cel.Id).Distinct().Count() != cels.Count)
            issues.Add(new("PSPR-DOC-018", "error", "Cel identifiers must be unique across the document.", "layers.cels"));
        if (cels.Any(cel => !frameIds.Contains(cel.FrameId)))
            issues.Add(new("PSPR-DOC-019", "error", "Every cel must reference an existing frame.", "layers.cels"));
        foreach (SpriteLayer layer in layers.Where(layer => layer.Kind != SpriteLayerKind.Group))
            if ((layer.Cels ?? []).GroupBy(cel => cel.FrameId).Any(group => group.Count() > 1))
                issues.Add(new("PSPR-DOC-038", "error", $"Layer '{layer.Name}' contains more than one cel in the same frame.", "layers.cels"));

        List<SpritePixelContentDescriptor> contents = document.PixelContents ?? [];
        if (contents.Count == 0)
            issues.Add(new("PSPR-DOC-033", "error", "At least one pixel-content descriptor is required.", "pixelContents"));
        if (contents.Any(content => content.Id == Guid.Empty) || contents.Select(content => content.Id).Distinct().Count() != contents.Count)
            issues.Add(new("PSPR-DOC-030", "error", "Pixel-content identifiers must be non-empty and unique.", "pixelContents"));
        if (contents.Any(content => content.Width <= 0 || content.Height <= 0 || !content.Encoding.Equals("rgba32", StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("PSPR-DOC-031", "error", "Pixel contents require positive dimensions and rgba32 encoding.", "pixelContents"));
        HashSet<Guid> contentIds = contents.Select(content => content.Id).ToHashSet();
        if (cels.Any(cel => !contentIds.Contains(cel.PixelContentId)))
            issues.Add(new("PSPR-DOC-032", "error", "Every cel must reference an existing pixel content descriptor.", "layers.cels"));

        List<SpriteTilemapContent> tilemapContents = document.TilemapContents ?? [];
        if (tilemapContents.Any(item => item.Id == Guid.Empty) || tilemapContents.Select(item => item.Id).Distinct().Count() != tilemapContents.Count)
            issues.Add(new("PSPR-DOC-050", "error", "Tilemap-content identifiers must be non-empty and unique.", "tilemapContents"));
        foreach (SpriteTilemapContent tilemap in tilemapContents)
        {
            if (tilemap.Columns <= 0 || tilemap.Rows <= 0)
                issues.Add(new("PSPR-DOC-051", "error", "Tilemap dimensions must be positive.", "tilemapContents"));
            else if ((tilemap.Cells?.Count ?? 0) != tilemap.CellCount)
                issues.Add(new("PSPR-DOC-052", "error", "Every tilemap must contain exactly columns × rows cells.", "tilemapContents.cells"));
            if ((tilemap.Cells ?? []).Any(cell => cell.TileIndex < tilemap.EmptyTileIndex || cell.Opacity > byte.MaxValue))
                issues.Add(new("PSPR-DOC-053", "error", "Tilemap cells contain an invalid tile index or opacity.", "tilemapContents.cells"));
        }
        HashSet<Guid> tilemapContentIds = tilemapContents.Select(item => item.Id).ToHashSet();
        List<SpriteTilemapCel> tilemapCels = layers.SelectMany(layer => layer.TilemapCels ?? []).ToList();
        if (tilemapCels.Any(cel => cel.Id == Guid.Empty || cel.TilemapContentId == Guid.Empty))
            issues.Add(new("PSPR-DOC-054", "error", "Tilemap cel and content identifiers cannot be empty.", "layers.tilemapCels"));
        if (tilemapCels.Select(cel => cel.Id).Distinct().Count() != tilemapCels.Count)
            issues.Add(new("PSPR-DOC-055", "error", "Tilemap cel identifiers must be unique.", "layers.tilemapCels"));
        if (tilemapCels.Any(cel => !frameIds.Contains(cel.FrameId) || !tilemapContentIds.Contains(cel.TilemapContentId)))
            issues.Add(new("PSPR-DOC-056", "error", "Every tilemap cel must reference an existing frame and tilemap content.", "layers.tilemapCels"));
        foreach (SpriteLayer layer in layers.Where(layer => layer.Kind == SpriteLayerKind.Tilemap))
            if ((layer.TilemapCels ?? []).GroupBy(cel => cel.FrameId).Any(group => group.Count() > 1))
                issues.Add(new("PSPR-DOC-057", "error", $"Tilemap layer '{layer.Name}' contains more than one cel in the same frame.", "layers.tilemapCels"));

        List<SpritePalette> palettes = document.Palettes ?? [];
        if (palettes.Select(palette => palette.Id).Distinct().Count() != palettes.Count || palettes.Any(palette => palette.Id == Guid.Empty))
            issues.Add(new("PSPR-DOC-022", "error", "Palette identifiers must be non-empty and unique.", "palettes"));
        foreach (SpritePalette palette in palettes)
        {
            if (string.IsNullOrWhiteSpace(palette.Name)) issues.Add(new("PSPR-DOC-023", "error", "Palette names are required.", "palettes"));
            List<SpritePaletteEntry> entries = palette.Entries ?? [];
            if (entries.Count is < 1 or > 256)
                issues.Add(new("PSPR-DOC-040", "error", $"Palette '{palette.Name}' must contain 1 to 256 entries.", "palettes.entries"));
            if (entries.Select(entry => entry.Index).Distinct().Count() != entries.Count || entries.Any(entry => entry.Index < 0 || entry.Index > 255))
                issues.Add(new("PSPR-DOC-024", "error", $"Palette '{palette.Name}' entries require unique indices from 0 through 255.", "palettes.entries"));
        }
        if (document.ActivePaletteId is Guid activePalette && palettes.All(palette => palette.Id != activePalette))
            issues.Add(new("PSPR-DOC-041", "error", "The active palette identifier does not exist in this document.", "activePaletteId"));
        if (document.TransparentPaletteIndex is < 0 or > 255)
            issues.Add(new("PSPR-DOC-042", "error", "The transparent palette index must be from 0 through 255.", "transparentPaletteIndex"));
        if (document.ColorMode == SpriteColorMode.Indexed && palettes.Count == 0)
            issues.Add(new("PSPR-DOC-043", "error", "Indexed documents require at least one palette.", "palettes"));
        if (document.ColorProfileMetadata is null || string.IsNullOrWhiteSpace(document.ColorProfileMetadata.Name) || string.IsNullOrWhiteSpace(document.ColorProfileMetadata.Kind) || document.ColorProfileMetadata.Gamma <= 0d || double.IsNaN(document.ColorProfileMetadata.Gamma) || double.IsInfinity(document.ColorProfileMetadata.Gamma))
            issues.Add(new("PSPR-DOC-044", "error", "Color-profile metadata requires a name, kind, and finite positive gamma.", "colorProfileMetadata"));
        if (document.ColorMode == SpriteColorMode.Indexed && document.ActivePaletteId is null)
            issues.Add(new("PSPR-DOC-045", "error", "Indexed documents require an active palette.", "activePaletteId"));
        SpritePalette? selectedPalette = document.ActivePaletteId is Guid selectedPaletteId ? palettes.FirstOrDefault(palette => palette.Id == selectedPaletteId) : null;
        if (selectedPalette is not null && document.TransparentPaletteIndex >= selectedPalette.Entries.Count)
            issues.Add(new("PSPR-DOC-046", "error", "The transparent palette index must identify an entry in the active palette.", "transparentPaletteIndex"));
        foreach (SpritePalette palette in palettes)
            if (!palette.Entries.OrderBy(entry => entry.Index).Select(entry => entry.Index).SequenceEqual(Enumerable.Range(0, palette.Entries.Count)))
                issues.Add(new("PSPR-DOC-047", "error", $"Palette '{palette.Name}' indices must be contiguous from zero.", "palettes.entries"));

        List<SpriteTileset> tilesets = document.Tilesets ?? [];
        if (tilesets.Select(tileset => tileset.Id).Distinct().Count() != tilesets.Count || tilesets.Any(tileset => tileset.Id == Guid.Empty))
            issues.Add(new("PSPR-DOC-025", "error", "Tileset identifiers must be non-empty and unique.", "tilesets"));
        foreach (SpriteTileset tileset in tilesets)
        {
            if (string.IsNullOrWhiteSpace(tileset.Name) || tileset.TileWidth <= 0 || tileset.TileHeight <= 0)
                issues.Add(new("PSPR-DOC-026", "error", "Tilesets require a name and positive tile dimensions.", "tilesets"));
            List<SpriteTileDefinition> tiles = tileset.Tiles ?? [];
            if (tiles.Select(tile => tile.Id).Distinct().Count() != tiles.Count || tiles.Any(tile => tile.Id == Guid.Empty))
                issues.Add(new("PSPR-DOC-027", "error", $"Tileset '{tileset.Name}' tile identifiers must be non-empty and unique.", "tilesets.tiles"));
            if (tiles.Select(tile => tile.Index).Distinct().Count() != tiles.Count || tiles.Any(tile => tile.Index < 0))
                issues.Add(new("PSPR-DOC-028", "error", $"Tileset '{tileset.Name}' tile indices must be non-negative and unique.", "tilesets.tiles"));
            if (tiles.Any(tile => !tile.Empty && (!tile.PixelContentId.HasValue || tile.PixelContentId.Value == Guid.Empty)))
                issues.Add(new("PSPR-DOC-029", "error", $"Non-empty tiles in '{tileset.Name}' require pixel content.", "tilesets.tiles"));
            if (tiles.Any(tile => tile.PixelContentId is Guid id && !contentIds.Contains(id)))
                issues.Add(new("PSPR-DOC-058", "error", $"Tileset '{tileset.Name}' references missing pixel content.", "tilesets.tiles"));
            if (tileset.Columns < 0 || tileset.Rows < 0 || tileset.Margin < 0 || tileset.Spacing < 0 || tileset.Revision <= 0)
                issues.Add(new("PSPR-DOC-059", "error", $"Tileset '{tileset.Name}' has invalid grid or revision metadata.", "tilesets"));
            if ((tileset.AutoRules ?? []).Any(rule => rule.NeighborMask is < 0 or > 255 || tiles.All(tile => tile.Index != rule.TileIndex)))
                issues.Add(new("PSPR-DOC-060", "error", $"Tileset '{tileset.Name}' contains an invalid automatic-tile rule.", "tilesets.autoRules"));
        }
        HashSet<Guid> tilesetIds = tilesets.Select(item => item.Id).ToHashSet();
        foreach (SpriteLayer layer in layers.Where(layer => layer.Kind == SpriteLayerKind.Tilemap))
        {
            if (layer.TilesetId is not Guid tilesetId || !tilesetIds.Contains(tilesetId))
                issues.Add(new("PSPR-DOC-061", "error", $"Tilemap layer '{layer.Name}' must reference an existing tileset.", "layers.tilesetId"));
            else
            {
                SpriteTileset tileset = tilesets.First(item => item.Id == tilesetId);
                HashSet<int> validTileIndexes = tileset.Tiles.Select(tile => tile.Index).ToHashSet();
                foreach (SpriteTilemapCel cel in layer.TilemapCels ?? [])
                {
                    SpriteTilemapContent? map = tilemapContents.FirstOrDefault(item => item.Id == cel.TilemapContentId);
                    if (map is not null && map.Cells.Any(cell => cell.TileIndex != map.EmptyTileIndex && !validTileIndexes.Contains(cell.TileIndex)))
                        issues.Add(new("PSPR-DOC-062", "error", $"Tilemap layer '{layer.Name}' contains a tile index not present in its tileset.", "tilemapContents.cells"));
                }
            }
        }

        List<SpriteSlice> slices = document.Slices ?? [];
        if (slices.Any(slice => slice.Id == Guid.Empty) || slices.Select(slice => slice.Id).Distinct().Count() != slices.Count)
            issues.Add(new("PSPR-DOC-063", "error", "Slice identifiers must be non-empty and unique.", "slices"));
        foreach (SpriteSlice slice in slices)
        {
            if (string.IsNullOrWhiteSpace(slice.Name)) issues.Add(new("PSPR-DOC-064", "error", "Slice names are required.", "slices"));
            if (slice.Bounds.Width < 0 || slice.Bounds.Height < 0) issues.Add(new("PSPR-DOC-065", "error", $"Slice '{slice.Name}' has invalid bounds.", "slices.bounds"));
            if ((slice.Keys ?? []).Select(key => key.FrameNumber).Distinct().Count() != (slice.Keys?.Count ?? 0))
                issues.Add(new("PSPR-DOC-066", "error", $"Slice '{slice.Name}' has duplicate animation keys.", "slices.keys"));
            foreach (SpriteSliceKey key in slice.Keys ?? [])
            {
                if (key.FrameNumber < 0 || key.FrameNumber >= frames.Count || key.Bounds.Width <= 0 || key.Bounds.Height <= 0)
                    issues.Add(new("PSPR-DOC-067", "error", $"Slice '{slice.Name}' contains an invalid animation key.", "slices.keys"));
                if (key.Center is IntRect center && (center.Width < 0 || center.Height < 0 || center.X < key.Bounds.X || center.Y < key.Bounds.Y || center.Right > key.Bounds.Right || center.Bottom > key.Bounds.Bottom))
                    issues.Add(new("PSPR-DOC-068", "error", $"Slice '{slice.Name}' has a nine-slice center outside its bounds.", "slices.keys.center"));
            }
        }

        List<SpriteAnimationTag> tags = document.Tags ?? [];
        if (tags.Any(tag => tag.Id == Guid.Empty) || tags.Select(tag => tag.Id).Distinct().Count() != tags.Count)
            issues.Add(new("PSPR-DOC-039", "error", "Animation tag identifiers must be non-empty and unique.", "tags"));
        foreach (SpriteAnimationTag tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Name))
                issues.Add(new("PSPR-DOC-020", "error", "Animation tag names are required.", "tags"));
            if (tag.FromFrame < 0 || tag.ToFrame < tag.FromFrame || tag.ToFrame >= frames.Count)
                issues.Add(new("PSPR-DOC-021", "error", $"Tag '{tag.Name}' has an invalid frame range.", "tags"));
        }
        return issues;
    }

    public static void ThrowIfInvalid(SpriteDocument document)
    {
        IReadOnlyList<SpriteValidationIssue> errors = Validate(document).Where(issue => issue.Severity == "error").ToArray();
        if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static bool HasLayerCycle(IReadOnlyCollection<SpriteLayer> layers)
    {
        Dictionary<Guid, Guid?> parents = layers.ToDictionary(layer => layer.Id, layer => layer.ParentId);
        foreach (Guid id in parents.Keys)
        {
            HashSet<Guid> seen = [];
            Guid? current = id;
            while (current is Guid value && parents.TryGetValue(value, out Guid? parent))
            {
                if (!seen.Add(value)) return true;
                current = parent;
            }
        }
        return false;
    }
}
