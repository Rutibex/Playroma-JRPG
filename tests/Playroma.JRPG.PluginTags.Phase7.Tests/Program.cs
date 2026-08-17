using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

List<(string Name, Action Test)> tests =
[
    ("Phase 7 covers eighteen YEP plugins", CoversEighteenPlugins),
    ("All covered plugins have database features", AllCoveredPluginsHaveDatabaseFeatures),
    ("Skill features route to Skill Designer", SkillFeaturesRoute),
    ("Progression features route to Progression Designer", ProgressionFeaturesRoute),
    ("Instant Cast receives database-surface definitions", InstantCastDatabaseSurface),
    ("Subclass receives database-surface definitions", SubclassDatabaseSurface),
    ("Original event-note definitions remain available", EventDefinitionsRemain),
    ("Skill areas classify costs and cooldowns", SkillAreasClassify),
    ("Progression areas classify learning and JP", ProgressionAreasClassify),
    ("Workspace filters to skill/progression features", WorkspaceFilters),
    ("Workspace current-only filter works", CurrentOnlyFilter),
    ("Workspace advanced filter works", AdvancedFilter),
    ("Cooldown preset resolves", CooldownPresetResolves),
    ("Instant Cast preset resolves", InstantPresetResolves),
    ("JP learning preset resolves", JpPresetResolves),
    ("Preset preview preserves unrelated notes", PresetPreservesNotes),
    ("Preset preview emits expected tags", PresetEmitsTags),
    ("Missing plugin produces preset warning", MissingPluginWarns),
    ("Typed write preview remains lossless", TypedWritePreservesNotes),
    ("Specialized descriptors register", SpecializedDescriptorsRegister),
    ("Runtime feature count includes database corrections", RuntimeFeatureCount),
    ("No YEP JavaScript is embedded", NoJavaScriptEmbedded)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
}
Console.WriteLine($"Phase 7 tests: {tests.Count - failures}/{tests.Count} passed.");
return failures == 0 ? 0 : 1;

static PluginTagCatalog Embedded() => new PluginTagCatalogBuilder().AddProvider(YanflyGeneratedCatalogProvider.LoadEmbedded()).Build();
static PluginTagContext SkillContext() => new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Skill);

static void CoversEighteenPlugins() => Assert(YanflySkillProgressionFeatureDefinitions.CoveredPluginIds.Count == 18,
    $"Expected 18 plugins, found {YanflySkillProgressionFeatureDefinitions.CoveredPluginIds.Count}.");

static void AllCoveredPluginsHaveDatabaseFeatures()
{
    PluginTagCatalog catalog = Embedded();
    foreach (string plugin in YanflySkillProgressionFeatureDefinitions.CoveredPluginIds)
        Assert(catalog.Features.Any(feature => feature.PluginId.Equals(plugin, StringComparison.OrdinalIgnoreCase) &&
            feature.Surface == PluginAuthoringSurface.DatabaseNote &&
            feature.SpecializedEditorId is not null), "No database feature routed for " + plugin);
}

static void SkillFeaturesRoute()
{
    PluginTagCatalog catalog = Embedded();
    PluginFeatureDefinition feature = Find(catalog, "YEP_X_SkillCooldowns", "<Cooldown:");
    Assert(feature.SpecializedEditorId == YanflySkillProgressionFeatureDefinitions.SkillDesignerEditorId, "Cooldown did not route to Skill Designer.");
}

static void ProgressionFeaturesRoute()
{
    PluginTagCatalog catalog = Embedded();
    PluginFeatureDefinition feature = Find(catalog, "YEP_SkillLearnSystem", "<Learn Require Level:");
    Assert(feature.SpecializedEditorId == YanflySkillProgressionFeatureDefinitions.ProgressionDesignerEditorId, "Learning did not route to Progression Designer.");
}

static void InstantCastDatabaseSurface()
{
    PluginTagCatalog catalog = Embedded();
    PluginFeatureDefinition feature = Find(catalog, "YEP_InstantCast", "<Instant Cast>");
    Assert(feature.Surface == PluginAuthoringSurface.DatabaseNote && feature.DocumentKinds.Contains(PluginDocumentKind.Skill), "Instant Cast database correction missing.");
}

static void SubclassDatabaseSurface()
{
    PluginTagCatalog catalog = Embedded();
    PluginFeatureDefinition feature = Find(catalog, "YEP_X_Subclass", "<Subclass:");
    Assert(feature.Surface == PluginAuthoringSurface.DatabaseNote && feature.DocumentKinds.Contains(PluginDocumentKind.Class), "Subclass database correction missing.");
}

