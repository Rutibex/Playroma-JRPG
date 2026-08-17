using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

List<(string Name, Action Test)> tests =
[
    ("Phase 10 routes 774 actionable features", TotalFeatureCount),
    ("Phase 10 covers eighty-nine plugins", CoveredPluginCount),
    ("Map notes route 77 features", MapFeatureCount),
    ("Tileset notes route 9 features", TilesetFeatureCount),
    ("True event notes route 22 features", EventFeatureCount),
    ("Event-page comments route 54 features", CommentFeatureCount),
    ("Actionable plugin commands route 607 features", PluginCommandCount),
    ("Move-route helpers route 5 features", MoveRouteCount),
    ("Plugin command documentation headings are excluded", CommandHeadingsExcluded),
    ("False event-note classifications remain outside Phase 10", FalseEventNotesExcluded),
    ("Region Rules routes 66 map features", RegionRuleCount),
    ("General Map Tags route 11 features", GeneralMapCount),
    ("Map workspace filters to region rules", MapWorkspaceFilters),
    ("Event comment workspace recognizes current tags", CommentWorkspaceCurrent),
    ("Leading comment extraction preserves lines", CommentExtraction),
    ("Leading comment replacement uses 108 and 408", CommentReplacement),
    ("Comment removal preserves later commands", CommentRemoval),
    ("Plugin command builder renders exact command", PluginCommandRender),
    ("Move-route builder renders exact script", MoveRouteRender),
    ("Map tag preview preserves unrelated note text", MapPreviewPreservesNotes),
    ("Seven specialized descriptors register", SpecializedDescriptorsRegister),
    ("Runtime feature count remains stable", RuntimeFeatureCount),
    ("No YEP JavaScript is embedded", NoJavaScriptEmbedded)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
}
Console.WriteLine($"Phase 10 tests: {tests.Count - failures}/{tests.Count} passed.");
return failures == 0 ? 0 : 1;

static PluginTagCatalog Embedded() => new PluginTagCatalogBuilder().AddProvider(YanflyGeneratedCatalogProvider.LoadEmbedded()).Build();
static bool IsPhase10(PluginFeatureDefinition feature) => YanflyMapEventFeatureDefinitions.IsPhase10Feature(feature);

