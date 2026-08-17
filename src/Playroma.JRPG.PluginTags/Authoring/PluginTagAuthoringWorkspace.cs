namespace Playroma.JRPG.PluginTags;

public enum PluginFeatureBrowserScope
{
    Installed,
    All,
    Current
}

public sealed record PluginFeatureBrowserQuery(
    string? SearchText = null,
    string? Category = null,
    PluginFeatureBrowserScope Scope = PluginFeatureBrowserScope.Installed,
    bool IncludeAdvanced = false);

public sealed record PluginFeatureBrowserItem(
    PluginFeatureDefinition Definition,
    string Category,
    bool IsPresent,
    int OccurrenceCount,
    PluginFeatureAvailability Availability)
{
    public bool CanAuthor => Availability.CanAuthor;
    public PluginFeatureEditorPlan EditorPlan => PluginGenericEditorCoverage.CreatePlan(Definition);
}

public sealed record PluginTagLineChange(string Prefix, string Text);

public sealed record PluginTagChangePreview(
    string Before,
    string After,
    string GeneratedSyntax,
    IReadOnlyList<PluginTagLineChange> Lines)
{
    public bool HasChanges => !string.Equals(Before, After, StringComparison.Ordinal);
}

/// <summary>
/// Platform-neutral authoring model used by database, map, event, and future provider UIs.
/// It never commits project files; it only operates on a lossless PluginTextEditingSession.
/// </summary>
public sealed class PluginTagAuthoringWorkspace
{
    private readonly PluginTagCatalog catalog;
    private readonly Dictionary<PluginFeatureKey, PluginFeatureAvailability> availability = new();

