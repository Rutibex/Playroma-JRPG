using System.Text.Json;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Validation;

public sealed record MapDimensions(int Width, int Height);

public sealed class ProjectTargetCatalog
{
    private readonly Dictionary<ReferenceTargetKind, HashSet<int>> _ids = [];
    private readonly HashSet<(AssetCategory Category, string Name)> _assets = new(AssetKeyComparer.Instance);
    private readonly Dictionary<string, bool> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MapDimensions> _mapDimensions = [];

    public IReadOnlyDictionary<int, MapDimensions> MapDimensions => _mapDimensions;

    public bool Exists(ReferenceTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Kind == ReferenceTargetKind.Asset)
        {
            return target.AssetCategory.HasValue
                && !string.IsNullOrWhiteSpace(target.Name)
                && _assets.Contains((target.AssetCategory.Value, NormalizeAssetName(target.Name)));
        }

        if (target.Kind == ReferenceTargetKind.Plugin)
        {
            return !string.IsNullOrWhiteSpace(target.Name) && _plugins.ContainsKey(target.Name);
        }

        if (!target.Id.HasValue || target.Id.Value <= 0) return true;
        return _ids.TryGetValue(target.Kind, out HashSet<int> values) && values.Contains(target.Id.Value);
    }

    public bool IsPluginEnabled(string name) =>
        _plugins.TryGetValue(name, out bool enabled) && enabled;

    public bool TryGetMapDimensions(int mapId, out MapDimensions dimensions) =>
        _mapDimensions.TryGetValue(mapId, out dimensions!);

    public static async ValueTask<ProjectTargetCatalog> BuildAsync(
        IMvProjectWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ProjectTargetCatalog catalog = new();
        foreach (MvMapDescriptor map in workspace.Snapshot.Maps)
        {
            if (map.FileExists) catalog.AddId(ReferenceTargetKind.Map, map.Id);
        }

        foreach (MvPluginEntry plugin in workspace.Snapshot.Plugins.Entries)
        {
            if (!catalog._plugins.ContainsKey(plugin.Name)) catalog._plugins[plugin.Name] = plugin.Enabled;
            else catalog._plugins[plugin.Name] |= plugin.Enabled;
        }

        foreach (AssetEntry asset in workspace.Snapshot.Assets.Assets)
        {
            string logicalName = GetLogicalAssetName(asset);
            if (!string.IsNullOrWhiteSpace(logicalName)) catalog._assets.Add((asset.Category, logicalName));
        }

        foreach (ProjectDocumentHandle handle in workspace.Documents.Where(item => item.Kind != MvDocumentKind.Unknown))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMutableProjectDocument document = await workspace.OpenDocumentAsync(handle.RelativePath, cancellationToken).ConfigureAwait(false);
            ReadOnlyMemory<byte> generated = await document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument json = JsonDocument.Parse(Utf8JsonInput.ToBomlessArray(generated.Span));
            if (handle.Kind == MvDocumentKind.System)
            {
                catalog.AddSystemTypes(json.RootElement);
            }
            else if (handle.Kind == MvDocumentKind.Map)
            {
                int? mapId = ParseMapId(handle.RelativePath);
                int? width = json.RootElement.GetOptionalInt32("width");
                int? height = json.RootElement.GetOptionalInt32("height");
                if (mapId.HasValue && width is > 0 && height is > 0)
                {
                    catalog._mapDimensions[mapId.Value] = new MapDimensions(width.Value, height.Value);
                }
            }
            else
            {
                ReferenceTargetKind targetKind = ToTargetKind(handle.Kind);
                if (targetKind != ReferenceTargetKind.Unknown)
                {
                    catalog.AddDatabaseIds(targetKind, json.RootElement);
                }
            }
        }

        return catalog;
    }

    private void AddSystemTypes(JsonElement root)
    {
        AddArrayRange(root, "switches", ReferenceTargetKind.Switch);
        AddArrayRange(root, "variables", ReferenceTargetKind.Variable);
        AddArrayRange(root, "elements", ReferenceTargetKind.Element);
        AddArrayRange(root, "skillTypes", ReferenceTargetKind.SkillType);
        AddArrayRange(root, "weaponTypes", ReferenceTargetKind.WeaponType);
        AddArrayRange(root, "armorTypes", ReferenceTargetKind.ArmorType);
        AddArrayRange(root, "equipTypes", ReferenceTargetKind.EquipmentType);
    }

    private void AddArrayRange(JsonElement root, string property, ReferenceTargetKind kind)
    {
        if (!root.TryGetPropertyValue(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array) return;
        for (int index = 1; index < array.GetArrayLength(); index++) AddId(kind, index);
    }

    private void AddDatabaseIds(ReferenceTargetKind kind, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        foreach ((int index, JsonElement record) in root.EnumerateIndexedArray())
        {
            if (index == 0 || record.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            int id = record.GetOptionalInt32("id") ?? index;
            if (id > 0) AddId(kind, id);
        }
    }

    private void AddId(ReferenceTargetKind kind, int id)
    {
        if (!_ids.TryGetValue(kind, out HashSet<int> values))
        {
            values = [];
            _ids.Add(kind, values);
        }
        values.Add(id);
    }

    private static ReferenceTargetKind ToTargetKind(MvDocumentKind kind) => kind switch
    {
        MvDocumentKind.Actors => ReferenceTargetKind.Actor,
        MvDocumentKind.Classes => ReferenceTargetKind.Class,
        MvDocumentKind.Skills => ReferenceTargetKind.Skill,
        MvDocumentKind.Items => ReferenceTargetKind.Item,
        MvDocumentKind.Weapons => ReferenceTargetKind.Weapon,
        MvDocumentKind.Armors => ReferenceTargetKind.Armor,
        MvDocumentKind.Enemies => ReferenceTargetKind.Enemy,
        MvDocumentKind.Troops => ReferenceTargetKind.Troop,
        MvDocumentKind.States => ReferenceTargetKind.State,
        MvDocumentKind.Animations => ReferenceTargetKind.Animation,
        MvDocumentKind.Tilesets => ReferenceTargetKind.Tileset,
        MvDocumentKind.CommonEvents => ReferenceTargetKind.CommonEvent,
        _ => ReferenceTargetKind.Unknown
    };

    private static int? ParseMapId(string relativePath)
    {
        string name = Path.GetFileNameWithoutExtension(relativePath);
        return name.StartsWith("Map", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(3), out int id)
            ? id
            : null;
    }

    private static string GetLogicalAssetName(AssetEntry asset)
    {
        string normalized = asset.RelativePath.Replace('\\', '/');
        string prefix = asset.Category switch
        {
            AssetCategory.Animations => "img/animations/",
            AssetCategory.Battlebacks1 => "img/battlebacks1/",
            AssetCategory.Battlebacks2 => "img/battlebacks2/",
            AssetCategory.Characters => "img/characters/",
            AssetCategory.Enemies => "img/enemies/",
            AssetCategory.Faces => "img/faces/",
            AssetCategory.Parallaxes => "img/parallaxes/",
            AssetCategory.Pictures => "img/pictures/",
            AssetCategory.SideViewActors => "img/sv_actors/",
            AssetCategory.SideViewEnemies => "img/sv_enemies/",
            AssetCategory.System => "img/system/",
            AssetCategory.Tilesets => "img/tilesets/",
            AssetCategory.Titles1 => "img/titles1/",
            AssetCategory.Titles2 => "img/titles2/",
            AssetCategory.BackgroundMusic => "audio/bgm/",
            AssetCategory.BackgroundSounds => "audio/bgs/",
            AssetCategory.MusicEffects => "audio/me/",
            AssetCategory.SoundEffects => "audio/se/",
            AssetCategory.Movies => "movies/",
            AssetCategory.Fonts => "fonts/",
            _ => string.Empty
        };
        int prefixIndex = string.IsNullOrEmpty(prefix)
            ? -1
            : normalized.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        string logical = prefixIndex >= 0
            ? normalized[(prefixIndex + prefix.Length)..]
            : Path.GetFileName(normalized);
        return NormalizeAssetName(Path.ChangeExtension(logical, null) ?? logical);
    }

    private static string NormalizeAssetName(string value) => value.Replace('\\', '/').Trim();

    private sealed class AssetKeyComparer : IEqualityComparer<(AssetCategory Category, string Name)>
    {
        public static AssetKeyComparer Instance { get; } = new();
        public bool Equals((AssetCategory Category, string Name) x, (AssetCategory Category, string Name) y) =>
            x.Category == y.Category && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((AssetCategory Category, string Name) obj) =>
            HashCode.Combine(obj.Category, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
