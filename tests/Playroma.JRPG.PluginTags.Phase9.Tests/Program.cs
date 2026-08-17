using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

List<(string Name, Action Test)> tests =
[
    ("Phase 9 covers thirty-four YEP plugins", CoversThirtyFourPlugins),
    ("Every covered plugin has routed database features", AllCoveredPluginsRoute),
    ("Enemy Designer routes 188 features", EnemyFeatureCount),
    ("State Lifecycle routes 132 features", StateFeatureCount),
    ("Passive States and Auras route 61 features", AuraFeatureCount),
    ("Formula Lab routes 154 features", FormulaFeatureCount),
    ("Phase 9 routes 535 total features", TotalFeatureCount),
    ("Prior semantic routes normalize to Phase 9 editors", PriorRoutesNormalize),
    ("Enemy categories classify correctly", EnemyClassifies),
    ("State categories classify correctly", StateClassifies),
    ("Aura categories classify correctly", AuraClassifies),
    ("Formula categories classify correctly", FormulaClassifies),
    ("Enemy workspace filters to Enemy Designer", EnemyWorkspaceFilters),
    ("State workspace filters to State Lifecycle", StateWorkspaceFilters),
    ("Current-only filtering recognizes a passive state", CurrentOnlyFilter),
    ("Advanced filtering reveals formula code blocks", AdvancedFilter),
    ("Scaled Enemy quick setup resolves", ScaledEnemyPresetResolves),
    ("Damage Over Time quick setup resolves", DamageOverTimePresetResolves),
    ("Passive State quick setup resolves", PassivePresetResolves),
    ("Barrier quick setup resolves", BarrierPresetResolves),
    ("Preset preview preserves unrelated notes", PresetPreservesNotes),
    ("Missing plugin safely warns and skips", MissingPluginWarns),
    ("Typed formula write remains lossless", TypedFormulaWritePreservesNotes),
    ("Formula safety detects bracket errors", FormulaBracketValidation),
    ("Formula safety warns about host access", FormulaHostWarning),
    ("Four specialized descriptors register", SpecializedDescriptorsRegister),
    ("Runtime feature count remains stable", RuntimeFeatureCount),
    ("No YEP JavaScript is embedded", NoJavaScriptEmbedded)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
}
Console.WriteLine($"Phase 9 tests: {tests.Count - failures}/{tests.Count} passed.");
return failures == 0 ? 0 : 1;

static PluginTagCatalog Embedded() => new PluginTagCatalogBuilder().AddProvider(YanflyGeneratedCatalogProvider.LoadEmbedded()).Build();
static PluginTagContext EnemyContext() => new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Enemy);
static PluginTagContext StateContext() => new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.State);
static PluginTagContext ActorContext() => new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Actor);

static void CoversThirtyFourPlugins() => Assert(YanflyEnemyStateFormulaFeatureDefinitions.CoveredPluginIds.Count == 34,
    $"Expected 34 plugins, found {YanflyEnemyStateFormulaFeatureDefinitions.CoveredPluginIds.Count}.");

static void AllCoveredPluginsRoute()
{
    PluginTagCatalog catalog = Embedded();
    foreach (string plugin in YanflyEnemyStateFormulaFeatureDefinitions.CoveredPluginIds)
        Assert(catalog.Features.Any(feature => feature.PluginId.Equals(plugin, StringComparison.OrdinalIgnoreCase) &&
            feature.Surface == PluginAuthoringSurface.DatabaseNote &&
            feature.SpecializedEditorId is not null &&
            IsPhase9Editor(feature.SpecializedEditorId)), "No Phase 9 database feature routed for " + plugin);
}

static void EnemyFeatureCount() => Assert(Count(YanflyEnemyStateFormulaFeatureDefinitions.EnemyDesignerEditorId) == 188,
    "Enemy Designer feature count changed.");
static void StateFeatureCount() => Assert(Count(YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId) == 132,
    "State Lifecycle feature count changed.");
static void AuraFeatureCount() => Assert(Count(YanflyEnemyStateFormulaFeatureDefinitions.PassiveAuraEditorId) == 61,
    "Passive/Aura feature count changed.");
static void FormulaFeatureCount() => Assert(Count(YanflyEnemyStateFormulaFeatureDefinitions.FormulaLabEditorId) == 154,
    "Formula Lab feature count changed.");
static void TotalFeatureCount() => Assert(Embedded().Features.Count(feature => feature.Surface == PluginAuthoringSurface.DatabaseNote &&
    feature.SpecializedEditorId is not null && IsPhase9Editor(feature.SpecializedEditorId)) == 535, "Expected 535 Phase 9 features.");

