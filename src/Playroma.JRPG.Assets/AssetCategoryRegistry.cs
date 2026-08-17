namespace Playroma.JRPG.Assets;

public static class AssetCategoryRegistry
{
    private static readonly IReadOnlyDictionary<AssetCategory, string> CategoryDirectories =
        new Dictionary<AssetCategory, string>
        {
            [AssetCategory.Animations] = "img/animations",
            [AssetCategory.Battlebacks1] = "img/battlebacks1",
            [AssetCategory.Battlebacks2] = "img/battlebacks2",
            [AssetCategory.Characters] = "img/characters",
            [AssetCategory.Enemies] = "img/enemies",
            [AssetCategory.Faces] = "img/faces",
            [AssetCategory.Parallaxes] = "img/parallaxes",
            [AssetCategory.Pictures] = "img/pictures",
            [AssetCategory.SideViewActors] = "img/sv_actors",
            [AssetCategory.SideViewEnemies] = "img/sv_enemies",
            [AssetCategory.System] = "img/system",
            [AssetCategory.Tilesets] = "img/tilesets",
            [AssetCategory.Titles1] = "img/titles1",
            [AssetCategory.Titles2] = "img/titles2",
            [AssetCategory.BackgroundMusic] = "audio/bgm",
            [AssetCategory.BackgroundSounds] = "audio/bgs",
            [AssetCategory.MusicEffects] = "audio/me",
            [AssetCategory.SoundEffects] = "audio/se",
            [AssetCategory.Fonts] = "fonts",
            [AssetCategory.Movies] = "movies",
            [AssetCategory.ProjectIcon] = "icon"
        };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico", ".rpgmvp"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ogg", ".m4a", ".wav", ".mp3", ".flac", ".rpgmvo"
    };

    private static readonly HashSet<string> MovieExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webm", ".mp4", ".ogv", ".rpgmvm"
    };

    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf", ".otf", ".woff", ".woff2"
    };

    public static IReadOnlyDictionary<AssetCategory, string> Directories => CategoryDirectories;

    public static bool TryResolveCategory(string runtimeRelativePath, out AssetCategory category)
    {
        string normalized = runtimeRelativePath.Replace('\\', '/').TrimStart('/');
        foreach ((AssetCategory candidate, string directory) in CategoryDirectories)
        {
            if (normalized.Equals(directory, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase))
            {
                category = candidate;
                return true;
            }
        }

        category = AssetCategory.Unknown;
        return false;
    }

    public static bool TryCreateKey(string runtimeRelativePath, AssetCategory category, out AssetKey key)
    {
        string normalized = runtimeRelativePath.Replace('\\', '/').TrimStart('/');
        if (!CategoryDirectories.TryGetValue(category, out string? directory))
        {
            key = default;
            return false;
        }

        int index = normalized.IndexOf(directory + "/", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            key = default;
            return false;
        }

        string within = normalized[(index + directory.Length + 1)..];
        string extension = Path.GetExtension(within);
        string logicalName = string.IsNullOrEmpty(extension) ? within : within[..^extension.Length];
        if (string.IsNullOrWhiteSpace(logicalName))
        {
            key = default;
            return false;
        }

        key = new AssetKey(category, logicalName);
        return true;
    }

    public static string BuildRuntimeRelativePath(AssetKey key, string extension)
    {
        if (!CategoryDirectories.TryGetValue(key.Category, out string? directory))
        {
            throw new ArgumentOutOfRangeException(nameof(key), "The asset category has no standard MV directory.");
        }

        string normalizedExtension = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        return $"{directory}/{key.LogicalName}{normalizedExtension}";
    }

    public static AssetMediaKind ResolveMediaKind(string extension)
    {
        if (ImageExtensions.Contains(extension)) return AssetMediaKind.Image;
        if (AudioExtensions.Contains(extension)) return AssetMediaKind.Audio;
        if (MovieExtensions.Contains(extension)) return AssetMediaKind.Movie;
        if (FontExtensions.Contains(extension)) return AssetMediaKind.Font;
        return AssetMediaKind.Unknown;
    }

    public static bool IsEncryptedExtension(string extension) =>
        extension.Equals(".rpgmvp", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".rpgmvo", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".rpgmvm", StringComparison.OrdinalIgnoreCase);

    public static int ExtensionPreference(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => 0,
        ".ogg" => 0,
        ".webm" => 0,
        ".ttf" => 0,
        ".m4a" => 1,
        ".wav" => 2,
        ".jpg" or ".jpeg" => 3,
        ".webp" => 4,
        _ => 10
    };

    public static bool IsRuntimeReserved(AssetKey key) => key.Category switch
    {
        AssetCategory.System => key.LogicalName is "IconSet" or "Window" or "Balloon" or "Damage" or "Loading" or "Shadow1" or "Shadow2" or "States" or "Weapons1" or "Weapons2" or "Weapons3",
        AssetCategory.Fonts => key.LogicalName.Equals("GameFont", StringComparison.OrdinalIgnoreCase),
        AssetCategory.ProjectIcon => true,
        _ => false
    };
}
