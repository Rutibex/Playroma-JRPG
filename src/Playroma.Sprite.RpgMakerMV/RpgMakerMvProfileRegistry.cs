using System.Reflection;
using System.Text.Json;

namespace Playroma.Sprite.RpgMakerMV;

public sealed class RpgMakerMvProfileRegistry
{
    private readonly Dictionary<string, RpgMakerMvImageProfile> profiles;

    private RpgMakerMvProfileRegistry(IEnumerable<RpgMakerMvImageProfile> profiles)
    {
        this.profiles = profiles.ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);
        if (this.profiles.Count == 0) throw new InvalidDataException("The RPG Maker MV profile catalog is empty.");
    }

    public IReadOnlyCollection<RpgMakerMvImageProfile> Profiles => profiles.Values;
    public RpgMakerMvImageProfile GetRequired(string id) => profiles.TryGetValue(id, out RpgMakerMvImageProfile? profile) ? profile : throw new KeyNotFoundException($"Unknown RPG Maker MV image profile '{id}'.");
    public bool TryGet(string id, out RpgMakerMvImageProfile? profile) => profiles.TryGetValue(id, out profile);

    public RpgMakerMvProfileRegistry MergeFromFile(string path, bool allowOverride = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        RpgMakerMvProfileCatalog catalog = JsonSerializer.Deserialize<RpgMakerMvProfileCatalog>(stream, options)
            ?? throw new InvalidDataException("The project profile catalog could not be read.");
        ValidateCatalog(catalog, allowEmpty: true);
        Dictionary<string, RpgMakerMvImageProfile> merged = profiles.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        foreach (RpgMakerMvImageProfile profile in catalog.Profiles)
        {
            if (!allowOverride && merged.ContainsKey(profile.Id))
                throw new InvalidDataException($"Project profile '{profile.Id}' conflicts with a built-in profile.");
            merged[profile.Id] = profile;
        }
        return new RpgMakerMvProfileRegistry(merged.Values);
    }

    public static RpgMakerMvProfileRegistry LoadBuiltIn()
    {
        Assembly assembly = typeof(RpgMakerMvProfileRegistry).Assembly;
        string resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith("RpgMakerMvImageProfiles.json", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("Built-in RPG Maker MV profiles are unavailable.");
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        RpgMakerMvProfileCatalog catalog = JsonSerializer.Deserialize<RpgMakerMvProfileCatalog>(stream, options) ?? throw new InvalidDataException("The profile catalog could not be read.");
        ValidateCatalog(catalog, allowEmpty: false);
        return new(catalog.Profiles);
    }

    private static void ValidateCatalog(RpgMakerMvProfileCatalog catalog, bool allowEmpty)
    {
        if (catalog.SchemaVersion != 1) throw new InvalidDataException("Unsupported RPG Maker MV profile catalog schema.");
        if (!allowEmpty && catalog.Profiles.Count == 0) throw new InvalidDataException("The RPG Maker MV profile catalog is empty.");
        if (catalog.Profiles.Any(profile => string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.AssetFolder)))
            throw new InvalidDataException("Every RPG Maker MV profile requires an id and asset folder.");
        if (catalog.Profiles.Select(profile => profile.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Profiles.Count)
            throw new InvalidDataException("RPG Maker MV profile identifiers must be unique.");
        foreach (RpgMakerMvImageProfile profile in catalog.Profiles)
        {
            if (!profile.RuntimeFormat.Equals("png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Profile '{profile.Id}' must export PNG runtime images.");
            if (profile.Dimensions is null)
                throw new InvalidDataException($"Profile '{profile.Id}' has no dimension rules.");
            int?[] dimensions = [profile.Dimensions.FixedWidth, profile.Dimensions.FixedHeight, profile.Dimensions.RecommendedWidth, profile.Dimensions.RecommendedHeight];
            if (dimensions.Any(value => value is <= 0))
                throw new InvalidDataException($"Profile '{profile.Id}' has a non-positive dimension.");
            if (profile.Grid is MvGridRule grid)
            {
                int?[] values = [grid.Columns, grid.Rows, grid.MinimumRows, grid.MaximumRows, grid.CellWidth, grid.CellHeight, grid.MaximumCells];
                if (values.Any(value => value is <= 0)) throw new InvalidDataException($"Profile '{profile.Id}' has a non-positive grid value.");
                if (grid.MinimumRows is int minimum && grid.MaximumRows is int maximum && minimum > maximum)
                    throw new InvalidDataException($"Profile '{profile.Id}' has contradictory row limits.");
            }
            if (profile.FilenameRules.RequireDollarPrefix && !profile.FilenameRules.AllowDollarPrefix)
                throw new InvalidDataException($"Profile '{profile.Id}' requires but does not allow the '$' prefix.");
            if (profile.Motions.Select(motion => motion.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != profile.Motions.Count)
                throw new InvalidDataException($"Profile '{profile.Id}' has duplicate motion identifiers.");
            if (profile.Motions.Any(motion => string.IsNullOrWhiteSpace(motion.Id) || motion.StartCell < 0 || motion.FrameCount <= 0))
                throw new InvalidDataException($"Profile '{profile.Id}' has an invalid motion definition.");
        }
    }
}
