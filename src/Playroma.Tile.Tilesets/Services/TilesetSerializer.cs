using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Services;

public static class TilesetSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TilesetConstructionReference SaveNew(
        TileProject project,
        TileSetDefinition tileSet,
        TilesetAssemblyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tileSet);
        ArgumentNullException.ThrowIfNull(definition);

        definition.TileSetId = tileSet.Id;
        if (definition.Id == Guid.Empty) definition.Id = Guid.NewGuid();
        definition.ModifiedUtc = DateTimeOffset.UtcNow;
        var relative = ProjectPath.ToPortableRelativePath(Path.Combine("Tilesets", definition.Id.ToString("N"), "tileset.tileset.json"));
        var reference = new TilesetConstructionReference
        {
            Id = definition.Id,
            TileSetId = tileSet.Id,
            Name = definition.Name,
            RelativeDefinitionPath = relative,
            Revision = Math.Max(1, definition.Revision),
            ModifiedUtc = definition.ModifiedUtc
        };
        project.TilesetConstructions.RemoveAll(x => x.Id == reference.Id || x.TileSetId == tileSet.Id);
        project.TilesetConstructions.Add(reference);
        tileSet.ConstructionId = reference.Id;
        Save(project, reference, definition);
        return reference;
    }

    public static void Save(TileProject project, TilesetConstructionReference reference, TilesetAssemblyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(definition);
        definition.ModifiedUtc = DateTimeOffset.UtcNow;
        if (definition.Revision < 1) definition.Revision = 1;
        Normalize(definition);
        var path = ProjectPath.Resolve(project, reference.RelativeDefinitionPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(definition, Options));
        File.Move(temp, path, true);
        reference.Name = definition.Name;
        reference.Revision = definition.Revision;
        reference.ModifiedUtc = definition.ModifiedUtc;
    }

    public static TilesetAssemblyDefinition Load(TileProject project, TilesetConstructionReference reference)
    {
        var path = ProjectPath.Resolve(project, reference.RelativeDefinitionPath);
        var definition = JsonSerializer.Deserialize<TilesetAssemblyDefinition>(File.ReadAllText(path), Options)
                         ?? throw new InvalidDataException("The tileset construction definition is invalid.");
        Normalize(definition);
        return definition;
    }

    public static TilesetAssemblyDefinition LoadOrCreate(TileProject project, TileSetDefinition tileSet)
    {
        var reference = tileSet.ConstructionId is Guid id
            ? project.TilesetConstructions.FirstOrDefault(x => x.Id == id)
            : project.TilesetConstructions.FirstOrDefault(x => x.TileSetId == tileSet.Id);
        if (reference is not null && File.Exists(ProjectPath.Resolve(project, reference.RelativeDefinitionPath)))
            return Load(project, reference);

        var created = new TilesetAssemblyDefinition
        {
            TileSetId = tileSet.Id,
            Name = tileSet.Name,
            Theme = tileSet.Name,
            TemplateKind = GuessTemplate(tileSet.Name)
        };
        InitializeBindings(tileSet, created);
        SaveNew(project, tileSet, created);
        return created;
    }

    public static TilesetConstructionReference GetReference(TileProject project, TilesetAssemblyDefinition definition)
        => project.TilesetConstructions.First(x => x.Id == definition.Id);

    public static void InitializeBindings(TileSetDefinition tileSet, TilesetAssemblyDefinition definition)
    {
        foreach (var sheet in tileSet.Sheets)
        {
            foreach (var region in sheet.Regions)
            {
                if (definition.Assets.Any(x => x.Sheet == sheet.Slot && x.LogicalSlotIndex == region.Index)) continue;
                definition.Assets.Add(new TilesetAssetBinding
                {
                    Sheet = sheet.Slot,
                    LogicalSlotIndex = region.Index,
                    Name = region.Name,
                    Category = region.Tags.FirstOrDefault() ?? string.Empty,
                    IsLocked = region.IsLocked,
                    SourceKind = region.SourceAutotileId.HasValue
                        ? TilesetAssetSourceKind.AutotileDefinition
                        : TilesetAssetSourceKind.ManualArtwork,
                    SourceAssetId = region.SourceAutotileId,
                    SourceRevision = region.SourceAutotileRevision,
                    SourceHash = region.CompiledSourceSha256,
                    CompiledHash = region.CompiledOutputSha256,
                    CompiledUtc = region.CompiledUtc
                });
            }
        }
    }

    private static TilesetTemplateKind GuessTemplate(string name)
    {
        var text = name.ToLowerInvariant();
        if (text.Contains("dungeon")) return TilesetTemplateKind.Dungeon;
        if (text.Contains("inside") || text.Contains("interior")) return TilesetTemplateKind.Interior;
        if (text.Contains("world")) return TilesetTemplateKind.WorldMap;
        if (text.Contains("science") || text.Contains("sf") || text.Contains("space")) return TilesetTemplateKind.ScienceFiction;
        if (text.Contains("cave")) return TilesetTemplateKind.Cave;
        return TilesetTemplateKind.Outdoor;
    }

    private static void Normalize(TilesetAssemblyDefinition definition)
    {
        definition.Name ??= "Tileset Construction";
        definition.Theme ??= "Custom";
        definition.Assets ??= new List<TilesetAssetBinding>();
        definition.Objects ??= new List<TileObjectDefinition>();
        definition.VariationGroups ??= new List<TileVariationGroup>();
        definition.Metadata ??= new List<TileCellMetadata>();
        definition.Construction ??= new TilesetConstructionSettings();
        definition.Export ??= new TilesetExportSettings();
        definition.Snapshots ??= new List<TilesetSnapshotRecord>();
        definition.TestMap ??= TestMapDefinition.CreateDefault();
        definition.TestMap.Layers ??= new Dictionary<TestMapLayer, int[]>();
        foreach (var layer in Enum.GetValues<TestMapLayer>())
        {
            if (!definition.TestMap.Layers.TryGetValue(layer, out var values) ||
                values.Length != definition.TestMap.Width * definition.TestMap.Height)
                definition.TestMap.Layers[layer] = Enumerable.Repeat(-1, definition.TestMap.Width * definition.TestMap.Height).ToArray();
        }
        foreach (var asset in definition.Assets)
        {
            asset.Name ??= string.Empty;
            asset.Category ??= string.Empty;
            asset.SourceHash ??= string.Empty;
            asset.CompiledHash ??= string.Empty;
        }
        foreach (var obj in definition.Objects)
        {
            obj.Name ??= "Object";
            obj.RelativeImagePath ??= string.Empty;
            obj.RelativeShadowPath ??= string.Empty;
            obj.Frames ??= new List<TileObjectFrame>();
            obj.Tags ??= new List<string>();
            obj.Metadata ??= new TileGameplayMetadata();
        }
        foreach (var item in definition.Metadata) item.Metadata ??= new TileGameplayMetadata();
    }
}
