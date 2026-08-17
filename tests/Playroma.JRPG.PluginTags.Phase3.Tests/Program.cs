using System.Text;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

List<(string Name, Func<Task> Test)> tests =
[
    ("Embedded intelligence profile covers normalized bundle", EmbeddedProfileCoversBundle),
    ("Source inspector extracts version fingerprint and aliases", SourceInspectorExtractsMetadata),
    ("Known source is available", KnownSourceIsAvailable),
    ("Missing dependency is an error", MissingDependencyIsError),
    ("Disabled dependency is an error", DisabledDependencyIsError),
    ("Dependency load order is validated", DependencyOrderIsValidated),
    ("Battle systems conflict", BattleSystemsConflict),
    ("Duplicate manifest entries are rejected", DuplicateManifestEntriesAreRejected),
    ("Missing scripts are errors", MissingScriptsAreErrors),
    ("Imported alias collisions are reported", ImportedAliasCollisionsAreReported),
    ("Unregistered scripts are reported", UnregisteredScriptsAreReported),
    ("Unknown versions and fingerprints are retained", UnknownSourceIsRetained),
    ("Feature availability follows plugin status", FeatureAvailabilityFollowsStatus),
    ("Suggested moves are deterministic", SuggestedMovesAreDeterministic),
    ("Project scanner reads plugins.js without executing JavaScript", ProjectScannerReadsManifest),
    ("Report writer emits JSON and Markdown", ReportWriterEmitsFiles),
    ("No plugin JavaScript is embedded", NoPluginJavaScriptEmbedded)
];