static void EventDefinitionsRemain()
{
    PluginTagCatalog catalog = Embedded();
    Assert(catalog.Features.Any(feature => feature.PluginId == "YEP_InstantCast" && feature.Surface == PluginAuthoringSurface.EventNote), "Instant Cast event feature was removed.");
    Assert(catalog.Features.Any(feature => feature.PluginId == "YEP_X_Subclass" && feature.Surface == PluginAuthoringSurface.EventNote), "Subclass event feature was removed.");
}

static void SkillAreasClassify()
{
    PluginTagCatalog catalog = Embedded();
    Assert(YanflySkillProgressionFeatureDefinitions.Classify(Find(catalog, "YEP_SkillCore", "<HP Cost:")) == SkillProgressionArea.Costs, "HP Cost area is wrong.");
    Assert(YanflySkillProgressionFeatureDefinitions.Classify(Find(catalog, "YEP_X_SkillCooldowns", "<Cooldown:")) == SkillProgressionArea.Cooldowns, "Cooldown area is wrong.");
}

static void ProgressionAreasClassify()
{
    PluginTagCatalog catalog = Embedded();
    Assert(YanflySkillProgressionFeatureDefinitions.Classify(Find(catalog, "YEP_SkillLearnSystem", "<Learn Cost:")) == SkillProgressionArea.Learning, "Learn Cost area is wrong.");
    Assert(YanflySkillProgressionFeatureDefinitions.Classify(Find(catalog, "YEP_JobPoints", "<Starting JP:")) == SkillProgressionArea.JobPoints, "Starting JP area is wrong.");
}

static void WorkspaceFilters()
{
    PluginTagCatalog catalog = Embedded();
    SkillProgressionWorkspace workspace = new(catalog, new(catalog, SkillContext(), string.Empty), YanflySkillProgressionFeatureDefinitions.Classify);
    IReadOnlyList<SkillProgressionFeatureItem> items = workspace.Query(new(InstalledOnly: false, IncludeAdvanced: true));
    Assert(items.Count > 200, "Skill workspace is unexpectedly small: " + items.Count);
    Assert(items.All(item => YanflySkillProgressionFeatureDefinitions.CoveredPluginIds.Contains(item.Definition.PluginId)), "Unrelated plugin entered workspace.");
}

static void CurrentOnlyFilter()
{
    PluginTagCatalog catalog = Embedded();
    SkillProgressionWorkspace workspace = new(catalog, new(catalog, SkillContext(), "<Cooldown: 3>"), YanflySkillProgressionFeatureDefinitions.Classify);
    IReadOnlyList<SkillProgressionFeatureItem> current = workspace.Query(new(CurrentOnly: true, InstalledOnly: false, IncludeAdvanced: true));
    Assert(current.Count == 1 && current[0].Definition.PluginId == "YEP_X_SkillCooldowns", "Current-only filter did not isolate cooldown.");
}

static void AdvancedFilter()
{
    PluginTagCatalog catalog = Embedded();
    SkillProgressionWorkspace workspace = new(catalog, new(catalog, SkillContext(), string.Empty), YanflySkillProgressionFeatureDefinitions.Classify);
    int basic = workspace.Query(new(InstalledOnly: false, IncludeAdvanced: false)).Count;
    int all = workspace.Query(new(InstalledOnly: false, IncludeAdvanced: true)).Count;
    Assert(all > basic, "Advanced-code filter did not reveal additional features.");
}

static void CooldownPresetResolves()
{
    PluginTagCatalog catalog = Embedded();
    SkillProgressionPreset preset = Preset(catalog, "cooldown-technique");
    Assert(preset.Steps.Count == 1 && catalog.GetRequiredFeature(preset.Steps[0].Feature).PluginId == "YEP_X_SkillCooldowns", "Cooldown preset did not resolve.");
}

static void InstantPresetResolves()
{
    PluginTagCatalog catalog = Embedded();
    SkillProgressionPreset preset = Preset(catalog, "instant-technique");
    Assert(preset.Steps.Count == 1 && catalog.GetRequiredFeature(preset.Steps[0].Feature).Surface == PluginAuthoringSurface.DatabaseNote, "Instant preset did not use database feature.");
}

static void JpPresetResolves()
{
    PluginTagCatalog catalog = Embedded();
    SkillProgressionPreset preset = Preset(catalog, "jp-learnable");
    Assert(preset.Steps.Count == 2, "JP learning preset is incomplete.");
}

static void PresetPreservesNotes()
{
    PluginTagCatalog catalog = Embedded();
    PluginTextEditingSession session = new(catalog, SkillContext(), "Manual prose\r\n<Unknown: Keep>\r\n");
    SkillProgressionWorkspace workspace = new(catalog, session, YanflySkillProgressionFeatureDefinitions.Classify);
    SkillProgressionPresetPreview preview = workspace.PreviewPreset(Preset(catalog, "cooldown-technique"));
    Assert(preview.After.StartsWith("Manual prose\r\n<Unknown: Keep>\r\n", StringComparison.Ordinal), "Preset changed unrelated notes.");
}

