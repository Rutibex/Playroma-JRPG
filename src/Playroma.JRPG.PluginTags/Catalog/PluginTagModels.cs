using System.Globalization;
using System.Text.Json.Serialization;

namespace Playroma.JRPG.PluginTags;

public enum PluginAuthoringSurface
{
    DatabaseNote,
    MapNote,
    TilesetNote,
    EventNote,
    EventPageComment,
    PluginCommand,
    MoveRouteScript,
    ActionSequence,
    MessageTextCode,
    SwitchNameMetadata,
    VariableNameMetadata,
    PluginParameter,
    ScriptCall
}

public enum PluginDocumentKind
{
    Unknown,
    Actor,
    Animation,
    Armor,
    Class,
    CommonEvent,
    Enemy,
    Item,
    Skill,
    State,
    Tileset,
    Troop,
    Weapon,
    System,
    Map,
    Event,
    EventPage,
    Message,
    Switch,
    Variable,
    PluginManifest
}

public enum PluginSyntaxKind
{
    Flag,
    KeyValue,
    MultiValue,
    Range,
    StructuredLine,
    PairedBlock,
    ScriptBlock,
    OrderedBlock,
    CommandLine,
    TextCode,
    MetadataExpression
}

public enum PluginFeatureMultiplicity
{
    Singleton,
    Repeatable,
    RepeatableByKey,
    Mergeable
}

public enum PluginArgumentKind
{
    Boolean,
    Integer,
    Decimal,
    Percentage,
    Enumeration,
    Text,
    MultilineText,
    JavaScriptExpression,
    JavaScriptBlock,
    Formula,
    DatabaseId,
    DatabaseIdList,
    DatabaseIdRange,
    SwitchId,
    VariableId,
    CommonEventId,
    AnimationId,
    StateId,
    SkillId,
    ItemId,
    WeaponId,
    ArmorId,
    ClassId,
    ActorId,
    EnemyId,
    RegionId,
    IconId,
    AssetFile,
    AudioFile,
    Color,
    Coordinate,
    TargetSelector,
    ParameterSelector,
    ComparisonOperator,
    Element,
    ActionSequenceTarget,
    ActionSequenceCommand
}

public enum PluginReferenceKind
{
    None,
    Actor,
    Animation,
    Armor,
    Class,
    CommonEvent,
    Enemy,
    Item,
    Skill,
    State,
    Tileset,
    Troop,
    Weapon,
    Switch,
    Variable,
    Region,
    Icon
}

public enum PluginFeatureComplexity
{
    Simple,
    Structured,
    Specialized,
    AdvancedCode
}

public enum PluginValueKind
{
    Text,
    Boolean,
    Integer,
    Decimal,
    Percentage,
    Identifier,
    IdentifierList,
    Expression,
    Block
}

public readonly struct PluginFeatureKey : IEquatable<PluginFeatureKey>
{
    public PluginFeatureKey(string providerId, string featureId)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        FeatureId = featureId ?? throw new ArgumentNullException(nameof(featureId));
    }

    public string ProviderId { get; }
    public string FeatureId { get; }

    public bool Equals(PluginFeatureKey other) =>
        string.Equals(ProviderId, other.ProviderId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(FeatureId, other.FeatureId, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is PluginFeatureKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(ProviderId),
        StringComparer.OrdinalIgnoreCase.GetHashCode(FeatureId));

    public static bool operator ==(PluginFeatureKey left, PluginFeatureKey right) => left.Equals(right);
    public static bool operator !=(PluginFeatureKey left, PluginFeatureKey right) => !left.Equals(right);

    public override string ToString() => ProviderId + ":" + FeatureId;
}

public readonly record struct PluginTextSpan(int Start, int Length)
{
    public int End => checked(Start + Length);

    public static PluginTextSpan EmptyAt(int offset) => new(offset, 0);
}

public sealed record PluginVersionRange(
    string? MinimumInclusive = null,
    string? MaximumInclusive = null,
    IReadOnlyList<string>? VerifiedVersions = null,
    IReadOnlyList<string>? SourceFingerprints = null);

public sealed record PluginProviderManifest(
    string ProviderId,
    string DisplayName,
    string RuntimeFamily,
    string CatalogVersion,
    string? DocumentationUrl = null,
    string? Description = null);

public sealed record PluginSyntaxPattern(
    string RegularExpression,
    bool IgnoreCase = true,
    bool MatchTrimmedLine = true,
    string? Description = null);

public sealed record PluginSyntaxDefinition(
    PluginSyntaxKind Kind,
    string CanonicalTemplate,
    IReadOnlyList<PluginSyntaxPattern> Patterns,
    string? ClosingTemplate = null,
    IReadOnlyList<PluginSyntaxPattern>? ClosingPatterns = null,
    bool PreserveBodyExactly = true)
{
    [JsonIgnore]
    public bool IsBlock => Kind is PluginSyntaxKind.PairedBlock or PluginSyntaxKind.ScriptBlock or PluginSyntaxKind.OrderedBlock;
}