static void TotalFeatureCount() => Assert(Embedded().Features.Count(IsPhase10) == 774, "Expected 774 Phase 10 features.");
static void CoveredPluginCount() => Assert(Embedded().Features.Where(IsPhase10).Select(feature => feature.PluginId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 89,
    "Expected 89 covered plugins.");
static void MapFeatureCount() => Assert(CountSurface(PluginAuthoringSurface.MapNote) == 77, "Map-note count changed.");
static void TilesetFeatureCount() => Assert(CountSurface(PluginAuthoringSurface.TilesetNote) == 9, "Tileset-note count changed.");
static void EventFeatureCount() => Assert(CountSurface(PluginAuthoringSurface.EventNote) == 22, "Event-note count changed.");
static void CommentFeatureCount() => Assert(CountSurface(PluginAuthoringSurface.EventPageComment) == 54, "Comment-tag count changed.");
static void PluginCommandCount() => Assert(CountSurface(PluginAuthoringSurface.PluginCommand) == 607, "Plugin-command count changed.");
static void MoveRouteCount() => Assert(CountSurface(PluginAuthoringSurface.MoveRouteScript) == 5, "Move-route count changed.");

static void CommandHeadingsExcluded()
{
    PluginTagCatalog catalog = Embedded();
    PluginFeatureDefinition[] headings = catalog.Features.Where(feature => feature.Surface == PluginAuthoringSurface.PluginCommand &&
        !YanflyMapEventFeatureDefinitions.IsActionablePluginCommand(feature)).ToArray();
    Assert(headings.Length == 117, "Expected 117 documentation headings.");
    Assert(headings.All(feature => feature.SpecializedEditorId != YanflyMapEventFeatureDefinitions.PluginCommandEditorId),
        "A documentation heading was routed into the command builder.");
}

static void FalseEventNotesExcluded()
{
    PluginTagCatalog catalog = Embedded();
    Assert(catalog.Features.Where(feature => feature.Surface == PluginAuthoringSurface.EventNote &&
        feature.PluginId.Equals("YEP_InstantCast", StringComparison.OrdinalIgnoreCase))
        .All(feature => feature.SpecializedEditorId != YanflyMapEventFeatureDefinitions.EventTagsEditorId),
        "Instant Cast database syntax entered the event-note editor.");
    Assert(catalog.Features.Where(feature => feature.Surface == PluginAuthoringSurface.EventNote &&
        feature.PluginId.Equals("YEP_X_Subclass", StringComparison.OrdinalIgnoreCase))
        .All(feature => feature.SpecializedEditorId != YanflyMapEventFeatureDefinitions.EventTagsEditorId),
        "Subclass database syntax entered the event-note editor.");
}

static void RegionRuleCount() => Assert(CountEditor(YanflyMapEventFeatureDefinitions.RegionRulesEditorId) == 66, "Region rule count changed.");
static void GeneralMapCount() => Assert(CountEditor(YanflyMapEventFeatureDefinitions.MapTagsEditorId) == 11, "General map count changed.");

static void MapWorkspaceFilters()
{
    PluginTagCatalog catalog = Embedded();
    PluginTagContext context = new(PluginAuthoringSurface.MapNote, PluginDocumentKind.Map);
    PluginTextEditingSession session = new(catalog, context, string.Empty);
    MapEventAuthoringWorkspace workspace = new(catalog, session, YanflyMapEventFeatureDefinitions.Classify);
    IReadOnlyList<MapEventFeatureItem> items = workspace.Query(new(SpecializedEditorId: YanflyMapEventFeatureDefinitions.RegionRulesEditorId,
        InstalledOnly: false, IncludeAdvanced: true));
    Assert(items.Count == 66, "Region workspace did not return 66 features.");
    Assert(items.All(item => item.Definition.Surface == PluginAuthoringSurface.MapNote), "Non-map syntax entered Region Rules.");
}

static void CommentWorkspaceCurrent()
{
    PluginTagCatalog catalog = Embedded();
    PluginTagContext context = new(PluginAuthoringSurface.EventPageComment, PluginDocumentKind.EventPage);
    PluginTextEditingSession session = new(catalog, context, "<Click Trigger>\nManual comment");
    MapEventAuthoringWorkspace workspace = new(catalog, session, YanflyMapEventFeatureDefinitions.Classify);
    IReadOnlyList<MapEventFeatureItem> items = workspace.Query(new(CurrentOnly: true, InstalledOnly: false, IncludeAdvanced: true));
    Assert(items.Count == 1 && items[0].Definition.PluginId == "YEP_EventClickTrigger", "Current comment tag was not recognized.");
}

static void CommentExtraction()
{
    PluginEventCommandLine[] rows = [new(108, 2, "<Click Trigger>"), new(408, 2, "Manual comment"), new(111, 2, string.Empty)];
    EventPageCommentBlock block = EventPageCommentSynchronizer.ExtractLeading(rows);
    Assert(block.Length == 2 && block.Indent == 2 && block.Text == "<Click Trigger>\nManual comment", "Leading comment block was parsed incorrectly.");
}

static void CommentReplacement()
{
    PluginEventCommandLine[] rows = [new(111, 1, string.Empty), new(0, 0, string.Empty)];
    IReadOnlyList<PluginEventCommandLine> updated = EventPageCommentSynchronizer.ReplaceLeading(rows, "<Click Trigger>\n<Activation Radius: 3>");
    Assert(updated[0].Code == 108 && updated[1].Code == 408 && updated[2].Code == 111, "Comment commands were not serialized as 108/408.");
}

static void CommentRemoval()
{
    PluginEventCommandLine[] rows = [new(108, 0, "<Click Trigger>"), new(408, 0, "Keep?"), new(355, 0, "script")];
    IReadOnlyList<PluginEventCommandLine> updated = EventPageCommentSynchronizer.ReplaceLeading(rows, string.Empty);
    Assert(updated.Count == 1 && updated[0].Code == 355, "Removing comments changed later commands.");
}

static void PluginCommandRender()
{
    PluginFeatureDefinition command = Embedded().Features.First(feature => feature.Surface == PluginAuthoringSurface.PluginCommand &&
        feature.Syntax.CanonicalTemplate.Equals("RefreshMap", StringComparison.Ordinal));
    PluginCommandRenderResult rendered = PluginCommandLineBuilder.Render(command, PluginFeatureValues.Empty);
    Assert(rendered.CommandLine == "RefreshMap", "Plugin command rendered incorrectly.");
}

static void MoveRouteRender()
{
    PluginFeatureDefinition helper = Embedded().Features.First(feature => feature.Surface == PluginAuthoringSurface.MoveRouteScript &&
        feature.Syntax.CanonicalTemplate.Equals("this.clearIconOnEvent()", StringComparison.Ordinal));
    Assert(MoveRouteScriptBuilder.Render(helper, PluginFeatureValues.Empty) == "this.clearIconOnEvent()", "Move-route helper rendered incorrectly.");
}

static void MapPreviewPreservesNotes()
{
    PluginTagCatalog catalog = Embedded();
    PluginFeatureDefinition regionEvent = catalog.Features.First(feature => feature.PluginId == "YEP_RegionEvents" &&
        feature.Surface == PluginAuthoringSurface.MapNote);
    PluginTextEditingSession session = new(catalog, new(PluginAuthoringSurface.MapNote, PluginDocumentKind.Map),
        "Manual prose\r\n<Unknown: Keep>\r\n");
    MapEventAuthoringWorkspace workspace = new(catalog, session, YanflyMapEventFeatureDefinitions.Classify);
    PluginFeatureValues values = PluginFeatureValues.FromStrings([
        new KeyValuePair<string, string>(regionEvent.Arguments[0].Name, "5"),
        new KeyValuePair<string, string>(regionEvent.Arguments[1].Name, "3")]);
    PluginTagChangePreview preview = workspace.PreviewWrite(new(regionEvent.Key, values));
    Assert(preview.After.StartsWith("Manual prose\r\n<Unknown: Keep>\r\n", StringComparison.Ordinal), "Map preview changed unrelated text.");
    Assert(preview.After.Contains("<Region 5 Event: 3>", StringComparison.Ordinal), "Region event tag was not generated.");
}

static void SpecializedDescriptorsRegister()
{
    PluginTagCatalog catalog = Embedded();
    PluginSpecializedEditorRegistry registry = new();
    YanflyMapEventFeatureDefinitions.RegisterSpecializedEditors(registry, catalog);
    Assert(registry.Editors.Count == 7, "Expected seven Phase 10 descriptors.");
    Assert(registry.Editors.Sum(editor => editor.SupportedFeatures.Count) == 774, "Descriptor coverage does not reconcile to 774.");
}

static void RuntimeFeatureCount() => Assert(Embedded().Features.Count == 2943, "Runtime feature count changed unexpectedly.");
static void NoJavaScriptEmbedded() => Assert(!typeof(YanflyGeneratedCatalogProvider).Assembly.GetManifestResourceNames()
    .Any(name => name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)), "YEP JavaScript was embedded.");

static int CountSurface(PluginAuthoringSurface surface) => Embedded().Features.Count(feature => IsPhase10(feature) && feature.Surface == surface);
static int CountEditor(string editorId) => Embedded().Features.Count(feature =>
    feature.SpecializedEditorId?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
