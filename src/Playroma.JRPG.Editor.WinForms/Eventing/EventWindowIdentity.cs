using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing;

/// <summary>Centralizes stable event-window keys and document-address conversion.</summary>
public static class EventWindowIdentity
{
    public static string Create(DocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.EventSurface switch
        {
            "CommonEvent" => $"event:common:{request.RecordId ?? 1}",
            "TroopEvent" => $"event:troop:{request.RecordId ?? 1}",
            _ => $"event:map:{request.MapId ?? 0}:{request.EventId ?? 1}"
        };
    }

    public static EventListAddress CreateAddress(DocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string relativePath = request.RelativePath
            ?? throw new InvalidOperationException("An event document path is required.");
        return request.EventSurface switch
        {
            "CommonEvent" => EventListAddress.ForCommonEvent(relativePath, request.RecordId ?? 1),
            "TroopEvent" => EventListAddress.ForTroopEvent(
                relativePath,
                request.RecordId ?? 1,
                request.EventPageIndex ?? 0),
            _ => EventListAddress.ForMapEvent(
                relativePath,
                request.MapId ?? 0,
                request.EventId ?? 1,
                request.EventPageIndex ?? 0)
        };
    }

    public static DocumentRequest WithAddress(DocumentRequest request, EventListAddress address) =>
        request with
        {
            Identity = Create(request),
            RelativePath = address.NormalizedRelativePath,
            MapId = address.MapId,
            RecordId = address.RecordId,
            EventId = address.EventId,
            EventPageIndex = address.PageIndex
        };
}
