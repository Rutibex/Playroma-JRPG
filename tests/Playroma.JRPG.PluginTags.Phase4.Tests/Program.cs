using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

List<(string Name, Action Test)> tests =
[
    ("Context query keeps actor features", ContextQueryKeepsActorFeatures),
    ("Installed scope hides missing owners", InstalledScopeHidesMissingOwners),
    ("All scope shows missing owners", AllScopeShowsMissingOwners),
    ("Current scope shows present features", CurrentScopeShowsPresentFeatures),
    ("Advanced features are opt in", AdvancedFeaturesAreOptIn),
    ("Upsert preview does not mutate session", UpsertPreviewDoesNotMutateSession),
    ("Upsert preview produces canonical result", UpsertPreviewProducesCanonicalResult),
    ("Remove preview is non destructive", RemovePreviewIsNonDestructive),
    ("Diff identifies additions and removals", DiffIdentifiesChanges),
    ("Blocking availability prevents authoring", BlockingAvailabilityPreventsAuthoring),
    ("Categories are deterministic", CategoriesAreDeterministic),
    ("Raw note changes refresh current features", RawNoteChangesRefreshCurrentFeatures),
    ("Unknown note text survives preview", UnknownTextSurvivesPreview),
    ("Embedded YEP catalog supports database notes", EmbeddedCatalogSupportsDatabaseNotes),
    ("Search matches semantic fields", SearchMatchesSemanticFields),
    ("Category filter narrows results", CategoryFilterNarrowsResults),
    ("Unverified source remains authorable", UnverifiedSourceRemainsAuthorable),
    ("Preview preserves CRLF preference", PreviewPreservesCrLfPreference),
    ("Current scope retains unavailable present tags", CurrentScopeRetainsUnavailablePresentTags)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
}
Console.WriteLine($"Phase 4 tests: {tests.Count - failures}/{tests.Count} passed.");
return failures == 0 ? 0 : 1;

static void ContextQueryKeepsActorFeatures()
{
    Fixture fixture = new();
    Assert(fixture.Workspace.Query(new()).Any(item => item.Definition.FeatureId == "actor.flag"), "Actor feature missing.");
    Assert(!fixture.Workspace.Query(new()).Any(item => item.Definition.FeatureId == "skill.flag"), "Skill feature leaked into actor context.");
}

static void InstalledScopeHidesMissingOwners()
{
    Fixture fixture = new();
    fixture.Workspace.SetAvailability([Missing(fixture.ActorFlag.Key)]);
    Assert(!fixture.Workspace.Query(new(Scope: PluginFeatureBrowserScope.Installed))
        .Any(item => item.Definition.Key == fixture.ActorFlag.Key), "Missing owner was visible in Installed scope.");
}

static void AllScopeShowsMissingOwners()
{
    Fixture fixture = new();
    fixture.Workspace.SetAvailability([Missing(fixture.ActorFlag.Key)]);
    Assert(fixture.Workspace.Query(new(Scope: PluginFeatureBrowserScope.All)).Count == 2, "All scope did not retain unavailable features.");
}

static void CurrentScopeShowsPresentFeatures()
{
    Fixture fixture = new("<Actor Flag>\n");
    IReadOnlyList<PluginFeatureBrowserItem> current = fixture.Workspace.Query(new(Scope: PluginFeatureBrowserScope.Current));
    Assert(current.Count == 1 && current[0].Definition.Key == fixture.ActorFlag.Key, "Current scope was incorrect.");
}

static void AdvancedFeaturesAreOptIn()
{
    Fixture fixture = new();
    Assert(!fixture.Workspace.Query(new(Scope: PluginFeatureBrowserScope.All)).Any(item => item.Definition.Key == fixture.Advanced.Key), "Advanced feature was visible by default.");
    Assert(fixture.Workspace.Query(new(Scope: PluginFeatureBrowserScope.All, IncludeAdvanced: true)).Any(item => item.Definition.Key == fixture.Advanced.Key), "Advanced feature did not appear when requested.");
}

static void UpsertPreviewDoesNotMutateSession()
{
    Fixture fixture = new("Prose\n");
    PluginTagChangePreview preview = fixture.Workspace.PreviewUpsert(fixture.ActorFlag.Key, PluginFeatureValues.Empty);
    Assert(fixture.Session.WorkingText == "Prose\n", "Preview mutated the real editing session.");
    Assert(preview.HasChanges, "Preview did not report a change.");
}

