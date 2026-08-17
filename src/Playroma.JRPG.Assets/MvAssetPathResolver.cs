namespace Playroma.JRPG.Assets;

/// <summary>
/// Backwards-compatible facade over the Phase 5 asset category registry.
/// New code should normally use <see cref="AssetCategoryRegistry"/> directly.
/// </summary>
public static class MvAssetPathResolver
{
    public static IReadOnlyCollection<AssetCategory> StandardCategories =>
        AssetCategoryRegistry.Directories.Keys.ToArray();

    public static string GetCategoryDirectory(AssetCategory category) =>
        AssetCategoryRegistry.Directories.TryGetValue(category, out string? directory)
            ? directory
            : throw new ArgumentOutOfRangeException(nameof(category), category, "The category does not have an MV asset directory.");

    public static bool TryResolveCategory(string runtimeRelativePath, out AssetCategory category) =>
        AssetCategoryRegistry.TryResolveCategory(runtimeRelativePath, out category);

    public static AssetKey CreateKey(AssetCategory category, string relativePath)
    {
        if (AssetCategoryRegistry.TryCreateKey(relativePath, category, out AssetKey key)) return key;
        throw new ArgumentException("The path does not belong to the requested asset category.", nameof(relativePath));
    }

    public static string BuildRuntimeRelativePath(AssetKey key, string extension) =>
        AssetCategoryRegistry.BuildRuntimeRelativePath(key, extension);

    public static AssetMediaKind ResolveMediaKind(string extension) =>
        AssetCategoryRegistry.ResolveMediaKind(extension);

    public static bool IsEncryptedExtension(string extension) =>
        AssetCategoryRegistry.IsEncryptedExtension(extension);

    public static int ExtensionPreference(AssetMediaKind kind, string extension) =>
        AssetCategoryRegistry.ExtensionPreference(extension);

    public static string Normalize(string value) => value.Replace('\\', '/').Trim();
}
