using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

List<(string Name, Action Test)> tests =
[
    ("Phase 8 covers twenty-one YEP plugins", CoversTwentyOnePlugins),
    ("All covered plugins have routed database features", AllCoveredPluginsRoute),
    ("Phase 8 routes exactly 240 features", RoutesTwoHundredFortyFeatures),
    ("Prior Item Core semantic routes are normalized", PriorRoutesNormalize),
    ("Durability classification works", DurabilityClassifies),
    ("Augment and upgrade classification works", AugmentUpgradeClassifies),
    ("Synthesis and disassembly classification works", CraftingClassifies),
    ("Shop and currency classification works", CommerceClassifies),
    ("Armor and weapon classification works", CombatEquipmentClassifies),
    ("Workspace filters to item-commerce providers", WorkspaceFilters),
    ("Current-only filtering recognizes existing tags", CurrentOnlyFilter),
    ("Advanced filtering reveals script blocks", AdvancedFilter),
    ("Unbreakable preset resolves", UnbreakablePresetResolves),
    ("Rename/discard preset resolves", RenameDiscardPresetResolves),
    ("Preset preview preserves unrelated notes", PresetPreservesNotes),
    ("Preset preview emits expected tags", PresetEmitsTags),
    ("Missing plugin safely warns and skips", MissingPluginWarns),
    ("Typed durability write remains lossless", TypedWritePreservesNotes),
    ("Item-commerce specialized descriptor registers", SpecializedDescriptorRegisters),
    ("Runtime feature count remains stable", RuntimeFeatureCount),
    ("No YEP JavaScript is embedded", NoJavaScriptEmbedded)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
}
Console.WriteLine($"Phase 8 tests: {tests.Count - failures}/{tests.Count} passed.");
return failures == 0 ? 0 : 1;

static PluginTagCatalog Embedded() => new PluginTagCatalogBuilder().AddProvider(YanflyGeneratedCatalogProvider.LoadEmbedded()).Build();
static PluginTagContext ArmorContext() => new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Armor);
static PluginTagContext ItemContext() => new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Item);

static void CoversTwentyOnePlugins() => Assert(YanflyItemCommerceFeatureDefinitions.CoveredPluginIds.Count == 21,
    $"Expected 21 plugins, found {YanflyItemCommerceFeatureDefinitions.CoveredPluginIds.Count}.");

static void AllCoveredPluginsRoute()
{
    PluginTagCatalog catalog = Embedded();
    foreach (string plugin in YanflyItemCommerceFeatureDefinitions.CoveredPluginIds)
        Assert(catalog.Features.Any(feature => feature.PluginId.Equals(plugin, StringComparison.OrdinalIgnoreCase) &&
            feature.Surface == PluginAuthoringSurface.DatabaseNote &&
            feature.SpecializedEditorId == YanflyItemCommerceFeatureDefinitions.DesignerEditorId),
            "No item-commerce database feature routed for " + plugin);
}

static void RoutesTwoHundredFortyFeatures()
{
    PluginTagCatalog catalog = Embedded();
    int count = catalog.Features.Count(feature => feature.Surface == PluginAuthoringSurface.DatabaseNote &&
        feature.SpecializedEditorId == YanflyItemCommerceFeatureDefinitions.DesignerEditorId);
    Assert(count == 240, $"Expected 240 Phase 8 features, found {count}.");
}

static void PriorRoutesNormalize()
{
    PluginTagCatalog catalog = Embedded();
    PluginFeatureDefinition feature = Find(catalog, "YEP_ItemCore", "<Random Variance:");
    Assert(feature.SpecializedEditorId == YanflyItemCommerceFeatureDefinitions.DesignerEditorId,
        "Item Core retained an obsolete specialized editor route.");
}

static void DurabilityClassifies() => Assert(
    YanflyItemCommerceFeatureDefinitions.Classify(Find(Embedded(), "YEP_X_ItemDurability", "<Durability:")) == ItemCommerceArea.Durability,
    "Durability tag classified incorrectly.");

static void AugmentUpgradeClassifies()
{
    PluginTagCatalog catalog = Embedded();
    Assert(YanflyItemCommerceFeatureDefinitions.Classify(Find(catalog, "YEP_X_AttachAugments", "<Augment Slots>")) == ItemCommerceArea.Augments,
        "Augment tag classified incorrectly.");
    Assert(YanflyItemCommerceFeatureDefinitions.Classify(Find(catalog, "YEP_X_ItemUpgradeSlots", "<Upgrade Slots:")) == ItemCommerceArea.UpgradeSlots,
        "Upgrade tag classified incorrectly.");
}

