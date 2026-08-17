using System.Globalization;
using System.Text.Json;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Rendering;

public static class MvRenderSnapshotParser
{
    public static MapRenderSnapshot ParseMap(
        int mapId,
        string editorName,
        string relativePath,
        ReadOnlyMemory<byte> bytes)
    {
        List<ProjectDiagnostic> diagnostics = [];
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The MV map root is not a JSON object.");
        int width = RequiredInt(root, "width");
        int height = RequiredInt(root, "height");
        int tilesetId = OptionalInt(root, "tilesetId", 0);
        int scrollType = OptionalInt(root, "scrollType", 0);
        int cells = checked(width * height);
        int[] data = ParseData(root, cells, relativePath, mapId, diagnostics);
        IReadOnlyList<MapEventRenderSnapshot> events = ParseEvents(root, relativePath, mapId, diagnostics);
        ParallaxRenderSnapshot parallax = ParseParallax(root);
        return new MapRenderSnapshot(
            mapId,
            editorName,
            relativePath,
            width,
            height,
            tilesetId,
            scrollType,
            data,
            events,
            parallax,
            diagnostics);
    }

    public static TilesetRenderSnapshot ParseTileset(
        int tilesetId,
        string relativePath,
        ReadOnlyMemory<byte> bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Tilesets.json is not a JSON array.");
        if (tilesetId <= 0 || tilesetId >= root.GetArrayLength())
        {
            ProjectDiagnostic diagnostic = ProjectDiagnostic.Error(
                DiagnosticCodes.Phase6TilesetMissing,
                $"Tileset {tilesetId.ToString(CultureInfo.InvariantCulture)} is outside the Tilesets.json array.",
                new ProjectLocation(relativePath, "/" + tilesetId.ToString(CultureInfo.InvariantCulture), RecordId: tilesetId));
            return new TilesetRenderSnapshot(tilesetId, $"Missing tileset {tilesetId}", 1, relativePath, new string[9], new int[MvTileRules.TileIdMax], [diagnostic]);
        }

        JsonElement record = root[tilesetId];
        if (record.ValueKind != JsonValueKind.Object)
        {
            ProjectDiagnostic diagnostic = ProjectDiagnostic.Error(
                DiagnosticCodes.Phase6TilesetMissing,
                $"Tileset slot {tilesetId.ToString(CultureInfo.InvariantCulture)} is empty.",
                new ProjectLocation(relativePath, "/" + tilesetId.ToString(CultureInfo.InvariantCulture), RecordId: tilesetId));
            return new TilesetRenderSnapshot(tilesetId, $"Missing tileset {tilesetId}", 1, relativePath, new string[9], new int[MvTileRules.TileIdMax], [diagnostic]);
        }

        string[] names = new string[9];
        if (record.TryGetProperty("tilesetNames", out JsonElement namesElement) && namesElement.ValueKind == JsonValueKind.Array)
        {
            int count = Math.Min(names.Length, namesElement.GetArrayLength());
            for (int index = 0; index < count; index++) names[index] = namesElement[index].ValueKind == JsonValueKind.String ? namesElement[index].GetString() ?? string.Empty : string.Empty;
        }

        int[] flags = new int[MvTileRules.TileIdMax];
        if (record.TryGetProperty("flags", out JsonElement flagsElement) && flagsElement.ValueKind == JsonValueKind.Array)
        {
            int count = Math.Min(flags.Length, flagsElement.GetArrayLength());
            for (int index = 0; index < count; index++)
            {
                if (flagsElement[index].TryGetInt32(out int value)) flags[index] = value;
            }
        }

        return new TilesetRenderSnapshot(
            OptionalInt(record, "id", tilesetId),
            OptionalString(record, "name"),
            OptionalInt(record, "mode", 1),
            relativePath,
            names,
            flags);
    }

