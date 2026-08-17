using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Rendering.Windows;

public sealed record LoadedMapRenderContext(
    MapRenderSnapshot Map,
    TilesetRenderSnapshot Tileset,
    MapRenderAssetSet Assets,
    IReadOnlyList<ProjectDiagnostic> Diagnostics,
    IReadOnlyList<RendererCompatibilityNotice> CompatibilityNotices);

public sealed record RendererCompatibilityNotice(
    string PluginName,
    string Message,
    DiagnosticSeverity Severity = DiagnosticSeverity.Warning);

/// <summary>Builds immutable render snapshots and owned image atlases without retaining document or asset file handles.</summary>
public sealed class MvProjectMapRenderLoader
{
    public ValueTask<LoadedMapRenderContext> LoadAsync(
        MvProjectSession session,
        int mapId,
        CancellationToken cancellationToken = default) =>
        LoadAsync(session, mapId, relativePath: null, editorName: null, cancellationToken);

    public async ValueTask<LoadedMapRenderContext> LoadAsync(
        MvProjectSession session,
        int mapId,
        string? relativePath,
        string? editorName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        MvMapDescriptor? descriptor = session.Workspace.Snapshot.Maps.FirstOrDefault(item => item.Id == mapId);
        string resolvedPath = relativePath ?? descriptor?.RelativePath
            ?? throw new KeyNotFoundException($"Map {mapId} was not found.");
        string resolvedName = editorName ?? descriptor?.Name ?? $"Map {mapId:D3}";
        MvDatabaseFileSummary tilesetFile = session.Workspace.Snapshot.Database.Find(MvDocumentKind.Tilesets)
            ?? throw new FileNotFoundException("Tilesets.json is not part of the project snapshot.");

        ReadOnlyMemory<byte> mapBytes;
        await using (IProjectDocumentLease mapLease = await session.Workspace.AcquireDocumentAsync(resolvedPath, cancellationToken).ConfigureAwait(false))
        {
            mapBytes = await mapLease.Document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
        }
        MapRenderSnapshot map = MvRenderSnapshotParser.ParseMap(mapId, resolvedName, resolvedPath, mapBytes);

        ReadOnlyMemory<byte> tilesetBytes;
        await using (IProjectDocumentLease tilesetLease = await session.Workspace.AcquireDocumentAsync(tilesetFile.RelativePath, cancellationToken).ConfigureAwait(false))
        {
            tilesetBytes = await tilesetLease.Document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
        }
        TilesetRenderSnapshot tileset = MvRenderSnapshotParser.ParseTileset(map.TilesetId, tilesetFile.RelativePath, tilesetBytes);

        string projectRoot = session.Workspace.Snapshot.Identity.ProjectRoot;
        string runtimeRoot = session.Workspace.Snapshot.Identity.RuntimeRoot ?? projectRoot;
        string runtimePrefix = Path.GetRelativePath(projectRoot, runtimeRoot).Replace('\\', '/');
        if (runtimePrefix == ".") runtimePrefix = string.Empty;
        WindowsRenderAssetLoader assetLoader = new(session.AssetContent, runtimePrefix);
        MapRenderAssetSet assets = await assetLoader.LoadAsync(map, tileset, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RendererCompatibilityNotice> notices = RendererCompatibilityAnalyzer.Analyse(session.Workspace.Snapshot.Plugins);
        List<ProjectDiagnostic> diagnostics = [];
        diagnostics.AddRange(map.Diagnostics);
        diagnostics.AddRange(tileset.Diagnostics);
        diagnostics.AddRange(assets.Diagnostics);
        foreach (RendererCompatibilityNotice notice in notices)
        {
            diagnostics.Add(new ProjectDiagnostic(
                DiagnosticCodes.Phase6PluginRenderingDifference,
                notice.Severity,
                notice.Message,
                new ProjectLocation(session.Workspace.Snapshot.Plugins.ManifestRelativePath),
                notice.PluginName));
        }
        return new LoadedMapRenderContext(map, tileset, assets, diagnostics, notices);
    }
}

public static class RendererCompatibilityAnalyzer
{
    private static readonly (string Token, string Reason)[] KnownTokens =
    [
        ("doodad", "adds editor/runtime map decorations that are not represented in standard MV map data"),
        ("overlay", "can add image layers above or below the standard MV tilemap"),
        ("foreground", "can add foreground layers not represented by the base map renderer"),
        ("layergraphic", "can add plugin-managed map layers"),
        ("overpass", "can alter tile and character layering"),
        ("regionrestriction", "can change movement semantics associated with region IDs"),
        ("regionrestrict", "can change movement semantics associated with region IDs"),
        ("animatedtile", "can modify autotile animation timing"),
        ("parallax", "can modify parallax positioning or add additional parallax layers")
    ];

    public static IReadOnlyList<RendererCompatibilityNotice> Analyse(MvPluginInventory plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        List<RendererCompatibilityNotice> notices = [];
        foreach (MvPluginEntry entry in plugins.Entries.Where(item => item.Enabled))
        {
            string compact = new string(entry.Name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            foreach ((string token, string reason) in KnownTokens)
            {
                if (!compact.Contains(token, StringComparison.Ordinal)) continue;
                notices.Add(new RendererCompatibilityNotice(
                    entry.Name,
                    $"Plugin '{entry.Name}' {reason}. The map canvas displays the authoritative standard MV base map and does not execute plugin JavaScript during loading."));
                break;
            }
        }
        return notices;
    }
}