static void UpsertPreviewProducesCanonicalResult()
{
    Fixture fixture = new("Prose\n");
    PluginTagChangePreview preview = fixture.Workspace.PreviewUpsert(fixture.ValueTag.Key,
        PluginFeatureValues.FromStrings([new("value", "7")]));
    Assert(preview.GeneratedSyntax == "<Actor Value: 7>", "Canonical syntax was incorrect.");
    Assert(preview.After == "Prose\n<Actor Value: 7>", "Preview result was incorrect.");
}

static void RemovePreviewIsNonDestructive()
{
    Fixture fixture = new("<Actor Flag>\nOther\n");
    PluginTagChangePreview preview = fixture.Workspace.PreviewRemove(fixture.ActorFlag.Key);
    Assert(preview.After == "Other\n", "Removal preview was incorrect.");
    Assert(fixture.Session.WorkingText == "<Actor Flag>\nOther\n", "Removal preview mutated the real session.");
}

static void DiffIdentifiesChanges()
{
    IReadOnlyList<PluginTagLineChange> changes = PluginTagTextDiff.Create("A\nB", "A\nC");
    Assert(changes.Any(change => change.Prefix == "-" && change.Text == "B"), "Removed line missing.");
    Assert(changes.Any(change => change.Prefix == "+" && change.Text == "C"), "Added line missing.");
}

static void BlockingAvailabilityPreventsAuthoring()
{
    Fixture fixture = new();
    fixture.Workspace.SetAvailability([Missing(fixture.ActorFlag.Key)]);
    bool blocked = false;
    try { fixture.Workspace.PreviewUpsert(fixture.ActorFlag.Key, PluginFeatureValues.Empty); }
    catch (PluginTextMutationException) { blocked = true; }
    Assert(blocked, "Blocked feature produced an authoring preview.");
}

static void CategoriesAreDeterministic()
{
    Fixture fixture = new();
    string[] first = fixture.Workspace.Categories(new(Scope: PluginFeatureBrowserScope.All, IncludeAdvanced: true)).ToArray();
    string[] second = fixture.Workspace.Categories(new(Scope: PluginFeatureBrowserScope.All, IncludeAdvanced: true)).ToArray();
    Assert(first.SequenceEqual(second), "Category ordering changed between queries.");
    Assert(first.Contains("Actors and Classes"), "Actor category was not assigned.");
}

static void RawNoteChangesRefreshCurrentFeatures()
{
    Fixture fixture = new();
    fixture.Session.ReplaceText("<Actor Value: 4>");
    IReadOnlyList<PluginFeatureBrowserItem> current = fixture.Workspace.Query(new(Scope: PluginFeatureBrowserScope.Current));
    Assert(current.Single().Definition.Key == fixture.ValueTag.Key, "Workspace did not follow raw session text.");
}

static void UnknownTextSurvivesPreview()
{
    Fixture fixture = new("Manual prose\n<OtherPlugin: abc>\n");
    PluginTagChangePreview preview = fixture.Workspace.PreviewUpsert(fixture.ActorFlag.Key, PluginFeatureValues.Empty);
    Assert(preview.After.StartsWith("Manual prose\n<OtherPlugin: abc>\n", StringComparison.Ordinal), "Unknown text was changed.");
}

static void EmbeddedCatalogSupportsDatabaseNotes()
{
    PluginTagCatalog catalog = new PluginTagCatalogBuilder().AddProvider(YanflyGeneratedCatalogProvider.LoadEmbedded()).Build();
    PluginTagContext actor = new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Actor);
    PluginTagContext skill = new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Skill);
    Assert(catalog.Query(actor).Count > 0, "Embedded catalog has no Actor database features.");
    Assert(catalog.Query(skill).Count > 0, "Embedded catalog has no Skill database features.");
}


static void SearchMatchesSemanticFields()
{
    Fixture fixture = new();
    IReadOnlyList<PluginFeatureBrowserItem> result = fixture.Workspace.Query(
        new(SearchText: "parameter", Scope: PluginFeatureBrowserScope.All));
    Assert(result.Count == 1 && result[0].Definition.Key == fixture.ValueTag.Key,
        "Semantic search did not match the feature summary.");
}

static void CategoryFilterNarrowsResults()
{
    Fixture fixture = new();
    IReadOnlyList<PluginFeatureBrowserItem> result = fixture.Workspace.Query(
        new(Category: "Actors and Classes", Scope: PluginFeatureBrowserScope.All));
    Assert(result.Count == 1 && result[0].Definition.Key == fixture.ValueTag.Key,
        "Category filter returned the wrong features.");
}

static void UnverifiedSourceRemainsAuthorable()
{
    Fixture fixture = new();
    fixture.Workspace.SetAvailability([
        new PluginFeatureAvailability(fixture.ActorFlag.Key,
            PluginFeatureAvailabilityState.SourceUnverified,
            "Source differs from the verified copy.", ["YEP_Test"])
    ]);
    PluginTagChangePreview preview = fixture.Workspace.PreviewUpsert(
        fixture.ActorFlag.Key, PluginFeatureValues.Empty);
    Assert(preview.HasChanges, "An unverified source incorrectly blocked authoring.");
}

