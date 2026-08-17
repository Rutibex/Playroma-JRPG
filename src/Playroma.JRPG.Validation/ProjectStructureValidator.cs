using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Validation;

public sealed class ProjectStructureValidator : IProjectValidator
{
    public string Name => "Project structure";

    public async ValueTask<IReadOnlyList<ProjectValidationDiagnostic>> ValidateAsync(
        ProjectValidationContext context,
        CancellationToken cancellationToken = default)
    {
        List<ProjectValidationDiagnostic> diagnostics = [];
        AddPluginDiagnostics(context, diagnostics);
        AddPluginUsageDiagnostics(context, diagnostics);

        ProjectDocumentHandle? systemHandle = context.Workspace.Documents.FirstOrDefault(handle => handle.Kind == MvDocumentKind.System);
        if (systemHandle is not null)
        {
            JsonDocument system = await ReadJsonAsync(context, systemHandle, cancellationToken).ConfigureAwait(false);
            using (system)
            {
                ValidateStartingPosition(system.RootElement, "startMapId", "startX", "startY", "Player", systemHandle.RelativePath, context, diagnostics);
                ValidateVehicle(system.RootElement, "boat", "Boat", systemHandle.RelativePath, context, diagnostics);
                ValidateVehicle(system.RootElement, "ship", "Ship", systemHandle.RelativePath, context, diagnostics);
                ValidateVehicle(system.RootElement, "airship", "Airship", systemHandle.RelativePath, context, diagnostics);
            }
        }

        ProjectDocumentHandle? mapInfosHandle = context.Workspace.Documents.FirstOrDefault(handle => handle.Kind == MvDocumentKind.MapInfos);
        if (mapInfosHandle is not null)
        {
            JsonDocument mapInfos = await ReadJsonAsync(context, mapInfosHandle, cancellationToken).ConfigureAwait(false);
            using (mapInfos) ValidateMapHierarchy(mapInfos.RootElement, mapInfosHandle.RelativePath, diagnostics);
        }

        foreach (ProjectDocumentHandle mapHandle in context.Workspace.Documents.Where(handle => handle.Kind == MvDocumentKind.Map))
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonDocument map = await ReadJsonAsync(context, mapHandle, cancellationToken).ConfigureAwait(false);
            using (map)
            {
                ValidateMapData(map.RootElement, mapHandle.RelativePath, diagnostics);
            }
        }

