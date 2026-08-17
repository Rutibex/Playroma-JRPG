using System.Security.Cryptography;
using System.Text;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

List<(string Name, Action Test)> tests =
[
    ("Embedded catalog loads", EmbeddedCatalogLoads),
    ("Embedded catalog satisfies core builder contracts", EmbeddedCatalogBuilds),
    ("Semantic region feature exists", SemanticRegionFeatureExists),
    ("Help parser extracts sections and parameters", HelpParserExtracts),
    ("Discovery pairs advanced blocks", DiscoveryPairsBlocks),
    ("Compiler classifies every synthetic discovery", CompilerClassifiesEverything),
    ("Compiler emits valid provider catalog", CompilerEmitsCatalog),
    ("Report writer emits complete artifact set", ReportWriterEmitsArtifacts),
    ("Golden verifier accepts identical compilation", GoldenVerifierAcceptsIdentity),
    ("Golden verifier detects feature change", GoldenVerifierDetectsChange),
    ("Generated catalog has no duplicate keys", GeneratedCatalogHasNoDuplicateKeys),
    ("Generated catalog covers every authoring surface family", GeneratedCatalogCoversSurfaces),
    ("No plugin JavaScript is embedded", NoPluginJavaScriptEmbedded)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
}
Console.WriteLine($"Phase 2 tests: {tests.Count - failures}/{tests.Count} passed.");
return failures == 0 ? 0 : 1;

static void EmbeddedCatalogLoads()
{
    YanflyGeneratedCatalogProvider provider = YanflyGeneratedCatalogProvider.LoadEmbedded();
    Assert(provider.Manifest.ProviderId == YanflyProviderIdentity.ProviderId, "Wrong provider ID.");
    Assert(provider.Features.Count >= 2500, "Expected the generated full-collection catalog.");
}

static void EmbeddedCatalogBuilds()
{
    YanflyGeneratedCatalogProvider provider = YanflyGeneratedCatalogProvider.LoadEmbedded();
    PluginTagCatalog catalog = new PluginTagCatalogBuilder().AddProvider(provider).Build();
    Assert(catalog.Features.Count == provider.Features.Count, "Embedded YEP catalog failed core catalog validation.");
    Assert(catalog.Features.All(feature => !string.IsNullOrWhiteSpace(feature.DisplayName)), "Embedded YEP catalog contains an empty display name.");
}

static void SemanticRegionFeatureExists()
{
    YanflyGeneratedCatalogProvider provider = YanflyGeneratedCatalogProvider.LoadEmbedded();
    PluginFeatureDefinition feature = provider.Features.Single(f => f.FeatureId == "region-events.region-common-event");
    Assert(feature.Surface == PluginAuthoringSurface.MapNote, "Region event must be a map note.");
    Assert(feature.Arguments.Any(a => a.ReferenceKind == PluginReferenceKind.CommonEvent), "Common-event reference was not typed.");
}

static void HelpParserExtracts()
{
    using SyntheticFixture fixture = new();
    YanflyCanonicalPluginSource source = fixture.Source();
    YanflyHelpDocument document = new YanflyHelpDocumentParser().Parse(source);
    Assert(document.Sections.Any(s => s.Heading == "Notetags"), "Notetag section missing.");
    Assert(document.Parameters.Count == 1 && document.Parameters[0].Name == "Speed", "Parameter parsing failed.");
}

static void DiscoveryPairsBlocks()
{
    using SyntheticFixture fixture = new();
    YanflyCanonicalPluginSource source = fixture.Source();
    YanflyHelpDocument document = new YanflyHelpDocumentParser().Parse(source);
    IReadOnlyList<YanflySyntaxDiscovery> discoveries = new YanflySyntaxDiscoveryEngine().Discover(source, document);
    YanflySyntaxDiscovery opening = discoveries.Single(d => d.Syntax == "<Custom Test>" && d.Disposition != YanflyDiscoveryDisposition.Duplicate);
    Assert(opening.Kind == YanflyDiscoveryKind.BlockOpening, "Opening tag was not paired.");
    Assert(opening.PairedDiscoveryId is not null, "Closing discovery was not linked.");
    Assert(opening.Disposition == YanflyDiscoveryDisposition.AdvancedRawFeature, "Lunatic block was not marked advanced/raw.");
}

