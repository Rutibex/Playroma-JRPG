using System.Collections.Concurrent;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

internal static class PluginTagCatalogHost
{
    private static readonly Lazy<PluginTagCatalog> CatalogValue = new(() =>
    {
        PluginTagProviderRegistry registry = new();
        registry.Register(YanflyGeneratedCatalogProvider.LoadEmbedded());
        return registry.CreateCatalog();
    });

    public static PluginTagCatalog Catalog => CatalogValue.Value;
}

internal static class PluginTagDatabaseContextMapper
{
    public static bool Supports(MvDocumentKind kind) => TryMap(kind, out _, out _);

    public static PluginTagContext Create(
        MvDocumentKind kind,
        string projectPath,
        string documentPath,
        int recordId)
    {
        if (!TryMap(kind, out PluginDocumentKind documentKind, out PluginAuthoringSurface surface))
            throw new NotSupportedException($"Plugin Tags are not available for {kind} in the shared database tab.");
        return new(surface, documentKind, projectPath, documentPath, recordId);
    }

    private static bool TryMap(
        MvDocumentKind kind,
        out PluginDocumentKind documentKind,
        out PluginAuthoringSurface surface)
    {
        surface = PluginAuthoringSurface.DatabaseNote;
        documentKind = kind switch
        {
            MvDocumentKind.Actors => PluginDocumentKind.Actor,
            MvDocumentKind.Armors => PluginDocumentKind.Armor,
            MvDocumentKind.Classes => PluginDocumentKind.Class,
            MvDocumentKind.Enemies => PluginDocumentKind.Enemy,
            MvDocumentKind.Items => PluginDocumentKind.Item,
            MvDocumentKind.Skills => PluginDocumentKind.Skill,
            MvDocumentKind.States => PluginDocumentKind.State,
            MvDocumentKind.Tilesets => PluginDocumentKind.Tileset,
            MvDocumentKind.Weapons => PluginDocumentKind.Weapon,
            _ => PluginDocumentKind.Unknown
        };
        if (kind == MvDocumentKind.Tilesets) surface = PluginAuthoringSurface.TilesetNote;
        return documentKind != PluginDocumentKind.Unknown;
    }
}

internal sealed record PluginTagProjectIntelligence(
    InstalledPluginAnalysis? Analysis,
    IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability> Availability,
    string? ErrorMessage)
{
    public static PluginTagProjectIntelligence Unavailable(string message) =>
        new(null, new Dictionary<PluginFeatureKey, PluginFeatureAvailability>(), message);
}

internal static class YanflyPluginTagProjectCache
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<PluginTagProjectIntelligence>>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static Task<PluginTagProjectIntelligence> GetAsync(
        string projectRoot,
        PluginTagCatalog catalog)
    {
        string fullPath = Path.GetFullPath(projectRoot);
        return Cache.GetOrAdd(fullPath, path => new Lazy<Task<PluginTagProjectIntelligence>>(
            () => ScanAsync(path, catalog), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public static void Invalidate(string projectRoot) =>
        Cache.TryRemove(Path.GetFullPath(projectRoot), out _);

    private static async Task<PluginTagProjectIntelligence> ScanAsync(
        string projectRoot,
        PluginTagCatalog catalog)
    {
        try
        {
            YanflyProjectPluginScanResult result =
                await new YanflyProjectPluginIntelligenceScanner().ScanAsync(projectRoot).ConfigureAwait(false);
            IReadOnlyList<PluginFeatureAvailability> evaluated =
                new YanflyInstalledPluginIntelligence().EvaluateCatalog(result.Analysis, catalog.Features);
            return new(result.Analysis,
                evaluated.ToDictionary(item => item.Feature), null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return PluginTagProjectIntelligence.Unavailable(exception.Message);
        }
    }
}
