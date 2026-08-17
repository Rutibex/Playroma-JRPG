using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public static class YanflyMapEventFeatureDefinitions
{
    public const string MapTagsEditorId = "yep.map-tags";
    public const string RegionRulesEditorId = "yep.region-rules";
    public const string TilesetTagsEditorId = "yep.tileset-tags";
    public const string EventTagsEditorId = "yep.event-tags";
    public const string EventCommentEditorId = "yep.event-page-comments";
    public const string PluginCommandEditorId = "yep.plugin-command-builder";
    public const string MoveRouteEditorId = "yep.move-route-builder";

    private static readonly IReadOnlySet<string> TrueEventNotePlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "YEP_CommonEventMenu",
        "YEP_EventEncounterAid",
        "YEP_EventSpriteOffset",
        "YEP_EventStepAniOpt",
        "YEP_MoveRouteCore",
        "YEP_X_MenuActorEvents"
    };

    public static IEnumerable<PluginFeatureDefinition> ApplyCorrections(IEnumerable<PluginFeatureDefinition> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        foreach (PluginFeatureDefinition feature in features)
        {
            string? editorId = EditorFor(feature);
            if (editorId is null)
            {
                yield return feature;
                continue;
            }

            MapEventAuthoringArea area = Classify(feature) ?? MapEventAuthoringArea.Advanced;
            yield return feature with
            {
                SpecializedEditorId = editorId,
                SearchTerms = feature.EffectiveSearchTerms
                    .Concat(["maps tilesets events comments commands", FriendlyArea(area), FriendlyEditor(editorId), "phase 10"])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }
    }

    public static bool IsPhase10Feature(PluginFeatureDefinition feature) => EditorFor(feature) is not null;

    public static bool IsActionablePluginCommand(PluginFeatureDefinition feature)
    {
        if (feature.Surface != PluginAuthoringSurface.PluginCommand) return false;
        string normalized = feature.Syntax.CanonicalTemplate.Trim().TrimEnd(':').Trim();
        return !normalized.Equals("Plugin Command", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Equals("Plugin Commands", StringComparison.OrdinalIgnoreCase);
    }

    public static string? EditorFor(PluginFeatureDefinition feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.Surface switch
        {
            PluginAuthoringSurface.MapNote => IsRegionFeature(feature) ? RegionRulesEditorId : MapTagsEditorId,
            PluginAuthoringSurface.TilesetNote => TilesetTagsEditorId,
            PluginAuthoringSurface.EventNote when TrueEventNotePlugins.Contains(feature.PluginId) => EventTagsEditorId,
            PluginAuthoringSurface.EventPageComment => EventCommentEditorId,
            PluginAuthoringSurface.PluginCommand when IsActionablePluginCommand(feature) => PluginCommandEditorId,
            PluginAuthoringSurface.MoveRouteScript => MoveRouteEditorId,
            _ => null
        };
    }

    public static MapEventAuthoringArea? Classify(PluginFeatureDefinition feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (EditorFor(feature) is null) return null;
        string text = string.Join(" ", feature.PluginId, feature.DisplayName, feature.Summary,
            feature.Syntax.CanonicalTemplate, string.Join(" ", feature.EffectiveSearchTerms)).ToLowerInvariant();

        if (feature.Complexity == PluginFeatureComplexity.AdvancedCode) return MapEventAuthoringArea.Advanced;
        if (feature.Surface == PluginAuthoringSurface.PluginCommand) return MapEventAuthoringArea.PluginCommands;
        if (feature.Surface == PluginAuthoringSurface.MoveRouteScript) return MapEventAuthoringArea.MovementScripts;
        if (feature.Surface == PluginAuthoringSurface.EventPageComment)
        {
            if (text.Contains("activation") || text.Contains("click trigger") || text.Contains("region trigger"))
                return MapEventAuthoringArea.EventActivation;
            if (text.Contains("sprite") || text.Contains("icon") || text.Contains("label") || text.Contains("hitbox"))
                return MapEventAuthoringArea.EventAppearance;
            return MapEventAuthoringArea.EventPageMetadata;
        }
        if (feature.Surface == PluginAuthoringSurface.EventNote)
        {
            if (text.Contains("sprite") || text.Contains("step animation")) return MapEventAuthoringArea.EventAppearance;
            return MapEventAuthoringArea.EventBehavior;
        }
        if (feature.Surface == PluginAuthoringSurface.TilesetNote) return MapEventAuthoringArea.TilesetBehavior;
        if (text.Contains("footstep") || text.Contains("terrain tag")) return MapEventAuthoringArea.Footsteps;
        if (text.Contains("battleback")) return MapEventAuthoringArea.Battlebacks;
        if (text.Contains("enemy level")) return MapEventAuthoringArea.EnemyLevels;
        if (text.Contains("vehicle") || text.Contains("airship") || text.Contains("boat") || text.Contains("ship "))
            return MapEventAuthoringArea.Vehicles;
        if (text.Contains("region") || text.Contains("stealth")) return MapEventAuthoringArea.RegionRules;
        return MapEventAuthoringArea.MapBehavior;
    }

    public static IReadOnlyList<PluginFeatureDefinition> FeaturesFor(
        PluginTagCatalog catalog,
        string editorId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Features
            .Where(feature => string.Equals(feature.SpecializedEditorId, editorId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(feature => Classify(feature))
            .ThenBy(feature => feature.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void RegisterSpecializedEditors(PluginSpecializedEditorRegistry registry, PluginTagCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(catalog);
        Register(registry, catalog, MapTagsEditorId, "Map Plugin Tags", new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.MapNote },
            "Map-level YEP behavior, event-copying, enemy levels, shadows, and saved event locations.");
        Register(registry, catalog, RegionRulesEditorId, "Region Rules", new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.MapNote },
            "Region restrictions, common events, battlebacks, footsteps, stealth regions, and vehicle permissions.");
        Register(registry, catalog, TilesetTagsEditorId, "Tileset Plugin Tags", new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.TilesetNote },
            "Floor damage, slippery tiles, and illegal jump terrain rules.");
        Register(registry, catalog, EventTagsEditorId, "Event Plugin Tags", new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.EventNote },
            "Event-level encounter, sprite offset, step animation, movement, and menu-event metadata.");
        Register(registry, catalog, EventCommentEditorId, "Event Page Comment Tags", new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.EventPageComment },
            "Page comments for click triggers, hitboxes, labels, proximity, icons, BGM, and activation rules.");
        Register(registry, catalog, PluginCommandEditorId, "YEP Plugin Command Builder", new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.PluginCommand },
            "Typed authoring for YEP MV plugin commands inserted as event command 356.");
        Register(registry, catalog, MoveRouteEditorId, "YEP Move Route Helpers", new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.MoveRouteScript },
            "Typed movement-route script helpers inserted as route command 45.");
    }

    private static void Register(
        PluginSpecializedEditorRegistry registry,
        PluginTagCatalog catalog,
        string editorId,
        string displayName,
        IReadOnlySet<PluginAuthoringSurface> surfaces,
        string description)
    {
        IReadOnlySet<PluginFeatureKey> supported = catalog.Features
            .Where(feature => string.Equals(feature.SpecializedEditorId, editorId, StringComparison.OrdinalIgnoreCase))
            .Select(feature => feature.Key)
            .ToHashSet();
        registry.Register(new(editorId, displayName, surfaces, supported, true, description), replace: true);
    }

    private static bool IsRegionFeature(PluginFeatureDefinition feature)
    {
        MapEventAuthoringArea? area = ClassifyWithoutEditor(feature);
        return area is MapEventAuthoringArea.RegionRules or MapEventAuthoringArea.Footsteps or
            MapEventAuthoringArea.Battlebacks or MapEventAuthoringArea.Vehicles;
    }

    private static MapEventAuthoringArea ClassifyWithoutEditor(PluginFeatureDefinition feature)
    {
        string text = string.Join(" ", feature.PluginId, feature.DisplayName, feature.Summary,
            feature.Syntax.CanonicalTemplate, string.Join(" ", feature.EffectiveSearchTerms)).ToLowerInvariant();
        if (text.Contains("footstep") || text.Contains("terrain tag")) return MapEventAuthoringArea.Footsteps;
        if (text.Contains("battleback")) return MapEventAuthoringArea.Battlebacks;
        if (text.Contains("vehicle") || text.Contains("airship") || text.Contains("boat") || text.Contains("ship "))
            return MapEventAuthoringArea.Vehicles;
        if (text.Contains("region") || text.Contains("stealth")) return MapEventAuthoringArea.RegionRules;
        return MapEventAuthoringArea.MapBehavior;
    }

    public static string FriendlyEditor(string editorId) => editorId switch
    {
        MapTagsEditorId => "Map Plugin Tags",
        RegionRulesEditorId => "Region Rules",
        TilesetTagsEditorId => "Tileset Plugin Tags",
        EventTagsEditorId => "Event Plugin Tags",
        EventCommentEditorId => "Event Page Comment Tags",
        PluginCommandEditorId => "Plugin Command Builder",
        MoveRouteEditorId => "Move Route Helpers",
        _ => "Map and Event Plugin Tools"
    };

    public static string FriendlyArea(MapEventAuthoringArea area) => area switch
    {
        MapEventAuthoringArea.MapBehavior => "Map Behavior",
        MapEventAuthoringArea.RegionRules => "Region Rules",
        MapEventAuthoringArea.Footsteps => "Footsteps",
        MapEventAuthoringArea.Battlebacks => "Region Battlebacks",
        MapEventAuthoringArea.EnemyLevels => "Map Enemy Levels",
        MapEventAuthoringArea.Vehicles => "Vehicle Restrictions",
        MapEventAuthoringArea.TilesetBehavior => "Tileset Behavior",
        MapEventAuthoringArea.EventBehavior => "Event Behavior",
        MapEventAuthoringArea.EventAppearance => "Event Appearance",
        MapEventAuthoringArea.EventActivation => "Event Activation",
        MapEventAuthoringArea.EventPageMetadata => "Event Page Metadata",
        MapEventAuthoringArea.PluginCommands => "Plugin Commands",
        MapEventAuthoringArea.MovementScripts => "Movement Scripts",
        _ => "Advanced"
    };
}