static void PriorRoutesNormalize()
{
    PluginTagCatalog catalog = Embedded();
    Assert(Find(catalog, "YEP_BuffsStatesCore", "<Custom Apply Effect>").SpecializedEditorId ==
        YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId, "State lifecycle route was not normalized.");
    Assert(Find(catalog, "YEP_EnemyLevels", "<Minimum Level:").SpecializedEditorId ==
        YanflyEnemyStateFormulaFeatureDefinitions.EnemyDesignerEditorId, "Enemy route was not normalized.");
    Assert(Find(catalog, "YEP_X_PassiveAuras", "<Alive Ally Aura:").SpecializedEditorId ==
        YanflyEnemyStateFormulaFeatureDefinitions.PassiveAuraEditorId, "Aura route was not normalized.");
}

static void EnemyClassifies()
{
    PluginTagCatalog catalog = Embedded();
    Assert(YanflyEnemyStateFormulaFeatureDefinitions.Classify(Find(catalog, "YEP_EnemyLevels", "<Minimum Level:")) == EnemyStateFormulaArea.EnemyLevels,
        "Enemy level tag classified incorrectly.");
    Assert(YanflyEnemyStateFormulaFeatureDefinitions.Classify(Find(catalog, "YEP_X_VisualHpGauge", "<HP Gauge Width:")) == EnemyStateFormulaArea.EnemyGauges,
        "Enemy gauge tag classified incorrectly.");
    Assert(YanflyEnemyStateFormulaFeatureDefinitions.Classify(Find(catalog, "YEP_X_AnimatedSVEnemies", "<Sideview Battler:")) == EnemyStateFormulaArea.EnemyAppearance,
        "Sideview tag classified incorrectly.");
}

static void StateClassifies()
{
    PluginTagCatalog catalog = Embedded();
    Assert(YanflyEnemyStateFormulaFeatureDefinitions.Classify(Find(catalog, "YEP_X_ExtDoT", "<DoT Element:")) == EnemyStateFormulaArea.DamageOverTime,
        "DoT tag classified incorrectly.");
    Assert(YanflyEnemyStateFormulaFeatureDefinitions.Classify(Find(catalog, "YEP_X_VisualStateFX", "<State Motion: Abnormal>")) == EnemyStateFormulaArea.StateVisuals,
        "State visual tag classified incorrectly.");
    Assert(YanflyEnemyStateFormulaFeatureDefinitions.Classify(Find(catalog, "YEP_Z_StateProtection", "<HP Protection: Damage Null 20%>")) == EnemyStateFormulaArea.StateProtection,
        "State protection tag classified incorrectly.");
}

static void AuraClassifies()
{
    PluginTagCatalog catalog = Embedded();
    Assert(YanflyEnemyStateFormulaFeatureDefinitions.Classify(Find(catalog, "YEP_AutoPassiveStates", "<Passive State:")) == EnemyStateFormulaArea.PassiveStates,
        "Passive state classified incorrectly.");
    Assert(YanflyEnemyStateFormulaFeatureDefinitions.Classify(Find(catalog, "YEP_X_PassiveAuras", "<Foe Aura:")) == EnemyStateFormulaArea.Auras,
        "Aura classified incorrectly.");
}

static void FormulaClassifies()
{
    PluginTagCatalog catalog = Embedded();
    Assert(YanflyEnemyStateFormulaFeatureDefinitions.Classify(Find(catalog, "YEP_AbsorptionBarrier", "<Barrier Points: +")) == EnemyStateFormulaArea.Barriers,
        "Barrier classified incorrectly.");
    Assert(YanflyEnemyStateFormulaFeatureDefinitions.Classify(Find(catalog, "YEP_ElementCore", "<Force Element")) == EnemyStateFormulaArea.Elements,
        "Element tag classified incorrectly.");
    Assert(YanflyEnemyStateFormulaFeatureDefinitions.Classify(Find(catalog, "YEP_X_CriticalControl", "Critical")) == EnemyStateFormulaArea.Criticals,
        "Critical tag classified incorrectly.");
}

static void EnemyWorkspaceFilters()
{
    PluginTagCatalog catalog = Embedded();
    EnemyStateFormulaWorkspace workspace = new(catalog, new(catalog, EnemyContext(), string.Empty), YanflyEnemyStateFormulaFeatureDefinitions.Classify);
    IReadOnlyList<EnemyStateFormulaFeatureItem> items = workspace.Query(new(SpecializedEditorId:
        YanflyEnemyStateFormulaFeatureDefinitions.EnemyDesignerEditorId, InstalledOnly: false, IncludeAdvanced: true));
    Assert(items.Count > 100, "Enemy workspace is unexpectedly small: " + items.Count);
    Assert(items.All(item => item.Definition.SpecializedEditorId == YanflyEnemyStateFormulaFeatureDefinitions.EnemyDesignerEditorId),
        "A non-enemy feature entered Enemy Designer.");
}

