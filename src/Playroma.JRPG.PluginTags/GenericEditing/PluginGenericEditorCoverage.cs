namespace Playroma.JRPG.PluginTags;

public enum PluginGenericEditorKind
{
    Toggle,
    Integer,
    Decimal,
    Percentage,
    Choice,
    SingleLineText,
    MultilineText,
    Formula,
    JavaScriptExpression,
    JavaScriptBlock,
    DatabaseReference,
    DatabaseReferenceList,
    DatabaseReferenceRange,
    SwitchReference,
    VariableReference,
    RegionReference,
    IconReference,
    AssetReference,
    AudioReference,
    Color,
    Coordinate,
    TargetSelector,
    ParameterSelector,
    ComparisonOperator,
    ElementSelector,
    ActionSequenceTarget,
    ActionSequenceCommand,
    RawBlockBody
}

public sealed record PluginArgumentEditorDescriptor(
    PluginArgumentDefinition Argument,
    PluginGenericEditorKind EditorKind,
    PluginDocumentKind ReferenceDocumentKind = PluginDocumentKind.Unknown,
    string? AssetCategoryHint = null,
    IReadOnlyList<string>? EffectiveChoices = null,
    bool SupportsSearch = false,
    bool SupportsMultiple = false);

public sealed record PluginFeatureEditorPlan(
    PluginFeatureDefinition Definition,
    IReadOnlyList<PluginArgumentEditorDescriptor> Arguments,
    PluginGenericEditorKind? BodyEditor,
    bool UsesGenericEditor,
    bool RequiresSpecializedEditor,
    bool IsAdvancedCode,
    string CoverageMessage)
{
    public bool IsCompletelyCovered => UsesGenericEditor && !RequiresSpecializedEditor;
}

/// <summary>
/// Resolves every platform-neutral argument kind to a reusable editor family. This is the
/// Phase 5 coverage contract: a feature without a specialized-editor assignment must always
/// receive a complete generic plan, including advanced formula/script blocks.
/// </summary>
public static class PluginGenericEditorCoverage
{
    private static readonly IReadOnlyDictionary<PluginArgumentKind, PluginGenericEditorKind> KindMap =
        new Dictionary<PluginArgumentKind, PluginGenericEditorKind>
        {
            [PluginArgumentKind.Boolean] = PluginGenericEditorKind.Toggle,
            [PluginArgumentKind.Integer] = PluginGenericEditorKind.Integer,
            [PluginArgumentKind.Decimal] = PluginGenericEditorKind.Decimal,
            [PluginArgumentKind.Percentage] = PluginGenericEditorKind.Percentage,
            [PluginArgumentKind.Enumeration] = PluginGenericEditorKind.Choice,
            [PluginArgumentKind.Text] = PluginGenericEditorKind.SingleLineText,
            [PluginArgumentKind.MultilineText] = PluginGenericEditorKind.MultilineText,
            [PluginArgumentKind.JavaScriptExpression] = PluginGenericEditorKind.JavaScriptExpression,
            [PluginArgumentKind.JavaScriptBlock] = PluginGenericEditorKind.JavaScriptBlock,
            [PluginArgumentKind.Formula] = PluginGenericEditorKind.Formula,
            [PluginArgumentKind.DatabaseId] = PluginGenericEditorKind.DatabaseReference,
            [PluginArgumentKind.DatabaseIdList] = PluginGenericEditorKind.DatabaseReferenceList,
            [PluginArgumentKind.DatabaseIdRange] = PluginGenericEditorKind.DatabaseReferenceRange,
            [PluginArgumentKind.SwitchId] = PluginGenericEditorKind.SwitchReference,
            [PluginArgumentKind.VariableId] = PluginGenericEditorKind.VariableReference,
            [PluginArgumentKind.CommonEventId] = PluginGenericEditorKind.DatabaseReference,
            [PluginArgumentKind.AnimationId] = PluginGenericEditorKind.DatabaseReference,
            [PluginArgumentKind.StateId] = PluginGenericEditorKind.DatabaseReference,
            [PluginArgumentKind.SkillId] = PluginGenericEditorKind.DatabaseReference,
            [PluginArgumentKind.ItemId] = PluginGenericEditorKind.DatabaseReference,
            [PluginArgumentKind.WeaponId] = PluginGenericEditorKind.DatabaseReference,
            [PluginArgumentKind.ArmorId] = PluginGenericEditorKind.DatabaseReference,
            [PluginArgumentKind.ClassId] = PluginGenericEditorKind.DatabaseReference,
            [PluginArgumentKind.ActorId] = PluginGenericEditorKind.DatabaseReference,
            [PluginArgumentKind.EnemyId] = PluginGenericEditorKind.DatabaseReference,
            [PluginArgumentKind.RegionId] = PluginGenericEditorKind.RegionReference,
            [PluginArgumentKind.IconId] = PluginGenericEditorKind.IconReference,
            [PluginArgumentKind.AssetFile] = PluginGenericEditorKind.AssetReference,
            [PluginArgumentKind.AudioFile] = PluginGenericEditorKind.AudioReference,
            [PluginArgumentKind.Color] = PluginGenericEditorKind.Color,
            [PluginArgumentKind.Coordinate] = PluginGenericEditorKind.Coordinate,
            [PluginArgumentKind.TargetSelector] = PluginGenericEditorKind.TargetSelector,
            [PluginArgumentKind.ParameterSelector] = PluginGenericEditorKind.ParameterSelector,
            [PluginArgumentKind.ComparisonOperator] = PluginGenericEditorKind.ComparisonOperator,
            [PluginArgumentKind.Element] = PluginGenericEditorKind.ElementSelector,
            [PluginArgumentKind.ActionSequenceTarget] = PluginGenericEditorKind.ActionSequenceTarget,
            [PluginArgumentKind.ActionSequenceCommand] = PluginGenericEditorKind.ActionSequenceCommand
        };

