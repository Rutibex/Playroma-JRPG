using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

List<(string Name, Action Test)> tests =
[
    ("All argument kinds have generic mappings", AllArgumentKindsMapped),
    ("Embedded database coverage reconciles", EmbeddedCoverageReconciles),
    ("No non-specialized database feature is uncovered", GenericDatabaseCoverageIsComplete),
    ("Typed boolean values canonicalize", BooleanCanonicalizes),
    ("Numeric bounds are enforced", NumericBoundsAreEnforced),
    ("Identifier lists canonicalize", IdentifierListsCanonicalize),
    ("Identifier ranges normalize", IdentifierRangesNormalize),
    ("Colors validate", ColorsValidate),
    ("Coordinates normalize", CoordinatesNormalize),
    ("Singleton smart writes replace", SingletonSmartReplace),
    ("Repeatable smart writes append", RepeatableSmartAppend),
    ("Repeatable-by-key smart writes update matching key", KeyedSmartUpdate),
    ("Mergeable smart writes merge identifiers", MergeableSmartMerge),
    ("Conflict fail blocks the plan", ConflictFailBlocks),
    ("Conflict keep skips without error", ConflictKeepSkips),
    ("Conflict removal is explicit", ConflictRemovalWorks),
    ("Batch planning is non destructive", BatchPlanningIsNonDestructive),
    ("Stale batch application is rejected", StaleBatchIsRejected),
    ("Beginner recipes resolve", BeginnerRecipesResolve),
    ("Recipes filter by record context", RecipesFilterByContext),
    ("Recipe previews preserve existing text", RecipePreviewPreservesText),
    ("Asset and audio editor hints resolve", AssetHintsResolve),
    ("Advanced generic code remains covered", AdvancedGenericCodeIsCovered),
    ("Specialized features remain deferred", SpecializedFeaturesRemainDeferred)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
}
Console.WriteLine($"Phase 5 tests: {tests.Count - failures}/{tests.Count} passed.");
return failures == 0 ? 0 : 1;

static PluginTagCatalog Embedded() => new PluginTagCatalogBuilder()
    .AddProvider(YanflyGeneratedCatalogProvider.LoadEmbedded()).Build();

static void AllArgumentKindsMapped()
{
    foreach (PluginArgumentKind kind in Enum.GetValues<PluginArgumentKind>())
    {
        PluginFeatureDefinition feature = Feature("kind." + kind, "Kind " + kind, "<Kind: {value}>",
            [new PluginArgumentDefinition("value", "Value", kind)]);
        PluginFeatureEditorPlan plan = PluginGenericEditorCoverage.CreatePlan(feature);
        Assert(plan.Arguments.Count == 1, "Missing editor mapping for " + kind);
    }
    Assert(Enum.GetValues<PluginArgumentKind>().Length == 37, "Argument-kind contract changed; review Phase 5 coverage.");
}

static void EmbeddedCoverageReconciles()
{
    PluginTagCatalog catalog = Embedded();
    PluginFeatureDefinition[] database = catalog.Features.Where(IsDatabaseSurface).ToArray();
    PluginFeatureEditorPlan[] plans = database.Select(PluginGenericEditorCoverage.CreatePlan).ToArray();
    Assert(database.Length == 1434, $"Expected 1434 database/tileset features, found {database.Length}.");
    Assert(plans.Count(plan => plan.IsCompletelyCovered) == 1132, "Generic complete count changed.");
    Assert(plans.Count(plan => plan.RequiresSpecializedEditor) == 302, "Specialized deferred count changed.");
}

static void GenericDatabaseCoverageIsComplete()
{
    PluginFeatureEditorPlan[] plans = Embedded().Features.Where(IsDatabaseSurface)
        .Select(PluginGenericEditorCoverage.CreatePlan).ToArray();
    Assert(plans.Where(plan => !plan.RequiresSpecializedEditor).All(plan => plan.IsCompletelyCovered),
        "A non-specialized database feature lacks generic coverage.");
}

static void BooleanCanonicalizes()
{
    PluginArgumentDefinition argument = new("enabled", "Enabled", PluginArgumentKind.Boolean, true);
    Assert(PluginTypedValueCodec.Canonicalize(argument, "1") == "true", "Boolean did not canonicalize.");
}

static void NumericBoundsAreEnforced()
{
    PluginArgumentDefinition argument = new("value", "Value", PluginArgumentKind.Decimal, true, -2m, 2m);
    Assert(PluginTypedValueCodec.Parse(argument, "-1.5").IsValid, "Valid negative decimal was rejected.");
    Assert(!PluginTypedValueCodec.Parse(argument, "3").IsValid, "Out-of-range number was accepted.");
}