static void StateWorkspaceFilters()
{
    PluginTagCatalog catalog = Embedded();
    EnemyStateFormulaWorkspace workspace = new(catalog, new(catalog, StateContext(), string.Empty), YanflyEnemyStateFormulaFeatureDefinitions.Classify);
    IReadOnlyList<EnemyStateFormulaFeatureItem> items = workspace.Query(new(SpecializedEditorId:
        YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId, InstalledOnly: false, IncludeAdvanced: true));
    Assert(items.Count > 80, "State workspace is unexpectedly small: " + items.Count);
    Assert(items.All(item => item.Definition.SpecializedEditorId == YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId),
        "A non-state feature entered State Lifecycle.");
}

static void CurrentOnlyFilter()
{
    PluginTagCatalog catalog = Embedded();
    EnemyStateFormulaWorkspace workspace = new(catalog, new(catalog, ActorContext(), "<Passive State: 4>"),
        YanflyEnemyStateFormulaFeatureDefinitions.Classify);
    IReadOnlyList<EnemyStateFormulaFeatureItem> current = workspace.Query(new(SpecializedEditorId:
        YanflyEnemyStateFormulaFeatureDefinitions.PassiveAuraEditorId, CurrentOnly: true, InstalledOnly: false, IncludeAdvanced: true));
    Assert(current.Count == 1 && current[0].Definition.PluginId == "YEP_AutoPassiveStates", "Current-only did not isolate Passive State.");
}

static void AdvancedFilter()
{
    PluginTagCatalog catalog = Embedded();
    EnemyStateFormulaWorkspace workspace = new(catalog, new(catalog, StateContext(), string.Empty), YanflyEnemyStateFormulaFeatureDefinitions.Classify);
    int basic = workspace.Query(new(SpecializedEditorId: YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId,
        InstalledOnly: false, IncludeAdvanced: false)).Count;
    int all = workspace.Query(new(SpecializedEditorId: YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId,
        InstalledOnly: false, IncludeAdvanced: true)).Count;
    Assert(all > basic, "Advanced filter did not reveal lifecycle code blocks.");
}

static void ScaledEnemyPresetResolves() => Assert(Preset(Embedded(), PluginDocumentKind.Enemy,
    YanflyEnemyStateFormulaFeatureDefinitions.EnemyDesignerEditorId, "scaled-enemy").Steps.Count == 2, "Scaled Enemy preset is incomplete.");
static void DamageOverTimePresetResolves() => Assert(Preset(Embedded(), PluginDocumentKind.State,
    YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId, "damage-over-time").Steps.Count == 2, "DoT preset is incomplete.");
static void PassivePresetResolves() => Assert(Preset(Embedded(), PluginDocumentKind.Actor,
    YanflyEnemyStateFormulaFeatureDefinitions.PassiveAuraEditorId, "passive-state").Steps.Count == 1, "Passive preset is incomplete.");
static void BarrierPresetResolves() => Assert(Preset(Embedded(), PluginDocumentKind.Enemy,
    YanflyEnemyStateFormulaFeatureDefinitions.FormulaLabEditorId, "barrier-100").Steps.Count == 1, "Barrier preset is incomplete.");

static void PresetPreservesNotes()
{
    PluginTagCatalog catalog = Embedded();
    EnemyStateFormulaWorkspace workspace = new(catalog,
        new(catalog, EnemyContext(), "Manual prose\r\n<Unknown: Keep>\r\n"), YanflyEnemyStateFormulaFeatureDefinitions.Classify);
    EnemyStateFormulaPresetPreview preview = workspace.PreviewPreset(Preset(catalog, PluginDocumentKind.Enemy,
        YanflyEnemyStateFormulaFeatureDefinitions.EnemyDesignerEditorId, "scaled-enemy"));
    Assert(preview.After.StartsWith("Manual prose\r\n<Unknown: Keep>\r\n", StringComparison.Ordinal), "Preset changed unrelated notes.");
}

static void MissingPluginWarns()
{
    PluginTagCatalog catalog = Embedded();
    EnemyStateFormulaPreset preset = Preset(catalog, PluginDocumentKind.Enemy,
        YanflyEnemyStateFormulaFeatureDefinitions.FormulaLabEditorId, "barrier-100");
    PluginFeatureAvailability unavailable = new(preset.Steps[0].Feature, PluginFeatureAvailabilityState.OwnerPluginMissing, "Plugin is missing.", []);
    EnemyStateFormulaWorkspace workspace = new(catalog, new(catalog, EnemyContext(), string.Empty),
        YanflyEnemyStateFormulaFeatureDefinitions.Classify, [unavailable]);
    EnemyStateFormulaPresetPreview preview = workspace.PreviewPreset(preset);
    Assert(preview.Warnings.Count == 1 && preview.After.Length == 0, "Missing plugin was not safely skipped.");
}