    public PluginTagAuthoringWorkspace(
        PluginTagCatalog catalog,
        PluginTextEditingSession session,
        IEnumerable<PluginFeatureAvailability>? featureAvailability = null)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        SetAvailability(featureAvailability);
    }

    public PluginTextEditingSession Session { get; }

    public void SetAvailability(IEnumerable<PluginFeatureAvailability>? featureAvailability)
    {
        availability.Clear();
        if (featureAvailability is null) return;
        foreach (PluginFeatureAvailability item in featureAvailability)
            availability[item.Feature] = item;
    }

    public IReadOnlyList<string> Categories(PluginFeatureBrowserQuery? query = null) =>
        Query(query ?? new PluginFeatureBrowserQuery())
            .Select(item => item.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<PluginFeatureBrowserItem> Query(PluginFeatureBrowserQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<PluginFeatureDefinition> features = catalog.Query(Session.Context, query.SearchText);
        List<PluginFeatureBrowserItem> result = [];
        foreach (PluginFeatureDefinition feature in features)
        {
            int count = Session.ParsedDocument.Find(feature.Key).Count;
            PluginFeatureAvailability state = ResolveAvailability(feature);
            if (!query.IncludeAdvanced && feature.Complexity is PluginFeatureComplexity.AdvancedCode)
                continue;
            if (query.Scope == PluginFeatureBrowserScope.Current && count == 0)
                continue;
            if (query.Scope == PluginFeatureBrowserScope.Installed &&
                state.State is PluginFeatureAvailabilityState.OwnerPluginMissing)
                continue;

            string category = PluginFeatureCategoryClassifier.Classify(feature);
            if (!string.IsNullOrWhiteSpace(query.Category) &&
                !string.Equals(query.Category, category, StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(new PluginFeatureBrowserItem(feature, category, count > 0, count, state));
        }

        return result
            .OrderByDescending(item => item.IsPresent)
            .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public PluginFeatureBrowserItem Get(PluginFeatureKey key)
    {
        PluginFeatureDefinition definition = catalog.GetRequiredFeature(key);
        int count = Session.ParsedDocument.Find(key).Count;
        return new(definition, PluginFeatureCategoryClassifier.Classify(definition), count > 0, count, ResolveAvailability(definition));
    }

    public PluginTagChangePreview PreviewUpsert(
        PluginFeatureKey key,
        PluginFeatureValues values,
        int occurrenceIndex = 0)
    {
        PluginFeatureDefinition definition = catalog.GetRequiredFeature(key);
        PluginFeatureAvailability state = ResolveAvailability(definition);
        if (!state.CanAuthor)
            throw new PluginTextMutationException(state.Message);

        PluginTextEditingSession clone = new(catalog, Session.Context, Session.WorkingText);
        clone.UpsertFeature(key, values, occurrenceIndex);
        string generated = new PluginTextRenderer().RenderFeature(definition, values, DetectLineEnding(Session.WorkingText));
        return new(Session.WorkingText, clone.WorkingText, generated,
            PluginTagTextDiff.Create(Session.WorkingText, clone.WorkingText));
    }


    public IReadOnlyList<PluginFeatureDefinition> FindConflicts(PluginFeatureKey key)
    {
        PluginFeatureDefinition definition = catalog.GetRequiredFeature(key);
        if (definition.EffectiveConflictGroups.Count == 0) return [];
        return Session.ParsedDocument.RecognizedNodes
            .Select(node => node switch
            {
                RecognizedTagNode tag => tag.Definition,
                RecognizedBlockNode block => block.Definition,
                _ => null
            })
            .OfType<PluginFeatureDefinition>()
            .Where(other => other.Key != key &&
                other.EffectiveConflictGroups.Intersect(definition.EffectiveConflictGroups, StringComparer.OrdinalIgnoreCase).Any())
            .DistinctBy(other => other.Key)
            .OrderBy(other => other.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<PluginFeatureWriteMode> WriteModes(PluginFeatureKey key)
    {
        PluginFeatureDefinition definition = catalog.GetRequiredFeature(key);
        return definition.Multiplicity switch
        {
            PluginFeatureMultiplicity.Singleton => [PluginFeatureWriteMode.Smart, PluginFeatureWriteMode.ReplaceFirst],
            PluginFeatureMultiplicity.Repeatable => [PluginFeatureWriteMode.Smart, PluginFeatureWriteMode.AddOccurrence, PluginFeatureWriteMode.ReplaceFirst, PluginFeatureWriteMode.ReplaceAll],
            PluginFeatureMultiplicity.RepeatableByKey => [PluginFeatureWriteMode.Smart, PluginFeatureWriteMode.AddOccurrence, PluginFeatureWriteMode.ReplaceFirst, PluginFeatureWriteMode.ReplaceAll],
            PluginFeatureMultiplicity.Mergeable => [PluginFeatureWriteMode.Smart, PluginFeatureWriteMode.Merge, PluginFeatureWriteMode.ReplaceFirst, PluginFeatureWriteMode.ReplaceAll],
            _ => [PluginFeatureWriteMode.Smart]
        };
    }

    public PluginTagChangePreview PreviewWrite(PluginTagWriteRequest request)
    {
        PluginFeatureDefinition definition = catalog.GetRequiredFeature(request.Feature);
        PluginFeatureAvailability state = ResolveAvailability(definition);
        if (!state.CanAuthor) throw new PluginTextMutationException(state.Message);
        PluginTagBatchRecord record = new("preview", new PluginTextEditingSession(catalog, Session.Context, Session.WorkingText));
        PluginTagBatchPlan plan = new PluginTagBatchPlanner(catalog).Plan([record], request);
        PluginTagBatchRecordChange change = plan.Records.Single();
        if (change.Error is not null) throw new PluginTextMutationException(change.Error);
        string generated = new PluginTextRenderer().RenderFeature(definition, request.Values, DetectLineEnding(Session.WorkingText));
        return new(change.Before, change.After, generated, PluginTagTextDiff.Create(change.Before, change.After));
    }

    public PluginTagChangePreview PreviewRemove(
        PluginFeatureKey key,
        int occurrenceIndex = 0,
        bool removeAll = false)
    {
        PluginTextEditingSession clone = new(catalog, Session.Context, Session.WorkingText);
        clone.RemoveFeature(key, occurrenceIndex, removeAll);
        return new(Session.WorkingText, clone.WorkingText, string.Empty,
            PluginTagTextDiff.Create(Session.WorkingText, clone.WorkingText));
    }

    private PluginFeatureAvailability ResolveAvailability(PluginFeatureDefinition feature)
    {
        if (availability.TryGetValue(feature.Key, out PluginFeatureAvailability? found)) return found;
        return new(feature.Key, PluginFeatureAvailabilityState.Available,
            "Installed-plugin analysis has not reported a blocking issue.", []);
    }

    private static string DetectLineEnding(string text)
    {
        if (text.Contains("\r\n", StringComparison.Ordinal)) return "\r\n";
        if (text.Contains('\n')) return "\n";
        if (text.Contains('\r')) return "\r";
        return Environment.NewLine;
    }
}

public static class PluginFeatureCategoryClassifier
{
    private static readonly (string Category, string[] Terms)[] Rules =
    [
        ("Battle", ["battle", "damage", "critical", "counter", "barrier", "steal", "target", "selection", "turn", "action"]),
        ("Skills", ["skill", "cooldown", "cost", "mastery", "instant cast", "limited use"]),
        ("States", ["state", "aura", "buff", "debuff", "passive", "damage over time"]),
        ("Equipment", ["weapon", "armor", "equip", "augment", "durability", "upgrade", "unleash"]),
        ("Items and Shops", ["item", "shop", "currency", "synthesis", "disassemble", "price"]),
        ("Enemies", ["enemy", "ai priority", "drop", "sideview", "dragonbones", "weak pose"]),
        ("Actors and Classes", ["actor", "class", "job point", "subclass", "stat allocation", "level up"]),
        ("Parameters", ["parameter", "param", "hp", "mp", "tp", "element", "formula"]),
        ("Maps and Regions", ["map", "region", "floor", "footstep", "slippery", "vehicle", "tile"]),
        ("Events", ["event", "move route", "click trigger", "hitbox", "sprite offset"]),
        ("Visual and Audio", ["visual", "animation", "picture", "gauge", "cursor", "sound", "bgm", "pose", "motion"])
    ];

    public static string Classify(PluginFeatureDefinition feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        string haystack = string.Join(" ", feature.PluginId, feature.DisplayName, feature.Summary,
            string.Join(" ", feature.EffectiveSearchTerms)).ToLowerInvariant();
        foreach ((string category, string[] terms) in Rules)
        {
            if (terms.Any(term => haystack.Contains(term, StringComparison.Ordinal))) return category;
        }
        return feature.Complexity is PluginFeatureComplexity.AdvancedCode ? "Advanced" : "Common";
    }
}

public static class PluginTagTextDiff
{
    public static IReadOnlyList<PluginTagLineChange> Create(string before, string after)
    {
        string[] left = Split(before);
        string[] right = Split(after);
        int[,] lengths = new int[left.Length + 1, right.Length + 1];
        for (int i = left.Length - 1; i >= 0; i--)
        for (int j = right.Length - 1; j >= 0; j--)
            lengths[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                ? lengths[i + 1, j + 1] + 1
                : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

        List<PluginTagLineChange> result = [];
        int x = 0, y = 0;
        while (x < left.Length && y < right.Length)
        {
            if (string.Equals(left[x], right[y], StringComparison.Ordinal))
            {
                result.Add(new(" ", left[x])); x++; y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                result.Add(new("-", left[x++]));
            }
            else
            {
                result.Add(new("+", right[y++]));
            }
        }
        while (x < left.Length) result.Add(new("-", left[x++]));
        while (y < right.Length) result.Add(new("+", right[y++]));
        return result;
    }

    private static string[] Split(string value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Split('\n');
}
