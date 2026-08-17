namespace Playroma.JRPG.PluginTags;

public enum SkillProgressionArea
{
    Costs,
    Requirements,
    Execution,
    Cooldowns,
    LimitedUses,
    Mastery,
    Rewards,
    SkillTypes,
    InstantCast,
    PartyLimit,
    Learning,
    JobPoints,
    Classes,
    Subclasses,
    StatAllocation,
    TpModes,
    EquippedSkills,
    LevelGrowth,
    Advanced
}

public sealed record SkillProgressionFeatureItem(
    PluginFeatureDefinition Definition,
    SkillProgressionArea Area,
    bool IsPresent,
    int OccurrenceCount,
    PluginFeatureAvailability Availability)
{
    public bool CanAuthor => Availability.CanAuthor;
    public PluginFeatureEditorPlan EditorPlan => PluginGenericEditorCoverage.CreatePlan(Definition);
}

public sealed record SkillProgressionQuery(
    string? SearchText = null,
    SkillProgressionArea? Area = null,
    bool CurrentOnly = false,
    bool IncludeAdvanced = false,
    bool InstalledOnly = true);

public sealed record SkillProgressionPresetStep(
    PluginFeatureKey Feature,
    PluginFeatureValues Values,
    PluginFeatureWriteMode WriteMode = PluginFeatureWriteMode.Smart,
    PluginConflictResolution ConflictResolution = PluginConflictResolution.RemoveConflicting);

public sealed record SkillProgressionPreset(
    string PresetId,
    string DisplayName,
    string Description,
    IReadOnlySet<PluginDocumentKind> DocumentKinds,
    IReadOnlyList<SkillProgressionPresetStep> Steps,
    IReadOnlyList<string>? SearchTerms = null)
{
    public IReadOnlyList<string> EffectiveSearchTerms => SearchTerms ?? [];
}

public sealed record SkillProgressionPresetPreview(
    SkillProgressionPreset Preset,
    string Before,
    string After,
    IReadOnlyList<PluginTagLineChange> Changes,
    IReadOnlyList<string> Warnings)
{
    public bool HasChanges => !string.Equals(Before, After, StringComparison.Ordinal);
}

/// <summary>
/// Provider-neutral workspace used by dedicated skill/progression editors. The provider supplies
/// classification and presets; this type supplies safe filtering, previews and lossless mutation.
/// </summary>
public sealed class SkillProgressionWorkspace
{
    private readonly PluginTagCatalog catalog;
    private readonly PluginTagAuthoringWorkspace authoring;
    private readonly Func<PluginFeatureDefinition, SkillProgressionArea?> classifier;
    private readonly Dictionary<PluginFeatureKey, PluginFeatureAvailability> availability;

    public SkillProgressionWorkspace(
        PluginTagCatalog catalog,
        PluginTextEditingSession session,
        Func<PluginFeatureDefinition, SkillProgressionArea?> classifier,
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

    public IReadOnlyList<SkillProgressionArea> Areas => Query(new(InstalledOnly: false, IncludeAdvanced: true))
        .Select(item => item.Area)
        .Distinct()
        .OrderBy(area => area)
        .ToArray();

    public IReadOnlyList<SkillProgressionFeatureItem> Query(SkillProgressionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string needle = (query.SearchText ?? string.Empty).Trim();
        List<SkillProgressionFeatureItem> result = [];
        foreach (PluginFeatureDefinition feature in catalog.Query(Session.Context, needle))
        {
            SkillProgressionArea? area = classifier(feature);
            if (area is null) continue;
            if (query.Area is not null && area != query.Area) continue;
            if (!query.IncludeAdvanced && feature.Complexity == PluginFeatureComplexity.AdvancedCode) continue;
            int count = Session.ParsedDocument.Find(feature.Key).Count;
            if (query.CurrentOnly && count == 0) continue;
            PluginFeatureAvailability state = ResolveAvailability(feature);
            if (query.InstalledOnly && state.State == PluginFeatureAvailabilityState.OwnerPluginMissing) continue;
            result.Add(new(feature, area.Value, count > 0, count, state));
        }

        return result
            .OrderByDescending(item => item.IsPresent)
            .ThenBy(item => item.Area)
            .ThenBy(item => item.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Definition.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public PluginTagChangePreview PreviewWrite(PluginTagWriteRequest request) => authoring.PreviewWrite(request);
    public PluginTagChangePreview PreviewRemove(PluginFeatureKey key, int occurrenceIndex = 0, bool removeAll = false) =>
        authoring.PreviewRemove(key, occurrenceIndex, removeAll);
    public IReadOnlyList<PluginFeatureDefinition> FindConflicts(PluginFeatureKey key) => authoring.FindConflicts(key);

    public SkillProgressionPresetPreview PreviewPreset(SkillProgressionPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (!preset.DocumentKinds.Contains(Session.Context.DocumentKind))
            throw new PluginTextMutationException($"Preset '{preset.DisplayName}' is not valid for {Session.Context.DocumentKind}.");

        PluginTextEditingSession clone = new(catalog, Session.Context, Session.WorkingText);
        PluginTagAuthoringWorkspace cloneWorkspace = new(catalog, clone, availability.Values);
        List<string> warnings = [];
        foreach (SkillProgressionPresetStep step in preset.Steps)
        {
            PluginFeatureDefinition definition = catalog.GetRequiredFeature(step.Feature);
            PluginFeatureAvailability state = ResolveAvailability(definition);
            if (!state.CanAuthor)
            {
                warnings.Add($"{definition.DisplayName}: {state.Message}");
                continue;
            }
            PluginTagChangePreview change = cloneWorkspace.PreviewWrite(new(step.Feature, step.Values, step.WriteMode, step.ConflictResolution));
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