static void TypedFormulaWritePreservesNotes()
{
    PluginTagCatalog catalog = Embedded();
    PluginFeatureDefinition barrier = Find(catalog, "YEP_AbsorptionBarrier", "<Barrier Points: +");
    PluginTextEditingSession session = new(catalog, EnemyContext(), "Text\n<Unknown: Keep>\n");
    EnemyStateFormulaWorkspace workspace = new(catalog, session, YanflyEnemyStateFormulaFeatureDefinitions.Classify);
    PluginTagChangePreview preview = workspace.PreviewWrite(new(barrier.Key,
        PluginFeatureValues.FromStrings([new(barrier.Arguments[0].Name, "100")])));
    Assert(preview.After.StartsWith("Text\n<Unknown: Keep>\n", StringComparison.Ordinal) &&
        preview.After.Contains("<Barrier Points: +100>", StringComparison.Ordinal), "Typed formula write was not lossless.");
}

static void FormulaBracketValidation()
{
    IReadOnlyList<PluginFormulaIssue> issues = PluginFormulaSafetyAnalyzer.Analyze("value = (a.atk + b.def;");
    Assert(issues.Any(issue => issue.Severity == PluginFormulaIssueSeverity.Error && issue.Code == "FORMULA_BRACKET"),
        "Unclosed formula bracket was not detected.");
}

static void FormulaHostWarning()
{
    IReadOnlyList<PluginFormulaIssue> issues = PluginFormulaSafetyAnalyzer.Analyze("require('fs').readFileSync('x')");
    Assert(issues.Any(issue => issue.Code == "FORMULA_HOST_ACCESS"), "Unsafe host access was not warned about.");
}

static void SpecializedDescriptorsRegister()
{
    PluginTagCatalog catalog = Embedded();
    PluginSpecializedEditorRegistry registry = new();
    YanflyEnemyStateFormulaFeatureDefinitions.RegisterSpecializedEditors(registry, catalog);
    Assert(registry.Editors.Count == 4, "Expected four Phase 9 descriptors.");
    Assert(registry.Editors.Sum(item => item.SupportedFeatures.Count) == 535, "Descriptor coverage does not reconcile to 535.");
}

static void RuntimeFeatureCount() => Assert(Embedded().Features.Count == 2943, "Runtime feature count changed unexpectedly.");

static void NoJavaScriptEmbedded()
{
    string[] resources = typeof(YanflyGeneratedCatalogProvider).Assembly.GetManifestResourceNames();
    Assert(!resources.Any(name => name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)), "YEP JavaScript was embedded.");
}

static int Count(string editorId) => Embedded().Features.Count(feature => feature.Surface == PluginAuthoringSurface.DatabaseNote &&
    feature.SpecializedEditorId?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);

static bool IsPhase9Editor(string editorId) => editorId.Equals(YanflyEnemyStateFormulaFeatureDefinitions.EnemyDesignerEditorId, StringComparison.OrdinalIgnoreCase) ||
    editorId.Equals(YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId, StringComparison.OrdinalIgnoreCase) ||
    editorId.Equals(YanflyEnemyStateFormulaFeatureDefinitions.PassiveAuraEditorId, StringComparison.OrdinalIgnoreCase) ||
    editorId.Equals(YanflyEnemyStateFormulaFeatureDefinitions.FormulaLabEditorId, StringComparison.OrdinalIgnoreCase);

static PluginFeatureDefinition Find(PluginTagCatalog catalog, string pluginId, string syntax)
{
    PluginFeatureDefinition? feature = catalog.Features.FirstOrDefault(candidate =>
        candidate.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase) &&
        candidate.Surface == PluginAuthoringSurface.DatabaseNote &&
        (candidate.Syntax.CanonicalTemplate.Contains(syntax, StringComparison.OrdinalIgnoreCase) ||
         candidate.DisplayName.Contains(syntax, StringComparison.OrdinalIgnoreCase)));
    return feature ?? throw new InvalidOperationException($"Feature not found: {pluginId} / {syntax}");
}

static EnemyStateFormulaPreset Preset(PluginTagCatalog catalog, PluginDocumentKind kind, string editorId, string id) =>
    YanflyEnemyStateFormulaFeatureDefinitions.CreatePresets(catalog, kind, editorId).Single(preset => preset.PresetId == id);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
