using System.Drawing;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Services;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Materials.Integration;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Serialization;
using Playroma.Tile.Materials.Services;
using Playroma.Tile.RpgMakerMV.Formats;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Services;

public sealed class AssistedTilesetBuildRequest
{
    public TilesetTemplateKind TemplateKind { get; set; } = TilesetTemplateKind.Outdoor;
    public bool FillEmptySlotsOnly { get; set; } = true;
    public bool IncludeSavedAutotiles { get; set; } = true;
    public bool IncludeSavedMaterials { get; set; } = true;
    public int Seed { get; set; } = 12345;
    public int MaximumMaterials { get; set; } = 12;
}

public sealed class AssistedTilesetBuildResult
{
    public int AutotilesPlaced { get; set; }
    public int MaterialsPlaced { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public sealed class AssistedTilesetBuilder
{
    public AssistedTilesetBuildResult Build(TileProject project, TileSetDefinition tileSet,
        TilesetAssemblyDefinition assembly, IDictionary<MvSheetSlot, Bitmap> sheets,
        AssistedTilesetBuildRequest request)
    {
        var result = new AssistedTilesetBuildResult();
        var construction = new TilesetConstructionService();
        var compiler = new MvAutotileCompiler();
        if (request.IncludeSavedAutotiles)
        {
            foreach (var reference in project.Autotiles)
            {
                try
                {
                    var definition = AutotileSerializer.Load(project, reference);
                    if (!TryFindSlot(definition.Kind, sheets, request.FillEmptySlotsOnly, out var sheet, out var slot)) continue;
                    using var source = AutotileSerializer.LoadSource(project, definition);
                    var sheetDefinition = tileSet.Sheets.First(x => x.Slot == sheet);
                    var regionMetadata = sheetDefinition.Regions.First(x => x.Index == slot);
                    var compile = compiler.CompileIntoSheet(sheets[sheet], regionMetadata, sheet, slot, definition, source);
                    construction.AssignBinding(assembly, sheet, slot, TilesetAssetSourceKind.AutotileDefinition,
                        reference.Id, reference.Revision, compile.SourceSha256, compile.OutputSha256, definition.Name,
                        string.Join(", ", definition.Tags));
                    result.AutotilesPlaced++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Could not place autotile {reference.Name}: {ex.Message}");
                }
            }
        }

        if (request.IncludeSavedMaterials)
        {
            var rendered = 0;
            foreach (var reference in project.Materials)
            {
                if (rendered >= request.MaximumMaterials) break;
                try
                {
                    var recipe = MaterialSerializer.Load(project, reference);
                    var mode = InferMode(recipe);
                    var kind = InferKind(mode);
                    if (!TryFindSlot(kind, sheets, request.FillEmptySlotsOnly, out var sheet, out var slot)) continue;
                    using var material = new MaterialRenderService().Render(recipe, 96, 96,
                        request.Seed + rendered * 977, MaterialQuality.Final,
                        frameCount: mode is MaterialAdapterMode.AnimatedWater or MaterialAdapterMode.Waterfall ? 3 : 1);
                    var created = new MaterialAutotileAdapter().CreateAutotile(recipe, material,
                        new MaterialAutotileSettings { Mode = mode });
                    using var source = created.Source;
                    var sheetDefinition = tileSet.Sheets.First(x => x.Slot == sheet);
                    var regionMetadata = sheetDefinition.Regions.First(x => x.Index == slot);
                    var compile = compiler.CompileIntoSheet(sheets[sheet], regionMetadata, sheet, slot, created.Definition, source);
                    construction.AssignBinding(assembly, sheet, slot, TilesetAssetSourceKind.MaterialRecipe,
                        reference.Id, reference.Revision, compile.SourceSha256, compile.OutputSha256, recipe.Name,
                        string.Join(", ", recipe.Tags));
                    result.MaterialsPlaced++;
                    rendered++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Could not place material {reference.Name}: {ex.Message}");
                }
            }
        }
        assembly.TemplateKind = request.TemplateKind;
        assembly.Revision++;
        return result;
    }

    private static bool TryFindSlot(AutotileKind kind, IDictionary<MvSheetSlot, Bitmap> sheets, bool emptyOnly,
        out MvSheetSlot sheet, out int slot)
    {
        foreach (var candidate in new[] { MvSheetSlot.A1, MvSheetSlot.A2, MvSheetSlot.A3, MvSheetSlot.A4 })
        {
            if (!sheets.TryGetValue(candidate, out var bitmap)) continue;
            var regions = MvLayoutCatalog.GetLogicalRegions(candidate);
            for (var i = 0; i < regions.Count; i++)
            {
                if (!AutotileKindInfo.IsCompatible(kind, candidate, i)) continue;
                if (emptyOnly && !TilesetConstructionService.IsRegionEmpty(bitmap, candidate, i)) continue;
                sheet = candidate;
                slot = i;
                return true;
            }
        }
        sheet = default;
        slot = -1;
        return false;
    }

    private static MaterialAdapterMode InferMode(Playroma.Tile.Materials.Models.MaterialRecipe recipe)
    {
        var text = (recipe.Name + " " + string.Join(' ', recipe.Tags)).ToLowerInvariant();
        if (text.Contains("waterfall")) return MaterialAdapterMode.Waterfall;
        if (text.Contains("water") || text.Contains("ocean") || text.Contains("lava")) return MaterialAdapterMode.AnimatedWater;
        if (text.Contains("wall") || text.Contains("cliff")) return MaterialAdapterMode.Wall;
        if (text.Contains("roof") || text.Contains("shingle")) return MaterialAdapterMode.Roof;
        if (text.Contains("overlay") || text.Contains("flower") || text.Contains("decoration")) return MaterialAdapterMode.TransparentDecoration;
        return MaterialAdapterMode.UniformFloor;
    }

    private static AutotileKind InferKind(MaterialAdapterMode mode) => mode switch
    {
        MaterialAdapterMode.Wall or MaterialAdapterMode.Cliff => AutotileKind.A4Wall,
        MaterialAdapterMode.Roof => AutotileKind.A3Building,
        MaterialAdapterMode.AnimatedWater or MaterialAdapterMode.Shoreline => AutotileKind.A1Water,
        MaterialAdapterMode.Waterfall => AutotileKind.A1Waterfall,
        _ => AutotileKind.A2Floor
    };
}