static void PresetEmitsTags()
{
    PluginTagCatalog catalog = Embedded();
    PluginTextEditingSession session = new(catalog, SkillContext(), string.Empty);
    SkillProgressionWorkspace workspace = new(catalog, session, YanflySkillProgressionFeatureDefinitions.Classify);
    SkillProgressionPresetPreview preview = workspace.PreviewPreset(Preset(catalog, "jp-learnable"));
    Assert(preview.After.Contains("<Learn Require Level: 5>", StringComparison.Ordinal), "Learning level tag missing.");
    Assert(preview.After.Contains("<Learn Cost: 100 JP>", StringComparison.Ordinal), "JP cost tag missing.");
}

static void MissingPluginWarns()
{
    PluginTagCatalog catalog = Embedded();
    SkillProgressionPreset preset = Preset(catalog, "cooldown-technique");
    PluginFeatureKey key = preset.Steps[0].Feature;
    PluginFeatureAvailability unavailable = new(key, PluginFeatureAvailabilityState.OwnerPluginMissing, "Plugin is missing.", []);
    SkillProgressionWorkspace workspace = new(catalog, new(catalog, SkillContext(), string.Empty), YanflySkillProgressionFeatureDefinitions.Classify, [unavailable]);
    SkillProgressionPresetPreview preview = workspace.PreviewPreset(preset);
    Assert(preview.Warnings.Count == 1 && preview.After.Length == 0, "Missing plugin was not safely skipped.");
}

static void TypedWritePreservesNotes()
{
    PluginTagCatalog catalog = Embedded();
    PluginFeatureDefinition cooldown = Find(catalog, "YEP_X_SkillCooldowns", "<Cooldown:");
    PluginTextEditingSession session = new(catalog, SkillContext(), "Text\n<Unknown: Keep>\n");
    SkillProgressionWorkspace workspace = new(catalog, session, YanflySkillProgressionFeatureDefinitions.Classify);
    PluginTagChangePreview preview = workspace.PreviewWrite(new(cooldown.Key,
        PluginFeatureValues.FromStrings([new(cooldown.Arguments[0].Name, "4")])));
    Assert(preview.After.StartsWith("Text\n<Unknown: Keep>\n", StringComparison.Ordinal) && preview.After.Contains("<Cooldown: 4>", StringComparison.Ordinal), "Typed write was not lossless.");
}

static void SpecializedDescriptorsRegister()
{
    PluginTagCatalog catalog = Embedded();
    PluginSpecializedEditorRegistry registry = new();
    YanflySkillProgressionFeatureDefinitions.RegisterSpecializedEditors(registry, catalog);
    Assert(registry.TryGet(YanflySkillProgressionFeatureDefinitions.SkillDesignerEditorId, out PluginSpecializedEditorDescriptor skill) && skill.SupportedFeatures.Count > 0, "Skill descriptor missing.");
    Assert(registry.TryGet(YanflySkillProgressionFeatureDefinitions.ProgressionDesignerEditorId, out PluginSpecializedEditorDescriptor progression) && progression.SupportedFeatures.Count > 0, "Progression descriptor missing.");
}

static void RuntimeFeatureCount()
{
    PluginTagCatalog catalog = Embedded();
    Assert(catalog.Features.Count == 2943, $"Expected 2943 runtime features, found {catalog.Features.Count}.");
    int phase7 = catalog.Features.Count(feature => feature.Surface == PluginAuthoringSurface.DatabaseNote &&
        (feature.SpecializedEditorId == YanflySkillProgressionFeatureDefinitions.SkillDesignerEditorId ||
         feature.SpecializedEditorId == YanflySkillProgressionFeatureDefinitions.ProgressionDesignerEditorId));
    Assert(phase7 == 327, $"Expected 327 Phase 7 features, found {phase7}.");
}

static void NoJavaScriptEmbedded()
{
    string[] resources = typeof(YanflyGeneratedCatalogProvider).Assembly.GetManifestResourceNames();
    Assert(!resources.Any(name => name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)), "YEP JavaScript was embedded.");
}

static PluginFeatureDefinition Find(PluginTagCatalog catalog, string pluginId, string syntax)
{
    PluginFeatureDefinition? feature = catalog.Features.FirstOrDefault(candidate =>
        candidate.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase) &&
        candidate.Surface == PluginAuthoringSurface.DatabaseNote &&
        candidate.Syntax.CanonicalTemplate.Contains(syntax, StringComparison.OrdinalIgnoreCase));
    return feature ?? throw new InvalidOperationException($"Feature not found: {pluginId} / {syntax}");
}

static SkillProgressionPreset Preset(PluginTagCatalog catalog, string id) =>
    YanflySkillProgressionFeatureDefinitions.CreatePresets(catalog, PluginDocumentKind.Skill)
        .Single(preset => preset.PresetId == id);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