public sealed record PluginArgumentDefinition(
    string Name,
    string DisplayName,
    PluginArgumentKind Kind,
    bool IsRequired = false,
    decimal? Minimum = null,
    decimal? Maximum = null,
    IReadOnlyList<string>? Choices = null,
    PluginReferenceKind ReferenceKind = PluginReferenceKind.None,
    bool AllowMultiple = false,
    string? Description = null,
    string? DefaultValue = null);

public sealed record PluginFeatureDefinition(
    string ProviderId,
    string PluginId,
    string FeatureId,
    string DisplayName,
    string Summary,
    PluginAuthoringSurface Surface,
    IReadOnlySet<PluginDocumentKind> DocumentKinds,
    PluginSyntaxDefinition Syntax,
    IReadOnlyList<PluginArgumentDefinition> Arguments,
    PluginFeatureMultiplicity Multiplicity = PluginFeatureMultiplicity.Singleton,
    IReadOnlyList<string>? RequiredPlugins = null,
    IReadOnlyList<string>? RecommendedPlugins = null,
    IReadOnlyList<string>? ConflictGroups = null,
    string? SpecializedEditorId = null,
    PluginFeatureComplexity Complexity = PluginFeatureComplexity.Simple,
    PluginVersionRange? Compatibility = null,
    string? DocumentationUrl = null,
    IReadOnlyList<string>? SearchTerms = null,
    int MatchPriority = 0)
{
    [JsonIgnore]
    public PluginFeatureKey Key => new(ProviderId, FeatureId);

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveRequiredPlugins => RequiredPlugins ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveRecommendedPlugins => RecommendedPlugins ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveConflictGroups => ConflictGroups ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveSearchTerms => SearchTerms ?? [];
}

public sealed record PluginValue(PluginValueKind Kind, string Text)
{
    public static PluginValue FromText(string value) => new(PluginValueKind.Text, value ?? string.Empty);
    public static PluginValue FromBoolean(bool value) => new(PluginValueKind.Boolean, value ? "true" : "false");
    public static PluginValue FromInteger(long value) => new(PluginValueKind.Integer, value.ToString(CultureInfo.InvariantCulture));
    public static PluginValue FromDecimal(decimal value) => new(PluginValueKind.Decimal, value.ToString(CultureInfo.InvariantCulture));
    public static PluginValue FromPercentage(decimal value) => new(PluginValueKind.Percentage, value.ToString(CultureInfo.InvariantCulture));
    public static PluginValue FromIdentifier(int value) => new(PluginValueKind.Identifier, value.ToString(CultureInfo.InvariantCulture));
    public static PluginValue FromIdentifierList(IEnumerable<int> values) => new(PluginValueKind.IdentifierList, string.Join(", ", values));
    public static PluginValue FromExpression(string value) => new(PluginValueKind.Expression, value ?? string.Empty);
    public static PluginValue FromBlock(string value) => new(PluginValueKind.Block, value ?? string.Empty);
}

public sealed class PluginFeatureValues
{
    private readonly Dictionary<string, PluginValue> _values;

    public PluginFeatureValues(
        IEnumerable<KeyValuePair<string, PluginValue>>? values = null,
        string? body = null)
    {
        _values = new Dictionary<string, PluginValue>(StringComparer.OrdinalIgnoreCase);
        if (values is not null)
        {
            foreach (KeyValuePair<string, PluginValue> pair in values)
                _values[pair.Key] = pair.Value;
        }

        Body = body;
    }

    public string? Body { get; }
    public IReadOnlyDictionary<string, PluginValue> Arguments => _values;

    public bool TryGetValue(string name, out PluginValue value)
    {
        if (_values.TryGetValue(name, out PluginValue? found))
        {
            value = found;
            return true;
        }

        value = null!;
        return false;
    }

    public PluginValue this[string name] => _values[name];

    public static PluginFeatureValues Empty { get; } = new();

    public static PluginFeatureValues FromStrings(
        IEnumerable<KeyValuePair<string, string>> values,
        string? body = null) =>
        new(values.Select(pair => new KeyValuePair<string, PluginValue>(pair.Key, PluginValue.FromText(pair.Value))), body);
}

public sealed record PluginTagContext(
    PluginAuthoringSurface Surface,
    PluginDocumentKind DocumentKind,
    string? ProjectPath = null,
    string? DocumentPath = null,
    int? RecordId = null,
    int? EventId = null,
    int? EventPageIndex = null);

public sealed record InstalledPluginStatus(
    string PluginId,
    bool IsPresent,
    bool IsEnabled,
    string? Version = null,
    int? LoadOrder = null,
    string? SourceFingerprint = null);
