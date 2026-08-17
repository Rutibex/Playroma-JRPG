using System.Text.Json;
using Playroma.JRPG.Editor.WinForms.Eventing;
using Playroma.JRPG.Eventing;
using Playroma.Text.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Application;

internal sealed record TextStudioEventBinding(
    EventEditSession Session,
    TextRecordTargetDescriptor Target,
    MvGeneratedEventTextKind Kind,
    EventCommandSpan Span,
    int TargetIndent,
    bool ReplaceExisting,
    MvShowTextSettings ShowTextSettings,
    MvScrollingTextSettings ScrollingSettings,
    MvChoiceSettings ChoiceSettings,
    bool HasNonTextChoiceCommands)
{
    public void Apply(MvEventTextPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanApply) throw new InvalidOperationException("The generated event-text plan contains blocking validation issues.");
        if (ReplaceExisting)
            Session.ReplaceCommandSpan(Span, plan.RawCommands, $"Import generated {Kind} text");
        else
            Session.InsertCommands(Span.StartIndex, TargetIndent, plan.RawCommands, $"Insert generated {Kind} text");
    }
}

internal static class TextStudioEventIntegration
{
    public static TextStudioEventBinding CreateInsertion(
        EventEditSession session,
        MvGeneratedEventTextKind kind,
        int rawIndex,
        int indent)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Create(session, kind, new EventCommandSpan(rawIndex, 0), Math.Max(0, indent), false, null);
    }

    public static TextStudioEventBinding CreateReplacement(
        EventEditSession session,
        EventNode node,
        MvGeneratedEventTextKind kind)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(node);
        return Create(session, kind, node.OriginalSpan, Math.Max(0, node.OriginalIndent), true, node);
    }

    private static TextStudioEventBinding Create(
        EventEditSession session,
        MvGeneratedEventTextKind kind,
        EventCommandSpan span,
        int indent,
        bool replace,
        EventNode? node)
    {
        string adapter = "event." + kind.ToString().ToLowerInvariant();
        string current = node is null ? string.Empty : ExtractCurrent(node, kind);
        string semantic = kind == MvGeneratedEventTextKind.ShowChoices ? "choices" : "text";
        string display = kind switch
        {
            MvGeneratedEventTextKind.ShowText => "Show Text",
            MvGeneratedEventTextKind.ShowChoices => "Show Choices",
            MvGeneratedEventTextKind.ScrollingText => "Scrolling Text",
            MvGeneratedEventTextKind.Comment => "Comment",
            MvGeneratedEventTextKind.BranchingDialogue => "Branching Dialogue",
            _ => "Event Text"
        };
        string recipe = kind switch
        {
            MvGeneratedEventTextKind.ShowText => "phase9.mv.show-text",
            MvGeneratedEventTextKind.ShowChoices => "phase9.mv.choices",
            MvGeneratedEventTextKind.ScrollingText => "phase9.mv.scrolling-lore",
            MvGeneratedEventTextKind.Comment => "phase9.mv.comment",
            MvGeneratedEventTextKind.BranchingDialogue => "phase9.mv.branching-dialogue",
            _ => "phase9.mv.show-text"
        };
        string recordId = session.Surface switch
        {
            EventSurfaceKind.MapEvent => $"map:{session.MapId}:event:{session.OwnerId}:page:{session.ActivePageIndex}",
            EventSurfaceKind.CommonEvent => $"common:{session.OwnerId}",
            EventSurfaceKind.TroopEvent => $"troop:{session.OwnerId}:page:{session.ActivePageIndex}",
            _ => $"event:{session.OwnerId}"
        };
        var field = new TextTargetDescriptor(adapter, session.RelativePath, recordId, "/list", semantic, display, current,
            true, kind == MvGeneratedEventTextKind.Comment, null,
            new HashSet<TextImportMode>([TextImportMode.Replace]), semantic, session.Name, [recipe]);
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["event.surface"] = session.Surface.ToString(),
            ["event.ownerId"] = session.OwnerId,
            ["event.name"] = session.Name,
            ["event.pageIndex"] = session.ActivePageIndex,
            ["event.mapId"] = session.MapId,
            ["event.commandKind"] = kind.ToString(),
            ["event.commandIndent"] = indent,
            ["event.replaceExisting"] = replace,
            ["event.current"] = current,
            ["speaker.name"] = session.Name,
            ["voice.assignmentKey"] = recordId,
            ["event.defaultDialogueAct"] = DefaultAct(kind),
            ["target.kind"] = "EventCommand",
            ["target.current"] = current
        };
        var target = new TextRecordTargetDescriptor(adapter, session.RelativePath, recordId,
            $"{EventLabel(session)} — {display}", [field], semantic, context);
        (MvShowTextSettings showSettings, MvScrollingTextSettings scrollSettings, MvChoiceSettings choiceSettings) = ReadPresentation(node);
        bool hasNonTextChoiceCommands = kind == MvGeneratedEventTextKind.ShowChoices && node is not null && HasNonTextChoiceCommands(node);
        return new(session, target, kind, span, indent, replace, showSettings, scrollSettings, choiceSettings, hasNonTextChoiceCommands);
    }



    private static string DefaultAct(MvGeneratedEventTextKind kind) => kind switch
    {
        MvGeneratedEventTextKind.ShowChoices => "Question",
        MvGeneratedEventTextKind.BranchingDialogue => "Answer",
        MvGeneratedEventTextKind.ScrollingText => "Exposition",
        MvGeneratedEventTextKind.Comment => "Custom",
        _ => "SmallTalk"
    };

    private static bool HasNonTextChoiceCommands(EventNode node)
    {
        // A generated replacement may safely replace a pure text-choice block, but it must not
        // erase gameplay logic living inside existing branches.
        HashSet<int> allowed = [101, 102, 401, 402, 403, 404];
        return node.RawCommands.Any(command => !allowed.Contains(command.Code));
    }

    private static (MvShowTextSettings Show, MvScrollingTextSettings Scroll, MvChoiceSettings Choices) ReadPresentation(EventNode? node)
    {
        var show = new MvShowTextSettings();
        var scroll = new MvScrollingTextSettings();
        var choices = new MvChoiceSettings();
        RawEventCommand? opening = node?.RawCommands.FirstOrDefault();
        if (opening is null) return (show, scroll, choices);
        try
        {
            using JsonDocument json = JsonDocument.Parse(opening.ParametersRawJson);
            JsonElement p = json.RootElement;
            if (p.ValueKind != JsonValueKind.Array) return (show, scroll, choices);
            if (opening.Code == 101)
                show = new(GetString(p, 0), GetInt(p, 1, 0), GetInt(p, 2, 0), GetInt(p, 3, 2));
            else if (opening.Code == 105)
                scroll = new(GetInt(p, 0, 2), GetBool(p, 1, false));
            else if (opening.Code == 102)
                choices = new(GetInt(p, 1, -2), GetInt(p, 2, 0), GetInt(p, 3, 2), GetInt(p, 4, 0));
        }
        catch (JsonException) { }
        return (show, scroll, choices);
    }

    private static string GetString(JsonElement array, int index) => index >= 0 && index < array.GetArrayLength() && array[index].ValueKind == JsonValueKind.String ? array[index].GetString() ?? string.Empty : string.Empty;
    private static int GetInt(JsonElement array, int index, int fallback) => index >= 0 && index < array.GetArrayLength() && array[index].TryGetInt32(out int value) ? value : fallback;
    private static bool GetBool(JsonElement array, int index, bool fallback) => index >= 0 && index < array.GetArrayLength() && array[index].ValueKind is JsonValueKind.True or JsonValueKind.False ? array[index].GetBoolean() : fallback;

    public static bool TryKindForNode(EventNode? node, out MvGeneratedEventTextKind kind)
    {
        int code = node?.RawCommands.FirstOrDefault()?.Code ?? -1;
        kind = code switch
        {
            101 => MvGeneratedEventTextKind.ShowText,
            102 => MvGeneratedEventTextKind.ShowChoices,
            105 => MvGeneratedEventTextKind.ScrollingText,
            108 => MvGeneratedEventTextKind.Comment,
            _ => MvGeneratedEventTextKind.ShowText
        };
        return code is 101 or 102 or 105 or 108;
    }

    private static string ExtractCurrent(EventNode node, MvGeneratedEventTextKind kind) => kind switch
    {
        MvGeneratedEventTextKind.ShowText => string.Join("\n", node.RawCommands.Where(x => x.Code == 401).Select(FirstStringParameter)),
        MvGeneratedEventTextKind.ScrollingText => string.Join("\n", node.RawCommands.Where(x => x.Code == 405).Select(FirstStringParameter)),
        MvGeneratedEventTextKind.Comment => string.Join("\n", node.RawCommands.Where(x => x.Code is 108 or 408).Select(FirstStringParameter)),
        MvGeneratedEventTextKind.ShowChoices => ExtractChoices(node),
        _ => string.Empty
    };

    private static string ExtractChoices(EventNode node)
    {
        RawEventCommand? opening = node.RawCommands.FirstOrDefault(x => x.Code == 102);
        if (opening is null) return string.Empty;
        try
        {
            using JsonDocument json = JsonDocument.Parse(opening.ParametersRawJson);
            if (json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.GetArrayLength() > 0
                && json.RootElement[0].ValueKind == JsonValueKind.Array)
                return string.Join("\n", json.RootElement[0].EnumerateArray().Select(x => x.GetString() ?? string.Empty));
        }
        catch (JsonException) { }
        return string.Empty;
    }

    private static string FirstStringParameter(RawEventCommand command)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(command.ParametersRawJson);
            return json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.GetArrayLength() > 0
                ? json.RootElement[0].GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException) { return string.Empty; }
    }

    private static string EventLabel(EventEditSession session) => session.Surface switch
    {
        EventSurfaceKind.MapEvent => $"Map {(session.MapId ?? 0):D3} / Event {session.OwnerId:D3} / Page {session.ActivePageIndex + 1}",
        EventSurfaceKind.CommonEvent => $"Common Event {session.OwnerId:D4}",
        EventSurfaceKind.TroopEvent => $"Troop {session.OwnerId:D4} / Page {session.ActivePageIndex + 1}",
        _ => "Event"
    };
}