static void CraftingClassifies()
{
    PluginTagCatalog catalog = Embedded();
    Assert(YanflyItemCommerceFeatureDefinitions.Classify(Find(catalog, "YEP_ItemSynthesis", "<Synthesis Ingredients>")) == ItemCommerceArea.Synthesis,
        "Synthesis tag classified incorrectly.");
    Assert(YanflyItemCommerceFeatureDefinitions.Classify(Find(catalog, "YEP_X_ItemDisassemble", "<Disassemble Pool>")) == ItemCommerceArea.Disassembly,
        "Disassembly tag classified incorrectly.");
}

static void CommerceClassifies()
{
    PluginTagCatalog catalog = Embedded();
    Assert(YanflyItemCommerceFeatureDefinitions.Classify(Find(catalog, "YEP_X_CondShopPrices", "<Price Minimum:")) == ItemCommerceArea.ShopPrices,
        "Price tag classified incorrectly.");
    Assert(YanflyItemCommerceFeatureDefinitions.Classify(Find(catalog, "YEP_X_MoreCurrencies", "<Variable {value1} Buy Price:")) == ItemCommerceArea.Currencies,
        "Currency tag classified incorrectly.");
}

static void CombatEquipmentClassifies()
{
    PluginTagCatalog catalog = Embedded();
    Assert(YanflyItemCommerceFeatureDefinitions.Classify(Find(catalog, "YEP_X_ArmorScaling", "<Base Armor>")) == ItemCommerceArea.ArmorScaling,
        "Armor tag classified incorrectly.");
    Assert(YanflyItemCommerceFeatureDefinitions.Classify(Find(catalog, "YEP_WeaponAnimation", "<Weapon Motion: swing>")) == ItemCommerceArea.WeaponVisuals,
        "Weapon visual tag classified incorrectly.");
    Assert(YanflyItemCommerceFeatureDefinitions.Classify(Find(catalog, "YEP_WeaponUnleash", "<Replace Attack:")) == ItemCommerceArea.WeaponUnleash,
        "Weapon unleash tag classified incorrectly.");
}

static void WorkspaceFilters()
{
    PluginTagCatalog catalog = Embedded();
    ItemCommerceWorkspace workspace = new(catalog, new(catalog, ArmorContext(), string.Empty), YanflyItemCommerceFeatureDefinitions.Classify);
    IReadOnlyList<ItemCommerceFeatureItem> items = workspace.Query(new(InstalledOnly: false, IncludeAdvanced: true));
    Assert(items.Count > 150, "Armor workspace is unexpectedly small: " + items.Count);
    Assert(items.All(item => YanflyItemCommerceFeatureDefinitions.CoveredPluginIds.Contains(item.Definition.PluginId)),
        "Unrelated plugin entered item-commerce workspace.");
}

static void CurrentOnlyFilter()
{
    PluginTagCatalog catalog = Embedded();
    ItemCommerceWorkspace workspace = new(catalog, new(catalog, ArmorContext(), "<Unbreakable>"), YanflyItemCommerceFeatureDefinitions.Classify);
    IReadOnlyList<ItemCommerceFeatureItem> current = workspace.Query(new(CurrentOnly: true, InstalledOnly: false, IncludeAdvanced: true));
    Assert(current.Count == 1 && current[0].Definition.PluginId == "YEP_X_ItemDurability", "Current-only filter did not isolate Unbreakable.");
}

static void AdvancedFilter()
{
    PluginTagCatalog catalog = Embedded();
    ItemCommerceWorkspace workspace = new(catalog, new(catalog, ItemContext(), string.Empty), YanflyItemCommerceFeatureDefinitions.Classify);
    int basic = workspace.Query(new(InstalledOnly: false, IncludeAdvanced: false)).Count;
    int all = workspace.Query(new(InstalledOnly: false, IncludeAdvanced: true)).Count;
    Assert(all > basic, "Advanced filter did not reveal custom item blocks.");
}

static void UnbreakablePresetResolves()
{
    ItemCommercePreset preset = Preset(Embedded(), PluginDocumentKind.Armor, "unbreakable-equipment");
    Assert(preset.Steps.Count == 1, "Unbreakable preset is incomplete.");
}

