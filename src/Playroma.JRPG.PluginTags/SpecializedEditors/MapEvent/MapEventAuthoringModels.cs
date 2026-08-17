namespace Playroma.JRPG.PluginTags;

public enum MapEventAuthoringArea
{
    MapBehavior,
    RegionRules,
    Footsteps,
    Battlebacks,
    EnemyLevels,
    Vehicles,
    TilesetBehavior,
    EventBehavior,
    EventAppearance,
    EventActivation,
    EventPageMetadata,
    PluginCommands,
    MovementScripts,
    Advanced
}

public sealed record MapEventFeatureQuery(
    string? SearchText = null,
    string? SpecializedEditorId = null,
    bool CurrentOnly = false,
    bool InstalledOnly = true,
    bool IncludeAdvanced = false,
    MapEventAuthoringArea? Area = null);

public sealed record MapEventFeatureItem(
    PluginFeatureDefinition Definition,
    MapEventAuthoringArea Area,
    int OccurrenceCount,
    PluginFeatureAvailability Availability)
{
    public bool IsPresent => OccurrenceCount > 0;
    public bool CanAuthor => Availability.CanAuthor;
}

/// <summary>
/// Shared platform-neutral workspace for map notes, tileset notes, event notes,
/// event-page comments, plugin commands, and movement-route script helpers.
/// </summary>
public sealed class MapEventAuthoringWorkspace
{
    private readonly PluginTagCatalog catalog;
    private readonly Func<PluginFeatureDefinition, MapEventAuthoringArea?> classifier;
    private readonly Dictionary<PluginFeatureKey, PluginFeatureAvailability> availability = new();

    public MapEventAuthoringWorkspace(
        PluginTagCatalog catalog,
        PluginTextEditingSession session,
        Func<PluginFeatureDefinition, MapEventAuthoringArea?> classifier,
        IEnumerable<PluginFeatureAvailability>? featureAvailability = null)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        this.classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        SetAvailability(featureAvailability);
    }

    public PluginTextEditingSession Session { get; }

    public void SetAvailability(IEnumerable<PluginFeatureAvailability>? values)
    {
        availability.Clear();
        if (values is null) return;
        foreach (PluginFeatureAvailability value in values) availability[value.Feature] = value;
    }

    public IReadOnlyList<MapEventFeatureItem> Query(MapEventFeatureQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        List<MapEventFeatureItem> result = [];
        foreach (PluginFeatureDefinition definition in catalog.Query(Session.Context, query.SearchText))
        {
            if (!string.IsNullOrWhiteSpace(query.SpecializedEditorId) &&
                !string.Equals(definition.SpecializedEditorId, query.SpecializedEditorId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!query.IncludeAdvanced && definition.Complexity == PluginFeatureComplexity.AdvancedCode)
                continue;

            int occurrenceCount = Session.ParsedDocument.Find(definition.Key).Count;
            if (query.CurrentOnly && occurrenceCount == 0) continue;

            PluginFeatureAvailability state = ResolveAvailability(definition);
            if (query.InstalledOnly && state.State == PluginFeatureAvailabilityState.OwnerPluginMissing)
                continue;

            MapEventAuthoringArea area = classifier(definition) ?? MapEventAuthoringArea.Advanced;
            if (query.Area.HasValue && query.Area.Value != area) continue;
            result.Add(new(definition, area, occurrenceCount, state));
        }

        return result
            .OrderByDescending(item => item.IsPresent)
            .ThenBy(item => item.Area)
            .ThenBy(item => item.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public PluginTagChangePreview PreviewWrite(PluginTagWriteRequest request) =>
        new PluginTagAuthoringWorkspace(catalog, Session, availability.Values).PreviewWrite(request);

    public PluginTagChangePreview PreviewRemove(PluginFeatureKey key, int occurrenceIndex = 0, bool removeAll = false) =>
        new PluginTagAuthoringWorkspace(catalog, Session, availability.Values).PreviewRemove(key, occurrenceIndex, removeAll);

    private PluginFeatureAvailability ResolveAvailability(PluginFeatureDefinition definition) =>
        availability.TryGetValue(definition.Key, out PluginFeatureAvailability? found)
            ? found
            : new(definition.Key, PluginFeatureAvailabilityState.Available,
                "Installed-plugin analysis has not reported a blocking issue.", []);
}

/// <summary>Minimal command representation used without depending on the event-editor assembly.</summary>
public sealed record PluginEventCommandLine(int Code, int Indent, string Text);

public sealed record EventPageCommentBlock(
    int StartIndex,
    int Length,
    int Indent,
    string Text)
{
    public bool Exists => Length > 0;
}

/// <summary>
/// Reads and replaces the leading RPG Maker MV comment block. YEP comment tags are
/// intentionally kept in code 108/408 commands instead of an event note field.
/// </summary>
public static class EventPageCommentSynchronizer
{
    public static EventPageCommentBlock ExtractLeading(IReadOnlyList<PluginEventCommandLine> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count == 0 || commands[0].Code != 108)
            return new(0, 0, commands.Count == 0 ? 0 : commands[0].Indent, string.Empty);

        int indent = commands[0].Indent;
        List<string> lines = [commands[0].Text];
        int length = 1;
        while (length < commands.Count && commands[length].Code == 408 && commands[length].Indent == indent)
        {
            lines.Add(commands[length].Text);
            length++;
        }
        return new(0, length, indent, string.Join("\n", lines));
    }

    public static IReadOnlyList<PluginEventCommandLine> BuildCommentRows(string? text, int indent)
    {
        string normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (normalized.Length == 0) return [];
        string[] lines = normalized.Split('\n');
        List<PluginEventCommandLine> result = new(lines.Length);
        for (int index = 0; index < lines.Length; index++)
            result.Add(new(index == 0 ? 108 : 408, indent, lines[index]));
        return result;
    }

    public static IReadOnlyList<PluginEventCommandLine> ReplaceLeading(
        IReadOnlyList<PluginEventCommandLine> commands,
        string? commentText)
    {
        ArgumentNullException.ThrowIfNull(commands);
        EventPageCommentBlock existing = ExtractLeading(commands);
        int indent = existing.Exists ? existing.Indent : commands.FirstOrDefault()?.Indent ?? 0;
        List<PluginEventCommandLine> result = [];
        result.AddRange(BuildCommentRows(commentText, indent));
        result.AddRange(commands.Skip(existing.Length));
        return result;
    }
}

public sealed record PluginCommandRenderResult(
    PluginFeatureDefinition Definition,
    string CommandLine,
    PluginFeatureValues Values);

public static class PluginCommandLineBuilder
{
    public static PluginCommandRenderResult Render(
        PluginFeatureDefinition definition,
        PluginFeatureValues values)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);
        if (definition.Surface != PluginAuthoringSurface.PluginCommand)
            throw new ArgumentException("The selected feature is not a plugin command.", nameof(definition));
        string rendered = new PluginTextRenderer()
            .RenderFeature(definition, values, "\n")
            .TrimEnd('\r', '\n');
        return new(definition, rendered, values);
    }
}

public static class MoveRouteScriptBuilder
{
    public static string Render(PluginFeatureDefinition definition, PluginFeatureValues values)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);
        if (definition.Surface != PluginAuthoringSurface.MoveRouteScript)
            throw new ArgumentException("The selected feature is not a movement-route script helper.", nameof(definition));
        return new PluginTextRenderer().RenderFeature(definition, values, "\n").TrimEnd('\r', '\n');
    }
}