static void IdentifierListsCanonicalize()
{
    PluginArgumentDefinition argument = new("ids", "IDs", PluginArgumentKind.DatabaseIdList, true);
    Assert(PluginTypedValueCodec.Canonicalize(argument, "3, 1, 3, 2") == "3, 1, 2", "ID list canonicalization changed order or duplicates.");
}

static void IdentifierRangesNormalize()
{
    PluginArgumentDefinition argument = new("range", "Range", PluginArgumentKind.DatabaseIdRange, true);
    Assert(PluginTypedValueCodec.Canonicalize(argument, "9 to 3") == "3 to 9", "Range was not normalized.");
}

static void ColorsValidate()
{
    PluginArgumentDefinition argument = new("color", "Color", PluginArgumentKind.Color, true);
    Assert(PluginTypedValueCodec.Parse(argument, "#12ABEF").IsValid, "Hex color was rejected.");
    Assert(!PluginTypedValueCodec.Parse(argument, "#12ZZEF").IsValid, "Invalid color was accepted.");
}

static void CoordinatesNormalize()
{
    PluginArgumentDefinition argument = new("point", "Point", PluginArgumentKind.Coordinate, true);
    Assert(PluginTypedValueCodec.Canonicalize(argument, "4  7") == "4, 7", "Coordinate was not normalized.");
}

static void SingletonSmartReplace()
{
    BatchFixture fixture = new("<Single: 1>");
    PluginTagBatchPlan plan = fixture.Plan(fixture.Single, Values(("value", "2")));
    Assert(plan.Records.Single().After == "<Single: 2>", "Singleton was not replaced.");
}

static void RepeatableSmartAppend()
{
    BatchFixture fixture = new("<Repeat: 1>");
    PluginTagBatchPlan plan = fixture.Plan(fixture.Repeat, Values(("value", "2")));
    Assert(plan.Records.Single().After.Contains("<Repeat: 1>", StringComparison.Ordinal) &&
           plan.Records.Single().After.Contains("<Repeat: 2>", StringComparison.Ordinal), "Repeatable feature did not append.");
}

static void KeyedSmartUpdate()
{
    BatchFixture fixture = new("<Key: fire, 1>\n<Key: ice, 2>");
    PluginTagBatchPlan plan = fixture.Plan(fixture.Keyed, Values(("key", "ice"), ("value", "9")));
    Assert(plan.Records.Single().After == "<Key: fire, 1>\n<Key: ice, 9>", "Keyed occurrence was not updated in place.");
}

static void MergeableSmartMerge()
{
    BatchFixture fixture = new("<List: 3, 1>");
    PluginTagBatchPlan plan = fixture.Plan(fixture.Mergeable, Values(("ids", "2, 3")));
    Assert(plan.Records.Single().After == "<List: 1, 2, 3>", "Mergeable IDs were not merged deterministically.");
}

static void ConflictFailBlocks()
{
    BatchFixture fixture = new("<Conflict A>");
    PluginTagBatchPlan plan = fixture.Plan(fixture.ConflictB, PluginFeatureValues.Empty,
        conflict: PluginConflictResolution.Fail);
    Assert(!plan.CanApply && plan.ErrorCount == 1, "Conflict fail did not block the plan.");
}

static void ConflictKeepSkips()
{
    BatchFixture fixture = new("<Conflict A>");
    PluginTagBatchPlan plan = fixture.Plan(fixture.ConflictB, PluginFeatureValues.Empty,
        conflict: PluginConflictResolution.KeepExisting);
    Assert(plan.CanApply && plan.ChangedCount == 0 && plan.Records.Single().Warnings.Count == 1,
        "Keep-existing conflict was not a clean skip.");
}

static void ConflictRemovalWorks()
{
    BatchFixture fixture = new("<Conflict A>");
    PluginTagBatchPlan plan = fixture.Plan(fixture.ConflictB, PluginFeatureValues.Empty,
        conflict: PluginConflictResolution.RemoveConflicting);
    Assert(plan.CanApply && plan.Records.Single().After == "<Conflict B>", "Conflicting feature was not replaced.");
}

static void BatchPlanningIsNonDestructive()
{
    BatchFixture fixture = new("Manual\n");
    PluginTagBatchPlan plan = fixture.Plan(fixture.Single, Values(("value", "4")));
    Assert(fixture.Session.WorkingText == "Manual\n", "Planning mutated the real session.");
    Assert(plan.ChangedCount == 1, "Planning did not identify the change.");
}

static void StaleBatchIsRejected()
{
    BatchFixture fixture = new();
    PluginTagBatchPlan plan = fixture.Plan(fixture.Single, Values(("value", "4")));
    fixture.Session.ReplaceText("changed");
    bool rejected = false;
    try { new PluginTagBatchPlanner(fixture.Catalog).Apply(plan, new Dictionary<string, PluginTextEditingSession> { ["1"] = fixture.Session }); }
    catch (PluginTextMutationException) { rejected = true; }
    Assert(rejected, "Stale plan was applied.");
}

