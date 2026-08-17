using System.Text.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.Eventing;

public sealed record EventListDescriptor(
    EventListAddress Address,
    string Name,
    int CommandCount,
    int PageCount,
    int OwnerId,
    int PageIndex);

public static class EventSurfaceCatalog
{
    public static IReadOnlyList<EventListDescriptor> Scan(MutableJsonProjectDocument document, int? mapId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ReadOnlyMemory<byte> bytes = document.GenerateBytesAsync().AsTask().GetAwaiter().GetResult();
        using JsonDocument json = JsonDocument.Parse(bytes);
        return document.Kind switch
        {
            Playroma.JRPG.RpgMakerMV.MvDocumentKind.Map => ScanMap(document.RelativePath, mapId ?? ParseMapId(document.RelativePath), json.RootElement),
            Playroma.JRPG.RpgMakerMV.MvDocumentKind.CommonEvents => ScanCommon(document.RelativePath, json.RootElement),
            Playroma.JRPG.RpgMakerMV.MvDocumentKind.Troops => ScanTroops(document.RelativePath, json.RootElement),
            _ => []
        };
    }

    private static IReadOnlyList<EventListDescriptor> ScanMap(string relativePath, int mapId, JsonElement root)
    {
        List<EventListDescriptor> result = [];
        if (!root.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array) return result;
        for (int eventId = 1; eventId < events.GetArrayLength(); eventId++)
        {
            JsonElement mapEvent = events[eventId];
            if (mapEvent.ValueKind != JsonValueKind.Object) continue;
            string name = mapEvent.TryGetProperty("name", out JsonElement nameNode) && nameNode.ValueKind == JsonValueKind.String
                ? nameNode.GetString() ?? $"EV{eventId:D3}" : $"EV{eventId:D3}";
            if (!mapEvent.TryGetProperty("pages", out JsonElement pages) || pages.ValueKind != JsonValueKind.Array) continue;
            for (int page = 0; page < pages.GetArrayLength(); page++)
            {
                JsonElement pageNode = pages[page];
                int count = pageNode.ValueKind == JsonValueKind.Object
                    && pageNode.TryGetProperty("list", out JsonElement list)
                    && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;
                result.Add(new EventListDescriptor(
                    EventListAddress.ForMapEvent(relativePath, mapId, eventId, page),
                    $"{eventId:D3} {name} — Page {page + 1}", count, pages.GetArrayLength(), eventId, page));
            }
        }
        return result;
    }

    private static IReadOnlyList<EventListDescriptor> ScanCommon(string relativePath, JsonElement root)
    {
        List<EventListDescriptor> result = [];
        if (root.ValueKind != JsonValueKind.Array) return result;
        for (int id = 1; id < root.GetArrayLength(); id++)
        {
            JsonElement item = root[id];
            if (item.ValueKind != JsonValueKind.Object) continue;
            string name = item.TryGetProperty("name", out JsonElement nameNode) ? nameNode.GetString() ?? string.Empty : string.Empty;
            int count = item.TryGetProperty("list", out JsonElement list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;
            result.Add(new EventListDescriptor(EventListAddress.ForCommonEvent(relativePath, id), $"{id:D4} {name}", count, 1, id, 0));
        }
        return result;
    }

    private static IReadOnlyList<EventListDescriptor> ScanTroops(string relativePath, JsonElement root)
    {
        List<EventListDescriptor> result = [];
        if (root.ValueKind != JsonValueKind.Array) return result;
        for (int id = 1; id < root.GetArrayLength(); id++)
        {
            JsonElement item = root[id];
            if (item.ValueKind != JsonValueKind.Object) continue;
            string name = item.TryGetProperty("name", out JsonElement nameNode) ? nameNode.GetString() ?? string.Empty : string.Empty;
            if (!item.TryGetProperty("pages", out JsonElement pages) || pages.ValueKind != JsonValueKind.Array) continue;
            for (int page = 0; page < pages.GetArrayLength(); page++)
            {
                JsonElement pageNode = pages[page];
                int count = pageNode.ValueKind == JsonValueKind.Object
                    && pageNode.TryGetProperty("list", out JsonElement list)
                    && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;
                result.Add(new EventListDescriptor(EventListAddress.ForTroopEvent(relativePath, id, page),
                    $"{id:D4} {name} — Page {page + 1}", count, pages.GetArrayLength(), id, page));
            }
        }
        return result;
    }

    private static int ParseMapId(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("Map", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(3), out int id) ? id : 0;
    }
}