        return diagnostics;
    }

    private static void AddPluginDiagnostics(
        ProjectValidationContext context,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        foreach (MvPluginEntry plugin in context.Workspace.Snapshot.Plugins.Entries)
        {
            if (!plugin.ScriptExists)
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Error(
                        DiagnosticCodes.Phase3MissingPluginFile,
                        $"Plugin file for '{plugin.Name}' is missing.",
                        new ProjectLocation(plugin.ScriptRelativePath ?? context.Workspace.Snapshot.Plugins.ManifestRelativePath)),
                    DocumentationKey: "phase3/missing-plugin"));
            }
            if (plugin.IsDuplicate)
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Warning(
                        DiagnosticCodes.Phase3DuplicatePluginEntry,
                        $"Plugin '{plugin.Name}' appears more than once. The duplicate is preserved intentionally.",
                        new ProjectLocation(context.Workspace.Snapshot.Plugins.ManifestRelativePath,
                            $"/plugins/{plugin.LoadOrder}")),
                    DocumentationKey: "phase3/duplicate-plugin"));
            }
        }
    }

    private static void AddPluginUsageDiagnostics(
        ProjectValidationContext context,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        foreach (ProjectReference reference in context.References.All.Where(item => item.Target.Kind == ReferenceTargetKind.Plugin))
        {
            string? name = reference.Target.Name;
            if (string.IsNullOrWhiteSpace(name) || !context.Catalog.Exists(reference.Target)) continue;
            if (context.Catalog.IsPluginEnabled(name)) continue;
            diagnostics.Add(new ProjectValidationDiagnostic(
                ProjectDiagnostic.Warning(
                    DiagnosticCodes.Phase3DisabledPluginReferenced,
                    $"Plugin command appears to reference disabled plugin '{name}'.",
                    new ProjectLocation(
                        reference.Source.RelativePath,
                        reference.Source.JsonPath,
                        reference.Source.MapId,
                        reference.Source.RecordId,
                        reference.Source.EventId,
                        reference.Source.PageIndex,
                        reference.Source.CommandIndex),
                    reference.Context),
                DocumentationKey: "phase3/disabled-plugin-reference"));
        }
    }

    private static void ValidateVehicle(
        JsonElement system,
        string property,
        string label,
        string relativePath,
        ProjectValidationContext context,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (!system.TryGetPropertyValue(property, out JsonElement vehicle)) return;
        ValidateStartingPosition(vehicle, "startMapId", "startX", "startY", label, relativePath, context, diagnostics, "/" + property);
    }

    private static void ValidateStartingPosition(
        JsonElement owner,
        string mapProperty,
        string xProperty,
        string yProperty,
        string label,
        string relativePath,
        ProjectValidationContext context,
        ICollection<ProjectValidationDiagnostic> diagnostics,
        string pathPrefix = "")
    {
        int mapId = owner.GetOptionalInt32(mapProperty) ?? 0;
        int x = owner.GetOptionalInt32(xProperty) ?? 0;
        int y = owner.GetOptionalInt32(yProperty) ?? 0;
        if (mapId <= 0) return;
        if (!context.Catalog.TryGetMapDimensions(mapId, out MapDimensions dimensions))
        {
            diagnostics.Add(new ProjectValidationDiagnostic(
                ProjectDiagnostic.Error(
                    DiagnosticCodes.Phase3InvalidStartingLocation,
                    $"{label} starting map {mapId} does not exist.",
                    new ProjectLocation(relativePath, pathPrefix + "/" + mapProperty, mapId)),
                DocumentationKey: "phase3/starting-location"));
            return;
        }
        if (x < 0 || x >= dimensions.Width || y < 0 || y >= dimensions.Height)
        {
            diagnostics.Add(new ProjectValidationDiagnostic(
                ProjectDiagnostic.Error(
                    DiagnosticCodes.Phase3InvalidStartingLocation,
                    $"{label} starting position ({x},{y}) is outside map {mapId} ({dimensions.Width}×{dimensions.Height}).",
                    new ProjectLocation(relativePath, pathPrefix + "/" + xProperty, mapId)),
                DocumentationKey: "phase3/starting-location"));
        }
    }

    private static void ValidateMapHierarchy(
        JsonElement root,
        string relativePath,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        Dictionary<int, int> parents = [];
        int index = 0;
        foreach (JsonElement entry in root.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object)
            {
                int id = entry.GetOptionalInt32("id") ?? index;
                int parentId = entry.GetOptionalInt32("parentId") ?? 0;
                parents[id] = parentId;
            }
            index++;
        }
        foreach ((int id, int parentId) in parents)
        {
            if (parentId > 0 && !parents.ContainsKey(parentId))
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Error(
                        DiagnosticCodes.Phase7InvalidParentMap,
                        $"Map {id} has missing parent map {parentId}.",
                        new ProjectLocation(relativePath, $"/{id}/parentId", MapId: id)),
                    DocumentationKey: "phase7/map-hierarchy"));
            }
            HashSet<int> visited = [];
            int current = id;
            while (current > 0 && parents.TryGetValue(current, out int next))
            {
                if (!visited.Add(current))
                {
                    diagnostics.Add(new ProjectValidationDiagnostic(
                        ProjectDiagnostic.Error(
                            DiagnosticCodes.Phase7MapHierarchyCycle,
                            $"Map hierarchy cycle detected at map {current}.",
                            new ProjectLocation(relativePath, $"/{id}/parentId", MapId: id)),
                        DocumentationKey: "phase7/map-hierarchy"));
                    break;
                }
                current = next;
            }
        }
    }

    private static void ValidateMapData(
        JsonElement map,
        string relativePath,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        int width = map.GetOptionalInt32("width") ?? 0;
        int height = map.GetOptionalInt32("height") ?? 0;
        if (!map.TryGetPropertyValue("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(new ProjectValidationDiagnostic(
                ProjectDiagnostic.Error(
                    DiagnosticCodes.Phase3MapDataLengthMismatch,
                    "Map data is missing or is not an array.",
                    new ProjectLocation(relativePath, "/data")),
                DocumentationKey: "phase3/map-data"));
            return;
        }
        long expected = (long)width * height * 6;
        int actual = data.GetArrayLength();
        if (width <= 0 || height <= 0 || actual != expected)
        {
            diagnostics.Add(new ProjectValidationDiagnostic(
                ProjectDiagnostic.Error(
                    DiagnosticCodes.Phase3MapDataLengthMismatch,
                    $"Map data length is {actual:N0}; expected {expected:N0} for {width}×{height}×6 layers.",
                    new ProjectLocation(relativePath, "/data")),
                DocumentationKey: "phase3/map-data"));
        }
        if (width <= 0 || height <= 0)
        {
            diagnostics.Add(new ProjectValidationDiagnostic(
                ProjectDiagnostic.Error(
                    DiagnosticCodes.Phase7InvalidMapDimensions,
                    $"Map dimensions {width}×{height} are invalid.",
                    new ProjectLocation(relativePath, "/width")),
                DocumentationKey: "phase7/map-dimensions"));
        }
        ValidateMapEvents(map, relativePath, width, height, diagnostics);
        ValidateMapSpecialLayers(data, relativePath, width, height, diagnostics);
        ValidateEncounterRegions(map, relativePath, diagnostics);
    }

    private static void ValidateMapEvents(
        JsonElement map,
        string relativePath,
        int width,
        int height,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (!map.TryGetPropertyValue("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array) return;
        HashSet<int> ids = [];
        int arrayIndex = 0;
        foreach (JsonElement mapEvent in events.EnumerateArray())
        {
            if (mapEvent.ValueKind != JsonValueKind.Object)
            {
                arrayIndex++;
                continue;
            }
            int id = mapEvent.GetOptionalInt32("id") ?? arrayIndex;
            int x = mapEvent.GetOptionalInt32("x") ?? 0;
            int y = mapEvent.GetOptionalInt32("y") ?? 0;
            if (!ids.Add(id))
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Error(
                        DiagnosticCodes.Phase7DuplicateEventId,
                        $"Map event ID {id} appears more than once.",
                        new ProjectLocation(relativePath, $"/events/{arrayIndex}/id", EventId: id)),
                    DocumentationKey: "phase7/event-id"));
            }
            if (id != arrayIndex)
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Warning(
                        DiagnosticCodes.Phase7EventIdIndexMismatch,
                        $"Map event at array index {arrayIndex} declares ID {id}.",
                        new ProjectLocation(relativePath, $"/events/{arrayIndex}/id", EventId: id)),
                    DocumentationKey: "phase7/event-id"));
            }
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Error(
                        DiagnosticCodes.Phase7EventOutOfBounds,
                        $"Map event {id} is outside the map at ({x},{y}).",
                        new ProjectLocation(relativePath, $"/events/{arrayIndex}", EventId: id)),
                    DocumentationKey: "phase7/event-bounds"));
            }
            arrayIndex++;
        }
    }

    private static void ValidateMapSpecialLayers(
        JsonElement data,
        string relativePath,
        int width,
        int height,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (width <= 0 || height <= 0 || data.ValueKind != JsonValueKind.Array) return;
        int cells = checked(width * height);
        int index = 0;
        foreach (JsonElement value in data.EnumerateArray())
        {
            if (!value.TryGetInt32(out int raw))
            {
                index++;
                continue;
            }
            int layer = index / cells;
            if (layer == 4 && (raw & ~0x0f) != 0)
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Warning(
                        DiagnosticCodes.Phase7UnexpectedShadowBits,
                        $"Shadow value {raw} contains bits outside the standard low four bits.",
                        new ProjectLocation(relativePath, $"/data/{index}")),
                    DocumentationKey: "phase7/shadows"));
            }
            if (layer == 5 && (raw < 0 || raw > 255))
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Warning(
                        DiagnosticCodes.Phase7InvalidRegionValue,
                        $"Region value {raw} is outside the standard 0–255 range.",
                        new ProjectLocation(relativePath, $"/data/{index}")),
                    DocumentationKey: "phase7/regions"));
            }
            index++;
        }
    }

    private static void ValidateEncounterRegions(
        JsonElement map,
        string relativePath,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        if (!map.TryGetPropertyValue("encounterList", out JsonElement encounters) || encounters.ValueKind != JsonValueKind.Array) return;
        int encounterIndex = 0;
        foreach (JsonElement encounter in encounters.EnumerateArray())
        {
            if (encounter.ValueKind == JsonValueKind.Object
                && encounter.TryGetPropertyValue("regionSet", out JsonElement regions)
                && regions.ValueKind == JsonValueKind.Array)
            {
                int regionIndex = 0;
                foreach (JsonElement region in regions.EnumerateArray())
                {
                    if (region.TryGetInt32(out int id) && (id < 1 || id > 255))
                    {
                        diagnostics.Add(new ProjectValidationDiagnostic(
                            ProjectDiagnostic.Warning(
                                DiagnosticCodes.Phase7InvalidEncounterRegion,
                                $"Encounter region {id} is outside 1–255.",
                                new ProjectLocation(relativePath, $"/encounterList/{encounterIndex}/regionSet/{regionIndex}")),
                            DocumentationKey: "phase7/encounters"));
                    }
                    regionIndex++;
                }
            }
            encounterIndex++;
        }
    }

    private static async ValueTask<JsonDocument> ReadJsonAsync(
        ProjectValidationContext context,
        ProjectDocumentHandle handle,
        CancellationToken cancellationToken)
    {
        IMutableProjectDocument document = await context.Workspace.OpenDocumentAsync(handle.RelativePath, cancellationToken).ConfigureAwait(false);
        ReadOnlyMemory<byte> bytes = await document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(Utf8JsonInput.ToBomlessArray(bytes.Span));
    }
}
