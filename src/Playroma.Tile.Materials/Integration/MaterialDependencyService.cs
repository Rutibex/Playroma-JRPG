using System.Drawing;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Serialization;
using Playroma.Tile.Materials.Services;

namespace Playroma.Tile.Materials.Integration;

public sealed class MaterialDependencyService
{
    public bool IsMaterialSourceOutdated(TileProject project, AutotileDefinition definition)
    {
        if (definition.SourceMaterial is null) return false;
        var reference = project.Materials.FirstOrDefault(x => x.Id == definition.SourceMaterial.MaterialId);
        return reference is null || reference.Revision != definition.SourceMaterial.MaterialRevision;
    }

    public Bitmap RebuildAutotileSource(TileProject project, AutotileDefinition definition, CancellationToken cancellationToken = default)
    {
        var binding = definition.SourceMaterial ?? throw new InvalidOperationException("The autotile is not bound to a material recipe.");
        var reference = project.Materials.FirstOrDefault(x => x.Id == binding.MaterialId)
                        ?? throw new InvalidOperationException("The source material recipe no longer exists.");
        var recipe = MaterialSerializer.Load(project, reference);
        var mode = Enum.TryParse<MaterialAdapterMode>(binding.AdapterPreset, true, out var parsed)
            ? parsed
            : MaterialAdapterMode.UniformFloor;
        var frames = definition.Kind is AutotileKind.A1Ocean or AutotileKind.A1DeepOcean or AutotileKind.A1Water or AutotileKind.A1Waterfall ? 3 : 1;
        using var result = new MaterialRenderService().Render(recipe, 192, 192, binding.Seed, MaterialQuality.Final,
            cancellationToken, assetResolver: path => Path.IsPathRooted(path) ? path : ProjectPath.Resolve(project, path), frameCount: frames);
        var adapter = new MaterialAutotileAdapter();
        var rebuilt = adapter.CreateAutotile(recipe, result, new MaterialAutotileSettings
        {
            Mode = mode,
            KindOverride = definition.Kind,
            IsCounter = definition.IsCounter,
            IsOverlay = definition.IsOverlay,
            A2UsageMode = definition.A2UsageMode
        });
        definition.SourceMaterial = rebuilt.Definition.SourceMaterial;
        definition.Seed = binding.Seed;
        definition.Revision++;
        definition.ModifiedUtc = DateTimeOffset.UtcNow;
        return rebuilt.Source;
    }
}