int failures = 0;
foreach ((string name, Func<Task> test) in tests)
{
    try { await test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
}
Console.WriteLine($"Phase 3 tests: {tests.Count - failures}/{tests.Count} passed.");
return failures == 0 ? 0 : 1;

static Task EmbeddedProfileCoversBundle()
{
    PluginIntelligenceProfile profile = new YanflyPluginIntelligenceProfileFactory().CreateEmbedded();
    Assert(profile.Plugins.Count == 218, "Expected 218 normalized plugin profiles.");
    Assert(profile.Dependencies.Count > 50, "Expected the evidence-backed dependency graph.");
    Assert(profile.LoadOrderRules.Any(rule => rule.BeforePluginId == "YEP_BattleEngineCore" && rule.AfterPluginId == "YEP_X_ActSeqPack1"), "Action Sequence ordering rule missing.");
    return Task.CompletedTask;
}

static Task SourceInspectorExtractsMetadata()
{
    const string source = "/*: @plugindesc v1.23 Test */\nvar Imported = Imported || {}; Imported.YEP_TestPlugin = true; Yanfly.Test.version = 1.23;";
    YanflyInstalledSourceInspection inspection = new YanflyInstalledSourceInspector().Inspect("YEP_TestPlugin", Encoding.UTF8.GetBytes(source));
    Assert(inspection.Version == "1.23", "Version was not extracted.");
    Assert(inspection.ImportedAliases.Contains("YEP_TestPlugin"), "Imported alias missing.");
    Assert(inspection.SourceFingerprint.Length == 64, "SHA-256 fingerprint missing.");
    Assert(inspection.AppearsToBeYanflyPlugin, "YEP identity was not detected.");
    return Task.CompletedTask;
}

static Task KnownSourceIsAvailable()
{
    YanflyBundleInventory inventory = YanflyInventorySerializer.LoadEmbeddedPhase0Inventory();
    YanflyPluginInventoryEntry core = inventory.Plugins.Single(plugin => plugin.PluginId == "YEP_CoreEngine");
    InstalledPluginAnalysis analysis = new YanflyInstalledPluginIntelligence().Analyze([
        new InstalledPluginDescriptor(1, core.PluginId, true, ScriptExists: true, Version: core.Version, SourceFingerprint: core.CanonicalSha256)
    ]);
    Assert(analysis.Find(core.PluginId)?.PrimaryState == InstalledPluginCompatibilityState.Available, "Verified core plugin was not available.");
    return Task.CompletedTask;
}

static Task MissingDependencyIsError()
{
    PluginIntelligenceProfile profile = FixtureProfile();
    InstalledPluginAnalysis analysis = new InstalledPluginIntelligenceAnalyzer().Analyze([
        Plugin(1, "YEP_Extension", true)
    ], profile);
    Assert(analysis.Issues.Any(issue => issue.Code == "PI1101" && issue.Severity == InstalledPluginIssueSeverity.Error), "Missing required dependency was not reported.");
    Assert(analysis.Find("YEP_Extension")!.States.Contains(InstalledPluginCompatibilityState.MissingDependency), "Missing-dependency state absent.");
    return Task.CompletedTask;
}

static Task DisabledDependencyIsError()
{
    InstalledPluginAnalysis analysis = new InstalledPluginIntelligenceAnalyzer().Analyze([
        Plugin(1, "YEP_Core", false), Plugin(2, "YEP_Extension", true)
    ], FixtureProfile());
    Assert(analysis.Issues.Any(issue => issue.Code == "PI1102"), "Disabled dependency was not reported.");
    return Task.CompletedTask;
}

static Task DependencyOrderIsValidated()
{
    InstalledPluginAnalysis analysis = new InstalledPluginIntelligenceAnalyzer().Analyze([
        Plugin(1, "YEP_Extension", true), Plugin(2, "YEP_Core", true)
    ], FixtureProfile());
    Assert(analysis.Issues.Any(issue => issue.Code is "PI1201" or "PI1202"), "Load order violation was not reported.");
    return Task.CompletedTask;
}

static Task BattleSystemsConflict()
{
    PluginIntelligenceProfile profile = new(
        "YEP-MV",
        [Profile("YEP_X_BattleSysATB"), Profile("YEP_X_BattleSysCTB")],
        [], [],
        [new PluginConflictRule("battle", ["YEP_X_BattleSysATB", "YEP_X_BattleSysCTB"], "Only one battle system.")]);
    InstalledPluginAnalysis analysis = new InstalledPluginIntelligenceAnalyzer().Analyze([
        Plugin(1, "YEP_X_BattleSysATB", true), Plugin(2, "YEP_X_BattleSysCTB", true)
    ], profile);
    Assert(analysis.Issues.Count(issue => issue.Code == "PI1301") == 2, "Conflict was not attached to both plugins.");
    return Task.CompletedTask;
}

static Task DuplicateManifestEntriesAreRejected()
{
    InstalledPluginAnalysis analysis = new InstalledPluginIntelligenceAnalyzer().Analyze([
        Plugin(1, "YEP_Core", true), Plugin(2, "YEP_Core", true)
    ], FixtureProfile());
    Assert(analysis.Issues.Any(issue => issue.Code == "PI1001"), "Duplicate manifest entry was not detected.");
    return Task.CompletedTask;
}

static Task MissingScriptsAreErrors()
{
    InstalledPluginAnalysis analysis = new InstalledPluginIntelligenceAnalyzer().Analyze([
        new InstalledPluginDescriptor(1, "YEP_Core", true, ScriptExists: false, Version: "1.00", SourceFingerprint: new string('0', 64))
    ], FixtureProfile());
    Assert(analysis.Issues.Any(issue => issue.Code == "PI1003" && issue.Severity == InstalledPluginIssueSeverity.Error), "Missing script was not an error.");
    return Task.CompletedTask;
}

static Task ImportedAliasCollisionsAreReported()
{
    InstalledPluginAnalysis analysis = new InstalledPluginIntelligenceAnalyzer().Analyze([
        new InstalledPluginDescriptor(1, "YEP_Core", true, ScriptExists: true, Version: "1.00", SourceFingerprint: new string('0', 64), ImportedAliases: ["YEP_Shared"]),
        new InstalledPluginDescriptor(2, "YEP_Extension", true, ScriptExists: true, Version: "1.00", SourceFingerprint: new string('0', 64), ImportedAliases: ["YEP_Shared"])
    ], FixtureProfile());
    Assert(analysis.Issues.Count(issue => issue.Code == "PI1302") == 2, "Imported alias collision was not attached to both plugins.");
    return Task.CompletedTask;
}

static Task UnregisteredScriptsAreReported()
{
    InstalledPluginAnalysis analysis = new InstalledPluginIntelligenceAnalyzer().Analyze([
        Plugin(1, "YEP_Core", true)
    ], FixtureProfile(), ["YEP_Core", "YEP_Orphan"]);
    Assert(analysis.UnregisteredScriptPluginIds.SequenceEqual(["YEP_Orphan"]), "Unregistered script list is incorrect.");
    Assert(analysis.Issues.Any(issue => issue.Code == "PI1401"), "Unregistered script diagnostic missing.");
    return Task.CompletedTask;
}

static Task UnknownSourceIsRetained()
{
    InstalledPluginAnalysis analysis = new InstalledPluginIntelligenceAnalyzer().Analyze([
        new InstalledPluginDescriptor(1, "YEP_Core", true, ScriptExists: true, Version: "9.99", SourceFingerprint: new string('a', 64))
    ], FixtureProfile());
    Assert(analysis.Find("YEP_Core")!.States.Contains(InstalledPluginCompatibilityState.UnknownVersion), "Unknown version state missing.");
    Assert(analysis.Find("YEP_Core")!.States.Contains(InstalledPluginCompatibilityState.UnverifiedSource), "Unverified source state missing.");
    return Task.CompletedTask;
}

static Task FeatureAvailabilityFollowsStatus()
{
    InstalledPluginIntelligenceAnalyzer analyzer = new();
    InstalledPluginAnalysis analysis = analyzer.Analyze([Plugin(1, "YEP_Extension", true)], FixtureProfile());
    PluginFeatureDefinition feature = new(
        "YEP-MV", "YEP_Extension", "test.feature", "Test", "Test feature",
        PluginAuthoringSurface.DatabaseNote, new HashSet<PluginDocumentKind> { PluginDocumentKind.Actor },
        new PluginSyntaxDefinition(PluginSyntaxKind.Flag, "<Test>", [new PluginSyntaxPattern("^<Test>$")]),
        [], RequiredPlugins: ["YEP_Core"]);
    PluginFeatureAvailability availability = analyzer.EvaluateFeatures([feature], analysis).Single();
    Assert(availability.State == PluginFeatureAvailabilityState.RequiredPluginMissing, "Feature did not inherit missing dependency state.");
    Assert(!availability.CanAuthor, "Blocked feature was marked authorable.");
    return Task.CompletedTask;
}

static Task SuggestedMovesAreDeterministic()
{
    InstalledPluginIntelligenceAnalyzer analyzer = new();
    InstalledPluginDescriptor[] plugins = [Plugin(1, "YEP_Extension", true), Plugin(2, "YEP_Core", true)];
    InstalledPluginAnalysis first = analyzer.Analyze(plugins, FixtureProfile());
    InstalledPluginAnalysis second = analyzer.Analyze(plugins, FixtureProfile());
    Assert(first.SuggestedMoves.SequenceEqual(second.SuggestedMoves), "Suggested moves changed between identical analyses.");
    Assert(first.SuggestedMoves.Any(move => move.PluginId == "YEP_Core"), "Core move was not suggested.");
    return Task.CompletedTask;
}

static async Task ProjectScannerReadsManifest()
{
    using ProjectFixture fixture = new();
    YanflyProjectPluginIntelligenceScanner scanner = new(new YanflyInstalledPluginIntelligence(FixtureProfile()));
    YanflyProjectPluginScanResult result = await scanner.ScanAsync(fixture.Root);
    Assert(result.Analysis.Entries.Count == 2, "Manifest entries were not parsed.");
    Assert(result.Analysis.Find("YEP_Extension")?.Installed.Version == "1.00", "Script version was not inspected.");
    Assert(result.Analysis.Issues.All(issue => issue.Code != "PI1401"), "Registered scripts were reported as orphaned.");
}

static Task ReportWriterEmitsFiles()
{
    string output = Path.Combine(Path.GetTempPath(), "playroma-phase3-report-" + Guid.NewGuid().ToString("N"));
    try
    {
        InstalledPluginAnalysis analysis = new InstalledPluginIntelligenceAnalyzer().Analyze([Plugin(1, "YEP_Core", true)], FixtureProfile());
        new YanflyPluginIntelligenceReportWriter().Write(analysis, output);
        Assert(File.Exists(Path.Combine(output, "yanfly-phase3-installed-analysis.json")), "JSON report missing.");
        Assert(File.Exists(Path.Combine(output, "YANFLY_PHASE3_INSTALLED_REPORT.md")), "Markdown report missing.");
    }
    finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    return Task.CompletedTask;
}

static Task NoPluginJavaScriptEmbedded()
{
    string[] resources = typeof(YanflyInstalledPluginIntelligence).Assembly.GetManifestResourceNames();
    Assert(resources.All(name => !name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)), "YEP JavaScript was embedded.");
    return Task.CompletedTask;
}