    private static int[] ParseData(
        JsonElement root,
        int cellsPerLayer,
        string relativePath,
        int mapId,
        List<ProjectDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("data", out JsonElement dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The map data property is not an array.");
        int actualLength = dataElement.GetArrayLength();
        int requiredLength = checked(cellsPerLayer * 6);
        if (actualLength < requiredLength)
        {
            throw new InvalidDataException($"The map data array has {actualLength.ToString(CultureInfo.InvariantCulture)} entries; at least {requiredLength.ToString(CultureInfo.InvariantCulture)} are required.");
        }
        if (actualLength % cellsPerLayer != 0)
        {
            diagnostics.Add(ProjectDiagnostic.Warning(
                DiagnosticCodes.Phase6MapLayerLength,
                "The map data length is not an exact multiple of width × height. Extra values are preserved but not rendered.",
                new ProjectLocation(relativePath, "/data", MapId: mapId),
                $"Length={actualLength}; cellsPerLayer={cellsPerLayer}"));
        }

        int[] values = new int[actualLength];
        int index = 0;
        foreach (JsonElement value in dataElement.EnumerateArray())
        {
            if (!value.TryGetInt32(out int parsed))
            {
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.Phase6InvalidTileValue,
                    $"Map data element {index.ToString(CultureInfo.InvariantCulture)} is not a 32-bit integer and will render as zero.",
                    new ProjectLocation(relativePath, "/data/" + index.ToString(CultureInfo.InvariantCulture), MapId: mapId)));
                parsed = 0;
            }
            values[index] = parsed;
            int layer = index / cellsPerLayer;
            if (layer == 4 && (parsed & ~0x0f) != 0)
            {
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.Phase6ShadowHighBits,
                    "A shadow cell contains bits outside MV's low four shadow bits. Playroma renders the low four bits and preserves the original integer.",
                    new ProjectLocation(relativePath, "/data/" + index.ToString(CultureInfo.InvariantCulture), MapId: mapId),
                    "Raw shadow value: 0x" + unchecked((uint)parsed).ToString("X8", CultureInfo.InvariantCulture)));
            }
            index++;
        }
        return values;
    }

    private static IReadOnlyList<MapEventRenderSnapshot> ParseEvents(
        JsonElement root,
        string relativePath,
        int mapId,
        List<ProjectDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("events", out JsonElement eventsElement) || eventsElement.ValueKind != JsonValueKind.Array) return [];
        List<MapEventRenderSnapshot> events = [];
        int slot = 0;
        foreach (JsonElement eventElement in eventsElement.EnumerateArray())
        {
            if (eventElement.ValueKind != JsonValueKind.Object) { slot++; continue; }
            int id = OptionalInt(eventElement, "id", slot);
            int x = OptionalInt(eventElement, "x", 0);
            int y = OptionalInt(eventElement, "y", 0);
            string name = OptionalString(eventElement, "name");
            int selectedPageIndex = 0;
            JsonElement selectedPage = default;
            if (eventElement.TryGetProperty("pages", out JsonElement pagesElement) && pagesElement.ValueKind == JsonValueKind.Array)
            {
                for (int pageIndex = 0; pageIndex < pagesElement.GetArrayLength(); pageIndex++)
                {
                    if (pagesElement[pageIndex].ValueKind != JsonValueKind.Object) continue;
                    selectedPage = pagesElement[pageIndex];
                    selectedPageIndex = pageIndex;
                    break;
                }
            }
            if (selectedPage.ValueKind != JsonValueKind.Object)
            {
                events.Add(new MapEventRenderSnapshot(id, name, x, y, 1, -1, EventGraphicSnapshot.Empty));
                slot++;
                continue;
            }

            int priority = OptionalInt(selectedPage, "priorityType", 1);
            EventGraphicSnapshot graphic = EventGraphicSnapshot.Empty;
            if (selectedPage.TryGetProperty("image", out JsonElement image) && image.ValueKind == JsonValueKind.Object)
            {
                graphic = new EventGraphicSnapshot(
                    OptionalInt(image, "tileId", 0),
                    OptionalString(image, "characterName"),
                    Math.Clamp(OptionalInt(image, "characterIndex", 0), 0, 7),
                    NormalizeDirection(OptionalInt(image, "direction", 2)),
                    Math.Clamp(OptionalInt(image, "pattern", 1), 0, 2));
            }
            if (graphic.TileId >= MvTileRules.TileIdMax)
            {
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.Phase6InvalidTileValue,
                    $"Event {id.ToString(CultureInfo.InvariantCulture)} uses out-of-range tile ID {graphic.TileId.ToString(CultureInfo.InvariantCulture)}.",
                    new ProjectLocation(relativePath, $"/events/{slot}/pages/{selectedPageIndex}/image/tileId", mapId, EventId: id, PageIndex: selectedPageIndex)));
            }
            events.Add(new MapEventRenderSnapshot(id, name, x, y, priority, selectedPageIndex, graphic));
            slot++;
        }
        return events;
    }

    private static ParallaxRenderSnapshot ParseParallax(JsonElement root)
    {
        string name = OptionalString(root, "parallaxName");
        return new ParallaxRenderSnapshot(
            name,
            OptionalBool(root, "parallaxLoopX", false),
            OptionalBool(root, "parallaxLoopY", false),
            OptionalInt(root, "parallaxSx", 0),
            OptionalInt(root, "parallaxSy", 0),
            OptionalBool(root, "parallaxShow", true),
            name.StartsWith('!'));
    }

    private static int RequiredInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement property) || !property.TryGetInt32(out int value)) throw new InvalidDataException($"Required integer property '{name}' is missing or invalid.");
        return value;
    }
    private static int OptionalInt(JsonElement element, string name, int fallback) => element.TryGetProperty(name, out JsonElement property) && property.TryGetInt32(out int value) ? value : fallback;
    private static string OptionalString(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : string.Empty;
    private static bool OptionalBool(JsonElement element, string name, bool fallback) => element.TryGetProperty(name, out JsonElement property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.GetBoolean() : fallback;
    private static int NormalizeDirection(int direction) => direction is 2 or 4 or 6 or 8 ? direction : 2;
}