static void CompilerClassifiesEverything()
{
    using SyntheticFixture fixture = new();
    YanflyCatalogCompilation compilation = fixture.Compile();
    Assert(compilation.Coverage.RequiresReviewCount == 0, "A discovery remained unclassified.");
    Assert(compilation.Discoveries.All(d => Enum.IsDefined(d.Disposition)), "Invalid disposition found.");
}

static void CompilerEmitsCatalog()
{
    using SyntheticFixture fixture = new();
    YanflyCatalogCompilation compilation = fixture.Compile();
    Assert(compilation.Success, "Synthetic compilation failed.");
    Assert(compilation.Catalog.Features.Any(f => f.Syntax.CanonicalTemplate.Contains("Test Value", StringComparison.Ordinal)), "Typed notetag feature missing.");
    PluginTagCatalog catalog = new PluginTagCatalogSerializer().BuildCatalog(compilation.Catalog);
    Assert(catalog.Features.Count == compilation.Catalog.Features.Count, "Generated catalog did not satisfy core contracts.");
}

static void ReportWriterEmitsArtifacts()
{
    using SyntheticFixture fixture = new();
    YanflyCatalogCompilation compilation = fixture.Compile();
    string output = Path.Combine(Path.GetTempPath(), "playroma-phase2-report-" + Guid.NewGuid().ToString("N"));
    try
    {
        new YanflyCatalogReportWriter().WriteAll(compilation, output);
        string[] required = ["yanfly-phase2-compilation.json", "yanfly-phase2-catalog.json", "yanfly-phase2-discoveries.json", "yanfly-phase2-parser-verification.json", "yanfly-phase2-help-sections.json", "yanfly-phase2-plugin-parameters.json", "yanfly-phase2-coverage.json", "yanfly-phase2-diagnostics.json", "yanfly-phase2-coverage.csv", "YANFLY_PHASE2_CATALOG_REPORT.md"];
        foreach (string name in required) Assert(File.Exists(Path.Combine(output, name)), "Missing report: " + name);
    }
    finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
}

static void GoldenVerifierAcceptsIdentity()
{
    using SyntheticFixture fixture = new();
    YanflyCatalogCompilation compilation = fixture.Compile();
    Assert(new YanflyCatalogGoldenVerifier().Verify(compilation, compilation).Success, "Identical catalog failed verification.");
}

static void GoldenVerifierDetectsChange()
{
    using SyntheticFixture fixture = new();
    YanflyCatalogCompilation expected = fixture.Compile();
    PluginTagCatalogDocument changedCatalog = expected.Catalog with { Features = expected.Catalog.Features.Skip(1).ToArray() };
    YanflyCatalogCompilation changed = expected with { Catalog = changedCatalog };
    Assert(!new YanflyCatalogGoldenVerifier().Verify(expected, changed).Success, "Missing feature was not detected.");
}

static void GeneratedCatalogHasNoDuplicateKeys()
{
    YanflyGeneratedCatalogProvider provider = YanflyGeneratedCatalogProvider.LoadEmbedded();
    Assert(provider.Features.Select(f => f.Key).Distinct().Count() == provider.Features.Count, "Duplicate generated keys found.");
}

static void GeneratedCatalogCoversSurfaces()
{
    YanflyGeneratedCatalogProvider provider = YanflyGeneratedCatalogProvider.LoadEmbedded();
    PluginAuthoringSurface[] required = [PluginAuthoringSurface.DatabaseNote, PluginAuthoringSurface.MapNote, PluginAuthoringSurface.TilesetNote, PluginAuthoringSurface.EventNote, PluginAuthoringSurface.EventPageComment, PluginAuthoringSurface.PluginCommand, PluginAuthoringSurface.ActionSequence, PluginAuthoringSurface.MessageTextCode, PluginAuthoringSurface.ScriptCall];
    foreach (PluginAuthoringSurface surface in required) Assert(provider.Features.Any(f => f.Surface == surface), "Missing generated surface: " + surface);
}