static void BeginnerRecipesResolve()
{
    PluginTagRecipeCatalog recipes = YanflyBeginnerRecipes.Create(Embedded());
    PluginTagContext actor = new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Actor);
    Assert(recipes.Query(actor).Count >= 7, "Actor recipes did not resolve.");
    HashSet<string> all = Enum.GetValues<PluginDocumentKind>()
        .SelectMany(kind => recipes.Query(new PluginTagContext(PluginAuthoringSurface.DatabaseNote, kind)))
        .Select(recipe => recipe.RecipeId).ToHashSet(StringComparer.Ordinal);
    Assert(all.Count == 12, $"Expected 12 beginner recipes, found {all.Count}.");
}

static void RecipesFilterByContext()
{
    PluginTagRecipeCatalog recipes = YanflyBeginnerRecipes.Create(Embedded());
    IReadOnlyList<PluginTagRecipeDefinition> enemy = recipes.Query(new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Enemy));
    IReadOnlyList<PluginTagRecipeDefinition> item = recipes.Query(new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Item));
    Assert(enemy.Any(recipe => recipe.RecipeId == "hide-enemy-level"), "Enemy recipe missing.");
    Assert(!item.Any(recipe => recipe.RecipeId == "hide-enemy-level"), "Enemy-only recipe leaked into items.");
}

static void RecipePreviewPreservesText()
{
    PluginTagCatalog catalog = Embedded();
    PluginTagRecipeCatalog recipes = YanflyBeginnerRecipes.Create(catalog);
    PluginTagContext context = new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Actor);
    PluginTextEditingSession session = new(catalog, context, "Manual prose\r\n<Unknown: value>\r\n");
    PluginTagRecipeDefinition recipe = recipes.Query(context).Single(item => item.RecipeId == "barrier-shield");
    PluginTagRecipePreview preview = recipes.Preview(session, recipe);
    Assert(preview.After.StartsWith("Manual prose\r\n<Unknown: value>\r\n", StringComparison.Ordinal), "Recipe changed existing text.");
    Assert(session.WorkingText == "Manual prose\r\n<Unknown: value>\r\n", "Recipe preview mutated the session.");
}

static void AssetHintsResolve()
{
    PluginFeatureDefinition picture = Feature("asset.picture", "Profile Picture", "<Picture: {file}>",
        [new PluginArgumentDefinition("file", "Picture File", PluginArgumentKind.AssetFile)]);
    PluginFeatureDefinition music = Feature("audio.bgm", "Battle BGM", "<BGM: {file}>",
        [new PluginArgumentDefinition("file", "Music File", PluginArgumentKind.AudioFile)]);
    Assert(PluginGenericEditorCoverage.CreatePlan(picture).Arguments.Single().AssetCategoryHint == "Pictures", "Picture hint failed.");
    Assert(PluginGenericEditorCoverage.CreatePlan(music).Arguments.Single().AssetCategoryHint == "BackgroundMusic", "BGM hint failed.");
}

static void AdvancedGenericCodeIsCovered()
{
    PluginFeatureDefinition feature = new("TEST", "YEP_Test", "script", "Script", "Advanced formula",
        PluginAuthoringSurface.DatabaseNote, new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor },
        new PluginSyntaxDefinition(PluginSyntaxKind.ScriptBlock, "<Script>", [new PluginSyntaxPattern("^<Script>$")],
            "</Script>", [new PluginSyntaxPattern("^</Script>$")]),
        [new PluginArgumentDefinition("code", "Code", PluginArgumentKind.JavaScriptBlock)],
        Complexity: PluginFeatureComplexity.AdvancedCode);
    PluginFeatureEditorPlan plan = PluginGenericEditorCoverage.CreatePlan(feature);
    Assert(plan.IsCompletelyCovered && plan.IsAdvancedCode && plan.BodyEditor == PluginGenericEditorKind.JavaScriptBlock,
        "Advanced code did not receive generic coverage.");
}

static void SpecializedFeaturesRemainDeferred()
{
    PluginFeatureDefinition feature = Feature("special", "Special", "<Special>", [], specializedEditorId: "ai-priority");
    PluginFeatureEditorPlan plan = PluginGenericEditorCoverage.CreatePlan(feature);
    Assert(plan.RequiresSpecializedEditor && !plan.IsCompletelyCovered, "Specialized feature was marked generically complete.");
}

static bool IsDatabaseSurface(PluginFeatureDefinition feature) =>
    feature.Surface is PluginAuthoringSurface.DatabaseNote or PluginAuthoringSurface.TilesetNote;

static PluginFeatureValues Values(params (string Name, string Value)[] values) =>
    PluginFeatureValues.FromStrings(values.Select(value => new KeyValuePair<string, string>(value.Name, value.Value)));

