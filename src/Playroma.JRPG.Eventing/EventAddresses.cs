using Playroma.JRPG.Documents.Json;

namespace Playroma.JRPG.Eventing;

public enum EventSurfaceKind
{
    MapEvent,
    CommonEvent,
    TroopEvent
}

public sealed record EventListAddress(
    EventSurfaceKind Surface,
    string RelativePath,
    JsonPath ListPath,
    int? MapId = null,
    int? EventId = null,
    int? RecordId = null,
    int? PageIndex = null)
{
    public string NormalizedRelativePath { get; } = Normalize(RelativePath);

    public string DisplayName => Surface switch
    {
        EventSurfaceKind.MapEvent => $"Map {MapId ?? 0:D3} / Event {EventId ?? 0:D3} / Page {(PageIndex ?? 0) + 1}",
        EventSurfaceKind.CommonEvent => $"Common Event {RecordId ?? 0:D4}",
        EventSurfaceKind.TroopEvent => $"Troop {RecordId ?? 0:D4} / Page {(PageIndex ?? 0) + 1}",
        _ => ListPath.ToString()
    };

    public static EventListAddress ForMapEvent(
        string relativePath,
        int mapId,
        int eventId,
        int pageIndex) =>
        new(
            EventSurfaceKind.MapEvent,
            Normalize(relativePath),
            JsonPath.Root.Append("events").Append(eventId).Append("pages").Append(pageIndex).Append("list"),
            mapId,
            eventId,
            null,
            pageIndex);

    public static EventListAddress ForCommonEvent(
        string relativePath,
        int commonEventId) =>
        new(
            EventSurfaceKind.CommonEvent,
            Normalize(relativePath),
            JsonPath.Root.Append(commonEventId).Append("list"),
            null,
            null,
            commonEventId,
            null);

    public static EventListAddress ForTroopEvent(
        string relativePath,
        int troopId,
        int pageIndex) =>
        new(
            EventSurfaceKind.TroopEvent,
            Normalize(relativePath),
            JsonPath.Root.Append(troopId).Append("pages").Append(pageIndex).Append("list"),
            null,
            null,
            troopId,
            pageIndex);

    public JsonPath OwnerPath => Surface switch
    {
        EventSurfaceKind.MapEvent => JsonPath.Root.Append("events").Append(EventId ?? 0).Append("pages").Append(PageIndex ?? 0),
        EventSurfaceKind.CommonEvent => JsonPath.Root.Append(RecordId ?? 0),
        EventSurfaceKind.TroopEvent => JsonPath.Root.Append(RecordId ?? 0).Append("pages").Append(PageIndex ?? 0),
        _ => JsonPath.Root
    };

    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Replace('\\', '/');
    }
}

public readonly record struct EventCommandSpan(int StartIndex, int Length)
{
    public int EndExclusive => checked(StartIndex + Length);
    public bool IsEmpty => Length == 0;

    public bool Contains(int rawIndex) => rawIndex >= StartIndex && rawIndex < EndExclusive;

    public static EventCommandSpan FromBounds(int startInclusive, int endExclusive)
    {
        if (startInclusive < 0) throw new ArgumentOutOfRangeException(nameof(startInclusive));
        if (endExclusive < startInclusive) throw new ArgumentOutOfRangeException(nameof(endExclusive));
        return new EventCommandSpan(startInclusive, endExclusive - startInclusive);
    }
}
