namespace Playroma.JRPG.PluginTags;

public enum EnemyStateFormulaArea
{
    EnemyLevels,
    EnemyRewards,
    EnemyAppearance,
    EnemyAnimation,
    EnemyGauges,
    EnemyAudio,
    EnemyFormation,
    EnemyBehavior,
    StateTiming,
    StateLifecycle,
    StateCounters,
    DamageOverTime,
    StateCategories,
    StateProtection,
    StateVisuals,
    PassiveStates,
    Auras,
    AuraConditions,
    Barriers,
    BaseParameters,
    ExtraParameters,
    SpecialParameters,
    DamageRules,
    Elements,
    Criticals,
    Advanced
}

public sealed record EnemyStateFormulaFeatureItem(
    PluginFeatureDefinition Definition,
    EnemyStateFormulaArea Area,
    bool IsPresent,
    int OccurrenceCount,
    PluginFeatureAvailability Availability)
{
    public bool CanAuthor => Availability.CanAuthor;
    public PluginFeatureEditorPlan EditorPlan => PluginGenericEditorCoverage.CreatePlan(Definition);
}

public sealed record EnemyStateFormulaQuery(
    string? SearchText = null,
    EnemyStateFormulaArea? Area = null,
    string? SpecializedEditorId = null,
    bool CurrentOnly = false,
    bool IncludeAdvanced = false,
    bool InstalledOnly = true);

public sealed record EnemyStateFormulaPresetStep(
    PluginFeatureKey Feature,
    PluginFeatureValues Values,
    PluginFeatureWriteMode WriteMode = PluginFeatureWriteMode.Smart,
    PluginConflictResolution ConflictResolution = PluginConflictResolution.RemoveConflicting);

public sealed record EnemyStateFormulaPreset(
    string PresetId,
    string DisplayName,
    string Description,
    string SpecializedEditorId,
    IReadOnlySet<PluginDocumentKind> DocumentKinds,
    IReadOnlyList<EnemyStateFormulaPresetStep> Steps,
    IReadOnlyList<string>? SearchTerms = null);

public sealed record EnemyStateFormulaPresetPreview(
    EnemyStateFormulaPreset Preset,
    string Before,
    string After,
    IReadOnlyList<PluginTagLineChange> Changes,
    IReadOnlyList<string> Warnings)
{
    public bool HasChanges => !string.Equals(Before, After, StringComparison.Ordinal);
}

/// <summary>
/// Provider-neutral workspace shared by enemy, state, aura and formula designers.
/// It edits the same lossless text session used by raw Notes and every earlier Plugin Tags phase.
/// </summary>
public sealed class EnemyStateFormulaWorkspace
{
    private readonly PluginTagCatalog catalog;
    private readonly PluginTagAuthoringWorkspace authoring;
    private readonly Func<PluginFeatureDefinition, EnemyStateFormulaArea?> classifier;
    private readonly Dictionary<PluginFeatureKey, PluginFeatureAvailability> availability;

    public EnemyStateFormulaWorkspace(
        PluginTagCatalog catalog,
        PluginTextEditingSession session,
        Func<PluginFeatureDefinition, EnemyStateFormulaArea?> classifier,
        IEnumerable<PluginFeatureAvailability>? featureAvailability = null)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        this.classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        availability = (featureAvailability ?? [])
            .GroupBy(item => item.Feature)
            .ToDictionary(group => group.Key, group => group.Last());
        authoring = new PluginTagAuthoringWorkspace(catalog, session, availability.Values);
    }

    public PluginTextEditingSession Session { get; }

    public IReadOnlyList<EnemyStateFormulaArea> Areas(string specializedEditorId) =>
        Query(new(SpecializedEditorId: specializedEditorId, InstalledOnly: false, IncludeAdvanced: true))
            .Select(item => item.Area)
            .Distinct()
            .OrderBy(area => area)
            .ToArray();

    public IReadOnlyList<EnemyStateFormulaFeatureItem> Query(EnemyStateFormulaQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string needle = (query.SearchText ?? string.Empty).Trim();
        List<EnemyStateFormulaFeatureItem> result = [];
        foreach (PluginFeatureDefinition feature in catalog.Query(Session.Context, needle))
        {
            if (!string.IsNullOrWhiteSpace(query.SpecializedEditorId) &&
                !string.Equals(feature.SpecializedEditorId, query.SpecializedEditorId, StringComparison.OrdinalIgnoreCase))
                continue;
            EnemyStateFormulaArea? area = classifier(feature);
            if (area is null) continue;
            if (query.Area is not null && area != query.Area) continue;
            if (!query.IncludeAdvanced && feature.Complexity == PluginFeatureComplexity.AdvancedCode) continue;
            int count = Session.ParsedDocument.Find(feature.Key).Count;
            if (query.CurrentOnly && count == 0) continue;
            PluginFeatureAvailability state = ResolveAvailability(feature);
            if (query.InstalledOnly && state.State == PluginFeatureAvailabilityState.OwnerPluginMissing) continue;
            result.Add(new(feature, area.Value, count > 0, count, state));
        }

        return result.OrderByDescending(item => item.IsPresent)
            .ThenBy(item => item.Area)
            .ThenBy(item => item.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Definition.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public PluginTagChangePreview PreviewWrite(PluginTagWriteRequest request) => authoring.PreviewWrite(request);

    public PluginTagChangePreview PreviewRemove(PluginFeatureKey key, int occurrenceIndex = 0, bool removeAll = false) =>
        authoring.PreviewRemove(key, occurrenceIndex, removeAll);

    public IReadOnlyList<PluginFeatureDefinition> FindConflicts(PluginFeatureKey key) => authoring.FindConflicts(key);

    public EnemyStateFormulaPresetPreview PreviewPreset(EnemyStateFormulaPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (!preset.DocumentKinds.Contains(Session.Context.DocumentKind))
            throw new PluginTextMutationException($"Preset '{preset.DisplayName}' is not valid for {Session.Context.DocumentKind}.");

        PluginTextEditingSession clone = new(catalog, Session.Context, Session.WorkingText);
        PluginTagAuthoringWorkspace cloneWorkspace = new(catalog, clone, availability.Values);
        List<string> warnings = [];
        foreach (EnemyStateFormulaPresetStep step in preset.Steps)
        {
            PluginFeatureDefinition definition = catalog.GetRequiredFeature(step.Feature);
            PluginFeatureAvailability state = ResolveAvailability(definition);
            if (!state.CanAuthor)
            {
                warnings.Add($"{definition.DisplayName}: {state.Message}");
                continue;
            }

            PluginTagChangePreview change = cloneWorkspace.PreviewWrite(
                new(step.Feature, step.Values, step.WriteMode, step.ConflictResolution));
            clone.ReplaceText(change.After);
        }

        return new(preset, Session.WorkingText, clone.WorkingText,
            PluginTagTextDiff.Create(Session.WorkingText, clone.WorkingText), warnings);
    }

    public IReadOnlyList<PluginTagDiagnostic> Validate() => Session.Validate();

    private PluginFeatureAvailability ResolveAvailability(PluginFeatureDefinition feature) =>
        availability.TryGetValue(feature.Key, out PluginFeatureAvailability? result)
            ? result
            : new(feature.Key, PluginFeatureAvailabilityState.Available,
                "Installed-plugin analysis has not reported a blocking issue.", []);
}