static PluginIntelligenceProfile FixtureProfile() => new(
    "YEP-MV",
    [Profile("YEP_Core"), Profile("YEP_Extension")],
    [new PluginDependencyRule("YEP_Extension", "YEP_Core", PluginDependencyStrength.Required, "fixture")],
    [new PluginLoadOrderRule("YEP_Core", "YEP_Extension", "Core must load first.")],
    []);

static KnownPluginProfile Profile(string id) => new(id, id, "YEP-MV", [id + ".js"], [new KnownPluginVersion("1.00", [new string('0', 64)])]);
static InstalledPluginDescriptor Plugin(int order, string id, bool enabled) => new(order, id, enabled, ScriptExists: true, Version: "1.00", SourceFingerprint: new string('0', 64), ImportedAliases: [id]);
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

sealed class ProjectFixture : IDisposable
{
    public ProjectFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "playroma-phase3-project-" + Guid.NewGuid().ToString("N"));
        string js = Path.Combine(Root, "js");
        string plugins = Path.Combine(js, "plugins");
        Directory.CreateDirectory(plugins);
        File.WriteAllText(Path.Combine(js, "plugins.js"), "var $plugins = [" +
            "{\"name\":\"YEP_Core\",\"status\":true,\"description\":\"\",\"parameters\":{}}," +
            "{\"name\":\"YEP_Extension\",\"status\":true,\"description\":\"\",\"parameters\":{}}];");
        File.WriteAllText(Path.Combine(plugins, "YEP_Core.js"), Source("YEP_Core"));
        File.WriteAllText(Path.Combine(plugins, "YEP_Extension.js"), Source("YEP_Extension"));
    }

    public string Root { get; }
    private static string Source(string id) => $"/*: @plugindesc v1.00 {id} */\\nvar Imported = Imported || {{}}; Imported.{id} = true;";
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