    public static PluginFeatureEditorPlan CreatePlan(PluginFeatureDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        List<PluginArgumentEditorDescriptor> arguments = [];
        foreach (PluginArgumentDefinition argument in definition.Arguments)
        {
            if (!KindMap.TryGetValue(argument.Kind, out PluginGenericEditorKind kind))
                throw new PluginTagCatalogException($"No generic editor mapping exists for {argument.Kind} in {definition.Key}/{argument.Name}.");
            arguments.Add(new(
                argument,
                kind,
                ResolveReferenceDocumentKind(argument),
                ResolveAssetCategoryHint(definition, argument),
                ResolveChoices(argument),
                SupportsSearch(kind),
                argument.AllowMultiple || kind is PluginGenericEditorKind.DatabaseReferenceList));
        }

        bool specialized = !string.IsNullOrWhiteSpace(definition.SpecializedEditorId) ||
            definition.Complexity == PluginFeatureComplexity.Specialized;
        bool advanced = definition.Complexity == PluginFeatureComplexity.AdvancedCode ||
            definition.Syntax.Kind == PluginSyntaxKind.ScriptBlock ||
            definition.Arguments.Any(argument => argument.Kind is PluginArgumentKind.JavaScriptExpression or PluginArgumentKind.JavaScriptBlock or PluginArgumentKind.Formula);
        PluginGenericEditorKind? body = definition.Syntax.IsBlock
            ? advanced ? PluginGenericEditorKind.JavaScriptBlock : PluginGenericEditorKind.RawBlockBody
            : null;
        string message = specialized
            ? $"Reserved for specialized editor '{definition.SpecializedEditorId ?? "unassigned"}'; generic raw fallback remains available."
            : advanced
                ? "Completely covered by the generic code/formula editor."
                : "Completely covered by reusable typed controls.";
        return new(definition, arguments, body, UsesGenericEditor: true, specialized, advanced, message);
    }

    public static IReadOnlyList<PluginFeatureEditorPlan> Analyze(IEnumerable<PluginFeatureDefinition> features) =>
        features.Select(CreatePlan).ToArray();

    public static IReadOnlyDictionary<PluginGenericEditorKind, int> CountArgumentEditors(
        IEnumerable<PluginFeatureDefinition> features) =>
        Analyze(features)
            .SelectMany(plan => plan.Arguments)
            .GroupBy(argument => argument.EditorKind)
            .ToDictionary(group => group.Key, group => group.Count());

    private static IReadOnlyList<string> ResolveChoices(PluginArgumentDefinition argument)
    {
        if (argument.Choices is { Count: > 0 }) return argument.Choices;
        return argument.Kind switch
        {
            PluginArgumentKind.Boolean => ["true", "false"],
            PluginArgumentKind.ParameterSelector => ["MHP", "MMP", "ATK", "DEF", "MAT", "MDF", "AGI", "LUK", "HP", "MP", "TP"],
            PluginArgumentKind.ComparisonOperator => ["==", "!=", ">", ">=", "<", "<="],
            PluginArgumentKind.TargetSelector or PluginArgumentKind.ActionSequenceTarget =>
                ["user", "target", "targets", "all targets", "friends", "opponents", "alive friends", "dead friends", "random target"],
            PluginArgumentKind.Element => ["Normal Attack", "None", "Fire", "Ice", "Thunder", "Water", "Earth", "Wind", "Light", "Dark"],
            _ => []
        };
    }

