using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Services;

public sealed class TilesetDependencyService
{
    public TilesetAssetStatus GetStatus(TileProject project, TilesetAssetBinding binding)
    {
        if (binding.IsDetached) return TilesetAssetStatus.Detached;
        if (binding.IsLocked) return TilesetAssetStatus.Locked;
        if (!binding.SourceAssetId.HasValue) return TilesetAssetStatus.Current;
        switch (binding.SourceKind)
        {
            case TilesetAssetSourceKind.AutotileDefinition:
            {
                var source = project.Autotiles.FirstOrDefault(x => x.Id == binding.SourceAssetId.Value);
                if (source is null || !File.Exists(ProjectPath.Resolve(project, source.RelativeDefinitionPath)))
                    return TilesetAssetStatus.MissingSource;
                return source.Revision > binding.SourceRevision ? TilesetAssetStatus.SourceChanged : TilesetAssetStatus.Current;
            }
            case TilesetAssetSourceKind.MaterialRecipe:
            {
                var source = project.Materials.FirstOrDefault(x => x.Id == binding.SourceAssetId.Value);
                if (source is null || !File.Exists(ProjectPath.Resolve(project, source.RelativeRecipePath)))
                    return TilesetAssetStatus.MissingSource;
                return source.Revision > binding.SourceRevision ? TilesetAssetStatus.SourceChanged : TilesetAssetStatus.Current;
            }
            case TilesetAssetSourceKind.ObjectDefinition:
                return TilesetAssetStatus.Current;
            case TilesetAssetSourceKind.LinkedExternalFile:
                return string.IsNullOrWhiteSpace(binding.SourceHash)
                    ? TilesetAssetStatus.MissingSource
                    : TilesetAssetStatus.Current;
            default:
                return TilesetAssetStatus.Current;
        }
    }

    public IReadOnlyDictionary<Guid, TilesetAssetStatus> Analyze(TileProject project, TilesetAssemblyDefinition definition)
        => definition.Assets.ToDictionary(x => x.Id, x => GetStatus(project, x));
}