static void NoPluginJavaScriptEmbedded()
{
    string[] resources = typeof(YanflyGeneratedCatalogProvider).Assembly.GetManifestResourceNames();
    Assert(resources.All(name => !name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)), "YEP JavaScript was embedded.");
}

static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

sealed class SyntheticFixture : IDisposable
{
    private const string PluginId = "YEP_TestCatalog";
    private readonly string root = Path.Combine(Path.GetTempPath(), "playroma-phase2-" + Guid.NewGuid().ToString("N"));
    private readonly string sourceText = """
/*:
 * @plugindesc v1.00 Synthetic catalog fixture
 * @param Speed
 * @type number
 * @default 4
 * @help
 * ============================================================================
 * Notetags
 * ============================================================================
 * Actor Notetag:
 *   <Test Value: x>
 *
 * ============================================================================
 * Lunatic Mode - Custom Test
 * ============================================================================
 * Actor Notetag:
 *   <Custom Test>
 *   value += 1;
 *   </Custom Test>
 *
 * ============================================================================
 * Plugin Commands
 * ============================================================================
 * TestCommand x
 * - Changes the synthetic value.
 */
var Imported = Imported || {};
Imported.YEP_TestCatalog = true;
Yanfly.TestCatalog.version = 1.00;
var noteRegex = /<TEST VALUE:[ ](\\d+)>/i;
Game_Interpreter.prototype.pluginCommand = function(command, args) {
  if (command === 'TestCommand') return;
};
""";

    public SyntheticFixture()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, PluginId + ".js"), sourceText);
    }

    public YanflyCanonicalPluginSource Source() => new(PluginId, PluginId + ".js", PluginId + ".js", "1.00", Hash(), sourceText, Inventory().Plugins.Single());

    public YanflyCatalogCompilation Compile() => new YanflyCatalogCompiler().Compile(root, Inventory(), YanflySemanticOverlaySerializer.Empty("test"), DateTimeOffset.UnixEpoch);

    private YanflyBundleInventory Inventory()
    {
        string hash = Hash();
        YanflyPluginInventoryEntry plugin = new(
            PluginId, PluginId + ".js", "Synthetic Catalog Fixture", YanflySourceFamily.YanflyEnginePlugins,
            "1.00", "Tests", PluginId + ".js", hash, YanflyDuplicateKind.Unique,
            YanflyResolutionReason.SoleOccurrence,
            [new YanflyBundleOccurrence(PluginId + ".js", PluginId + ".js", "Tests", Encoding.UTF8.GetByteCount(sourceText), hash, "1.00", false)],
            [PluginId], [YanflyAuthoringSurface.DatabaseNote, YanflyAuthoringSurface.PluginCommand, YanflyAuthoringSurface.PluginParameter],
            ["Actor"], [YanflyCoverageMode.TypedCatalog, YanflyCoverageMode.ContextAssistant, YanflyCoverageMode.PluginConfiguration],
            "Phase 2 — Complete YEP catalog compiler", 3, true, true, []);
        YanflyBundleSummary summary = new(1, 0, 1, 1, 1, 0, 0, 0, 0,
            new Dictionary<string, int> { ["Tests"] = 1 },
            new Dictionary<YanflyAuthoringSurface, int> { [YanflyAuthoringSurface.DatabaseNote] = 1 },
            new Dictionary<YanflyCoverageMode, int> { [YanflyCoverageMode.TypedCatalog] = 1 });
        return new(1, "YEP-MV", "RPG Maker MV", "synthetic", DateTimeOffset.UnixEpoch, summary, [plugin]);
    }

    private string Hash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceText))).ToLowerInvariant();
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