static void PreviewPreservesCrLfPreference()
{
    Fixture fixture = new("Prose\r\n");
    PluginTagChangePreview preview = fixture.Workspace.PreviewUpsert(
        fixture.ActorFlag.Key, PluginFeatureValues.Empty);
    Assert(preview.After == "Prose\r\n<Actor Flag>",
        "Preview did not preserve the record's CRLF convention.");
}

static void CurrentScopeRetainsUnavailablePresentTags()
{
    Fixture fixture = new("<Actor Flag>");
    fixture.Workspace.SetAvailability([Missing(fixture.ActorFlag.Key)]);
    IReadOnlyList<PluginFeatureBrowserItem> current = fixture.Workspace.Query(
        new(Scope: PluginFeatureBrowserScope.Current));
    Assert(current.Count == 1 && current[0].Definition.Key == fixture.ActorFlag.Key,
        "Current scope hid a present tag because its owner was unavailable.");
}

static PluginFeatureAvailability Missing(PluginFeatureKey key) =>
    new(key, PluginFeatureAvailabilityState.OwnerPluginMissing, "Owner missing", ["YEP_Test"]);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class Fixture
{
    public Fixture(string text = "")
    {
        ActorFlag = Feature("actor.flag", "Actor Battle Flag", "<Actor Flag>", "^<Actor Flag>$");
        ValueTag = new PluginFeatureDefinition(
            "TEST", "YEP_Test", "actor.value", "Actor Value", "Actor parameter value",
            PluginAuthoringSurface.DatabaseNote, new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor },
            new PluginSyntaxDefinition(PluginSyntaxKind.KeyValue, "<Actor Value: {value}>",
                [new PluginSyntaxPattern("^<Actor Value: (?<value>[0-9]+)>$")]),
            [new PluginArgumentDefinition("value", "Value", PluginArgumentKind.Integer, true)]);
        SkillFlag = new PluginFeatureDefinition(
            "TEST", "YEP_Test", "skill.flag", "Skill Flag", "Skill only",
            PluginAuthoringSurface.DatabaseNote, new HashSet<PluginDocumentKind> { PluginDocumentKind.Skill },
            new PluginSyntaxDefinition(PluginSyntaxKind.Flag, "<Skill Flag>", [new PluginSyntaxPattern("^<Skill Flag>$")]), []);
        Advanced = new PluginFeatureDefinition(
            "TEST", "YEP_Test", "actor.advanced", "Actor Script", "Advanced actor formula",
            PluginAuthoringSurface.DatabaseNote, new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor },
            new PluginSyntaxDefinition(PluginSyntaxKind.ScriptBlock, "<Actor Script>", [new PluginSyntaxPattern("^<Actor Script>$")],
                "</Actor Script>", [new PluginSyntaxPattern("^</Actor Script>$")]), [], Complexity: PluginFeatureComplexity.AdvancedCode);
        PluginTagCatalog catalog = new PluginTagCatalogBuilder()
            .AddProvider(new PluginProviderManifest("TEST", "Test", "MV", "1"))
            .AddFeature(ActorFlag).AddFeature(ValueTag).AddFeature(SkillFlag).AddFeature(Advanced).Build();
        Session = new PluginTextEditingSession(catalog,
            new PluginTagContext(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Actor), text);
        Workspace = new PluginTagAuthoringWorkspace(catalog, Session,
        [
            Available(ActorFlag.Key), Available(ValueTag.Key), Available(SkillFlag.Key), Available(Advanced.Key)
        ]);
    }

    public PluginFeatureDefinition ActorFlag { get; }
    public PluginFeatureDefinition ValueTag { get; }
    public PluginFeatureDefinition SkillFlag { get; }
    public PluginFeatureDefinition Advanced { get; }
    public PluginTextEditingSession Session { get; }
    public PluginTagAuthoringWorkspace Workspace { get; }

    private static PluginFeatureDefinition Feature(string id, string name, string template, string pattern) => new(
        "TEST", "YEP_Test", id, name, "Actor battle feature",
        PluginAuthoringSurface.DatabaseNote, new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor },
        new PluginSyntaxDefinition(PluginSyntaxKind.Flag, template, [new PluginSyntaxPattern(pattern)]), []);

    private static PluginFeatureAvailability Available(PluginFeatureKey key) =>
        new(key, PluginFeatureAvailabilityState.Available, "Ready", []);
}