static void RenameDiscardPresetResolves()
{
    ItemCommercePreset preset = Preset(Embedded(), PluginDocumentKind.Item, "rename-and-discard");
    Assert(preset.Steps.Count == 2, "Rename/discard preset is incomplete.");
}

static void PresetPreservesNotes()
{
    PluginTagCatalog catalog = Embedded();
    PluginTextEditingSession session = new(catalog, ArmorContext(), "Manual prose\r\n<Unknown: Keep>\r\n");
    ItemCommerceWorkspace workspace = new(catalog, session, YanflyItemCommerceFeatureDefinitions.Classify);
    ItemCommercePresetPreview preview = workspace.PreviewPreset(Preset(catalog, PluginDocumentKind.Armor, "unbreakable-equipment"));
    Assert(preview.After.StartsWith("Manual prose\r\n<Unknown: Keep>\r\n", StringComparison.Ordinal), "Preset changed unrelated notes.");
}

static void PresetEmitsTags()
{
    PluginTagCatalog catalog = Embedded();
    ItemCommerceWorkspace workspace = new(catalog, new(catalog, ItemContext(), string.Empty), YanflyItemCommerceFeatureDefinitions.Classify);
    ItemCommercePresetPreview preview = workspace.PreviewPreset(Preset(catalog, PluginDocumentKind.Item, "rename-and-discard"));
    Assert(preview.After.Contains("<Can Rename>", StringComparison.Ordinal) && preview.After.Contains("<Can Discard>", StringComparison.Ordinal),
        "Customizable item preset did not emit both tags.");
}

static void MissingPluginWarns()
{
    PluginTagCatalog catalog = Embedded();
    ItemCommercePreset preset = Preset(catalog, PluginDocumentKind.Armor, "unbreakable-equipment");
    PluginFeatureAvailability unavailable = new(preset.Steps[0].Feature, PluginFeatureAvailabilityState.OwnerPluginMissing, "Plugin is missing.", []);
    ItemCommerceWorkspace workspace = new(catalog, new(catalog, ArmorContext(), string.Empty), YanflyItemCommerceFeatureDefinitions.Classify, [unavailable]);
    ItemCommercePresetPreview preview = workspace.PreviewPreset(preset);
    Assert(preview.Warnings.Count == 1 && preview.After.Length == 0, "Missing plugin was not safely skipped.");
}

static void TypedWritePreservesNotes()
{
    PluginTagCatalog catalog = Embedded();
    PluginFeatureDefinition durability = Find(catalog, "YEP_X_ItemDurability", "<Durability:");
    PluginTextEditingSession session = new(catalog, ArmorContext(), "Text\n<Unknown: Keep>\n");
    ItemCommerceWorkspace workspace = new(catalog, session, YanflyItemCommerceFeatureDefinitions.Classify);
    PluginTagChangePreview preview = workspace.PreviewWrite(new(durability.Key,
        PluginFeatureValues.FromStrings([new(durability.Arguments[0].Name, "250")])));
    Assert(preview.After.StartsWith("Text\n<Unknown: Keep>\n", StringComparison.Ordinal) && preview.After.Contains("<Durability: 250>", StringComparison.Ordinal),
        "Typed durability write was not lossless.");
}

static void SpecializedDescriptorRegisters()
{
    PluginTagCatalog catalog = Embedded();
    PluginSpecializedEditorRegistry registry = new();
    YanflyItemCommerceFeatureDefinitions.RegisterSpecializedEditor(registry, catalog);
    Assert(registry.TryGet(YanflyItemCommerceFeatureDefinitions.DesignerEditorId, out PluginSpecializedEditorDescriptor descriptor) &&
        descriptor.SupportedFeatures.Count == 240, "Item-commerce descriptor missing or incomplete.");
}

static void RuntimeFeatureCount()
{
    PluginTagCatalog catalog = Embedded();
    Assert(catalog.Features.Count == 2943, $"Expected 2943 runtime features, found {catalog.Features.Count}.");
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

static ItemCommercePreset Preset(PluginTagCatalog catalog, PluginDocumentKind kind, string id) =>
    YanflyItemCommerceFeatureDefinitions.CreatePresets(catalog, kind).Single(preset => preset.PresetId == id);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