static PluginFeatureDefinition Feature(
    string id, string name, string template, IReadOnlyList<PluginArgumentDefinition> arguments,
    PluginFeatureMultiplicity multiplicity = PluginFeatureMultiplicity.Singleton,
    IReadOnlyList<string>? conflicts = null, string? specializedEditorId = null)
{
    string pattern = "^" + System.Text.RegularExpressions.Regex.Escape(template)
        .Replace("\\{value\\}", "(?<value>.+)")
        .Replace("\\{key\\}", "(?<key>[^,>]+)")
        .Replace("\\{ids\\}", "(?<ids>[^>]+)") + "$";
    return new("TEST", "YEP_Test", id, name, name,
        PluginAuthoringSurface.DatabaseNote, new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor },
        new PluginSyntaxDefinition(arguments.Count == 0 ? PluginSyntaxKind.Flag : PluginSyntaxKind.KeyValue,
            template, [new PluginSyntaxPattern(pattern)]), arguments, multiplicity,
        ConflictGroups: conflicts, SpecializedEditorId: specializedEditorId);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class BatchFixture
{
    public BatchFixture(string text = "")
    {
        Single = Feature("single", "Single", "<Single: {value}>", [new("value", "Value", PluginArgumentKind.Integer, true)]);
        Repeat = Feature("repeat", "Repeat", "<Repeat: {value}>", [new("value", "Value", PluginArgumentKind.Integer, true)], PluginFeatureMultiplicity.Repeatable);
        Keyed = Feature("keyed", "Keyed", "<Key: {key}, {value}>",
            [new("key", "Key", PluginArgumentKind.Text, true), new("value", "Value", PluginArgumentKind.Integer, true)], PluginFeatureMultiplicity.RepeatableByKey);
        Mergeable = Feature("merge", "Merge", "<List: {ids}>",
            [new("ids", "IDs", PluginArgumentKind.DatabaseIdList, true, AllowMultiple: true)], PluginFeatureMultiplicity.Mergeable);
        ConflictA = Feature("conflict.a", "Conflict A", "<Conflict A>", [], conflicts: ["mode"]);
        ConflictB = Feature("conflict.b", "Conflict B", "<Conflict B>", [], conflicts: ["mode"]);
        Catalog = new PluginTagCatalogBuilder().AddProvider(new PluginProviderManifest("TEST", "Test", "MV", "1"))
            .AddFeature(Single).AddFeature(Repeat).AddFeature(Keyed).AddFeature(Mergeable).AddFeature(ConflictA).AddFeature(ConflictB).Build();
        Session = new PluginTextEditingSession(Catalog, new(PluginAuthoringSurface.DatabaseNote, PluginDocumentKind.Actor), text);
    }

    public PluginTagCatalog Catalog { get; }
    public PluginTextEditingSession Session { get; }
    public PluginFeatureDefinition Single { get; }
    public PluginFeatureDefinition Repeat { get; }
    public PluginFeatureDefinition Keyed { get; }
    public PluginFeatureDefinition Mergeable { get; }
    public PluginFeatureDefinition ConflictA { get; }
    public PluginFeatureDefinition ConflictB { get; }

    private static PluginFeatureDefinition Feature(
        string id, string name, string template, IReadOnlyList<PluginArgumentDefinition> arguments,
        PluginFeatureMultiplicity multiplicity = PluginFeatureMultiplicity.Singleton,
        IReadOnlyList<string>? conflicts = null, string? specializedEditorId = null)
    {
        string pattern = "^" + System.Text.RegularExpressions.Regex.Escape(template)
            .Replace("\\{value\\}", "(?<value>.+)")
            .Replace("\\{key\\}", "(?<key>[^,>]+)")
            .Replace("\\{ids\\}", "(?<ids>[^>]+)") + "$";
        return new("TEST", "YEP_Test", id, name, name,
            PluginAuthoringSurface.DatabaseNote, new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor },
            new PluginSyntaxDefinition(arguments.Count == 0 ? PluginSyntaxKind.Flag : PluginSyntaxKind.KeyValue,
                template, [new PluginSyntaxPattern(pattern)]), arguments, multiplicity,
            ConflictGroups: conflicts, SpecializedEditorId: specializedEditorId);
    }

    public PluginTagBatchPlan Plan(PluginFeatureDefinition feature, PluginFeatureValues values,
        PluginFeatureWriteMode mode = PluginFeatureWriteMode.Smart,
        PluginConflictResolution conflict = PluginConflictResolution.Fail) =>
        new PluginTagBatchPlanner(Catalog).Plan([new PluginTagBatchRecord("1", Session)],
            new PluginTagWriteRequest(feature.Key, values, mode, conflict));
}