    private static bool SupportsSearch(PluginGenericEditorKind kind) => kind is
        PluginGenericEditorKind.DatabaseReference or
        PluginGenericEditorKind.DatabaseReferenceList or
        PluginGenericEditorKind.DatabaseReferenceRange or
        PluginGenericEditorKind.AssetReference or
        PluginGenericEditorKind.AudioReference or
        PluginGenericEditorKind.IconReference or
        PluginGenericEditorKind.ElementSelector;

    private static PluginDocumentKind ResolveReferenceDocumentKind(PluginArgumentDefinition argument)
    {
        if (argument.ReferenceKind != PluginReferenceKind.None)
        {
            return argument.ReferenceKind switch
            {
                PluginReferenceKind.Actor => PluginDocumentKind.Actor,
                PluginReferenceKind.Animation => PluginDocumentKind.Animation,
                PluginReferenceKind.Armor => PluginDocumentKind.Armor,
                PluginReferenceKind.Class => PluginDocumentKind.Class,
                PluginReferenceKind.CommonEvent => PluginDocumentKind.CommonEvent,
                PluginReferenceKind.Enemy => PluginDocumentKind.Enemy,
                PluginReferenceKind.Item => PluginDocumentKind.Item,
                PluginReferenceKind.Skill => PluginDocumentKind.Skill,
                PluginReferenceKind.State => PluginDocumentKind.State,
                PluginReferenceKind.Tileset => PluginDocumentKind.Tileset,
                PluginReferenceKind.Troop => PluginDocumentKind.Troop,
                PluginReferenceKind.Weapon => PluginDocumentKind.Weapon,
                _ => PluginDocumentKind.Unknown
            };
        }

        return argument.Kind switch
        {
            PluginArgumentKind.ActorId => PluginDocumentKind.Actor,
            PluginArgumentKind.AnimationId => PluginDocumentKind.Animation,
            PluginArgumentKind.ArmorId => PluginDocumentKind.Armor,
            PluginArgumentKind.ClassId => PluginDocumentKind.Class,
            PluginArgumentKind.CommonEventId => PluginDocumentKind.CommonEvent,
            PluginArgumentKind.EnemyId => PluginDocumentKind.Enemy,
            PluginArgumentKind.ItemId => PluginDocumentKind.Item,
            PluginArgumentKind.SkillId => PluginDocumentKind.Skill,
            PluginArgumentKind.StateId => PluginDocumentKind.State,
            PluginArgumentKind.WeaponId => PluginDocumentKind.Weapon,
            _ => PluginDocumentKind.Unknown
        };
    }

    private static string? ResolveAssetCategoryHint(
        PluginFeatureDefinition definition,
        PluginArgumentDefinition argument)
    {
        if (argument.Kind is not PluginArgumentKind.AssetFile and not PluginArgumentKind.AudioFile) return null;
        string text = string.Join(" ", definition.DisplayName, definition.Summary, argument.Name, argument.DisplayName).ToLowerInvariant();
        if (argument.Kind == PluginArgumentKind.AudioFile)
        {
            if (text.Contains("bgm", StringComparison.Ordinal) || text.Contains("music", StringComparison.Ordinal)) return "BackgroundMusic";
            if (text.Contains("bgs", StringComparison.Ordinal) || text.Contains("ambient", StringComparison.Ordinal)) return "BackgroundSounds";
            if (text.Contains("me", StringComparison.Ordinal) || text.Contains("fanfare", StringComparison.Ordinal)) return "MusicEffects";
            return "SoundEffects";
        }
        if (text.Contains("face", StringComparison.Ordinal)) return "Faces";
        if (text.Contains("character", StringComparison.Ordinal) || text.Contains("sprite", StringComparison.Ordinal)) return "Characters";
        if (text.Contains("sideview", StringComparison.Ordinal) || text.Contains("sv battler", StringComparison.Ordinal)) return "SideViewEnemies";
        if (text.Contains("enemy", StringComparison.Ordinal) || text.Contains("battler", StringComparison.Ordinal)) return "Enemies";
        if (text.Contains("picture", StringComparison.Ordinal) || text.Contains("profile", StringComparison.Ordinal)) return "Pictures";
        if (text.Contains("tileset", StringComparison.Ordinal)) return "Tilesets";
        if (text.Contains("battleback", StringComparison.Ordinal)) return "Battlebacks1";
        if (text.Contains("parallax", StringComparison.Ordinal)) return "Parallaxes";
        return "Pictures";
    }
}
