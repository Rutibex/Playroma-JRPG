using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.PluginTags.Yanfly;

string repositoryRoot = RepositoryRootLocator.Find();
List<(string Name, Action Test)> tests =
[
    ("Phase 0 constitution", Constitution),
    ("Golden inventory counts", GoldenInventoryCounts),
    ("Complete plugin classification", CompleteClassification),
    ("Known duplicate resolutions", DuplicateResolutions),
    ("Dependency graph integrity", DependencyGraphIntegrity),
    ("Licensing boundary", LicensingBoundary),
    ("Synthetic archive audit", SyntheticArchiveAudit),
    ("Unknown conflict detection", UnknownConflictDetection),
    ("Golden verifier", GoldenVerifier),
    ("Report output", ReportOutput)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
    }
}

Console.WriteLine($"{tests.Count - failures}/{tests.Count} Yanfly Phase 0 tests passed.");
return failures == 0 ? 0 : 1;

void Constitution()
{
    True(YanflyPhase0Constitution.Promises.Count >= 10);
    Equal(YanflyPhase0Constitution.Promises.Count, YanflyPhase0Constitution.Promises.Select(promise => promise.Id).Distinct(StringComparer.Ordinal).Count());
    True(YanflyPhase0Constitution.Promises.Any(promise => promise.Id == "no-source-redistribution"));
    True(YanflyPhase0Constitution.Promises.Any(promise => promise.Id == "explicit-conflicts"));
    True(YanflyPhase0Constitution.Phase0ExitConditions.Count >= 8);
}

void GoldenInventoryCounts()
{
    YanflyBundleInventory inventory = YanflyInventorySerializer.LoadEmbeddedPhase0Inventory();
    Equal(1, inventory.SchemaVersion);
    Equal("YEP-MV", inventory.ProviderId);
    Equal(259, inventory.Summary.TotalJavaScriptFiles);
    Equal(2, inventory.Summary.IgnoredSeparatorFiles);
    Equal(257, inventory.Summary.PluginOccurrences);
    Equal(218, inventory.Summary.UniquePlugins);
    Equal(217, inventory.Summary.YanflyPlugins);
    Equal(1, inventory.Summary.IntegrationPlugins);
    Equal(39, inventory.Summary.DuplicateFileNameGroups);
    Equal(37, inventory.Summary.ExactDuplicateGroups);
    Equal(2, inventory.Summary.ConflictingDuplicateGroups);
    Equal(218, inventory.Plugins.Select(plugin => plugin.PluginId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
}

void CompleteClassification()
{
    YanflyBundleInventory inventory = YanflyInventorySerializer.LoadEmbeddedPhase0Inventory();
    foreach (YanflyPluginInventoryEntry plugin in inventory.Plugins)
    {
        True(plugin.CoverageModes.Count > 0, "Unclassified plugin: " + plugin.PluginId);
        True(!string.IsNullOrWhiteSpace(plugin.PlannedImplementationPhase), "Missing implementation phase: " + plugin.PluginId);
        Equal(64, plugin.CanonicalSha256.Length);
        True(plugin.Occurrences.Count > 0, "Missing occurrence: " + plugin.PluginId);
    }

    True(inventory.Plugins.Count(plugin => plugin.AuthoringSurfaces.Contains(YanflyAuthoringSurface.DatabaseNote)) >= 120);
    True(inventory.Plugins.Count(plugin => plugin.CoverageModes.Contains(YanflyCoverageMode.SpecializedEditor)) >= 50);
    True(inventory.Plugins.Any(plugin => plugin.CoverageModes.Contains(YanflyCoverageMode.RuntimeValidationOnly)));
}

void DuplicateResolutions()
{
    YanflyBundleInventory inventory = YanflyInventorySerializer.LoadEmbeddedPhase0Inventory();
    YanflyPluginInventoryEntry levelGrowth = inventory.Plugins.Single(plugin => plugin.PluginId == "YEP_LevelUpGrowthEffects");
    Equal(YanflyDuplicateKind.ConflictingDuplicate, levelGrowth.DuplicateKind);
    Equal(YanflyResolutionReason.HighestVersion, levelGrowth.ResolutionReason);
    Equal("1.01", levelGrowth.Version!);
    True(levelGrowth.CanonicalPath.Contains("/Free Plugins/", StringComparison.OrdinalIgnoreCase));

    YanflyPluginInventoryEntry upgrades = inventory.Plugins.Single(plugin => plugin.PluginId == "YEP_X_ItemUpgradeSlots");
    Equal(YanflyDuplicateKind.ConflictingDuplicate, upgrades.DuplicateKind);
    Equal(YanflyResolutionReason.PreferredCategorizedCopy, upgrades.ResolutionReason);
    True(upgrades.CanonicalPath.Contains("/Items & Equips Plugins/", StringComparison.OrdinalIgnoreCase));
}

void DependencyGraphIntegrity()
{
    YanflyBundleInventory inventory = YanflyInventorySerializer.LoadEmbeddedPhase0Inventory();
    HashSet<string> ids = inventory.Plugins.Select(plugin => plugin.PluginId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (YanflyPluginInventoryEntry plugin in inventory.Plugins)
    {
        foreach (YanflyDependencyEvidence dependency in plugin.Dependencies)
        {
            True(!string.IsNullOrWhiteSpace(dependency.Evidence), "Missing dependency evidence: " + plugin.PluginId);
            if (dependency.IsResolved)
            {
                True(dependency.ResolvedPluginId is not null && ids.Contains(dependency.ResolvedPluginId),
                    $"Resolved dependency does not name an inventory plugin: {plugin.PluginId} -> {dependency.ResolvedPluginId}");
            }
        }
    }

    YanflyPluginInventoryEntry itemDurability = inventory.Plugins.Single(plugin => plugin.PluginId == "YEP_X_ItemDurability");
    True(itemDurability.Dependencies.Any(dependency => dependency.ResolvedPluginId == "YEP_ItemCore" && dependency.Kind == YanflyDependencyKind.Required));
}

void LicensingBoundary()
{
    string[] prohibitedFiles = Directory.EnumerateFiles(repositoryRoot, "*.js", SearchOption.AllDirectories)
        .Where(path =>
        {
            string name = Path.GetFileName(path);
            return name.StartsWith("YEP_", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("KELYEP_", StringComparison.OrdinalIgnoreCase);
        })
        .ToArray();
    Equal(0, prohibitedFiles.Length);

    string specificationRoot = Path.Combine(repositoryRoot, "specifications", "plugin-tags", "yanfly");
    foreach (string path in Directory.EnumerateFiles(specificationRoot, "*", SearchOption.AllDirectories))
        True(Path.GetExtension(path) is ".json" or ".csv", "Unexpected Phase 0 specification file type: " + path);
}

void SyntheticArchiveAudit()
{
    string temp = CreateTempDirectory();
    try
    {
        string archivePath = Path.Combine(temp, "synthetic-yep.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "Collection/Core/YEP_TestCore.js", CorePluginSource());
            WriteEntry(archive, "Collection/Free Plugins/YEP_TestCore.js", CorePluginSource());
            WriteEntry(archive, "Collection/Extensions/YEP_X_TestExtension.js", ExtensionPluginSource());
            WriteEntry(archive, "Collection/--------------------.js", "// separator");
        }

        YanflyBundleAuditResult result = new YanflyBundleAuditor().Audit(archivePath, DateTimeOffset.UnixEpoch);
        True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Equal(4, result.Inventory.Summary.TotalJavaScriptFiles);
        Equal(1, result.Inventory.Summary.IgnoredSeparatorFiles);
        Equal(3, result.Inventory.Summary.PluginOccurrences);
        Equal(2, result.Inventory.Summary.UniquePlugins);
        Equal(1, result.Inventory.Summary.ExactDuplicateGroups);

        YanflyPluginInventoryEntry core = result.Inventory.Plugins.Single(plugin => plugin.PluginId == "YEP_TestCore");
        True(core.AuthoringSurfaces.Contains(YanflyAuthoringSurface.DatabaseNote));
        True(core.AuthoringSurfaces.Contains(YanflyAuthoringSurface.PluginCommand));
        True(core.AuthoringSurfaces.Contains(YanflyAuthoringSurface.LunaticMode));
        True(core.CoverageModes.Contains(YanflyCoverageMode.TypedCatalog));

        YanflyPluginInventoryEntry extension = result.Inventory.Plugins.Single(plugin => plugin.PluginId == "YEP_X_TestExtension");
        True(extension.Dependencies.Any(dependency => dependency.ResolvedPluginId == "YEP_TestCore" && dependency.Kind == YanflyDependencyKind.Required));
        True(extension.AuthoringSurfaces.Contains(YanflyAuthoringSurface.EventPageComment));
        True(extension.AuthoringSurfaces.Contains(YanflyAuthoringSurface.ActionSequence));
        True(extension.AuthoringSurfaces.Contains(YanflyAuthoringSurface.MessageTextCode));
        True(extension.AuthoringSurfaces.Contains(YanflyAuthoringSurface.ScriptCall));
    }
    finally
    {
        Directory.Delete(temp, true);
    }
}

void UnknownConflictDetection()
{
    string temp = CreateTempDirectory();
    try
    {
        Directory.CreateDirectory(Path.Combine(temp, "A"));
        Directory.CreateDirectory(Path.Combine(temp, "B"));
        File.WriteAllText(Path.Combine(temp, "A", "YEP_UnknownConflict.js"), CorePluginSource());
        File.WriteAllText(Path.Combine(temp, "B", "YEP_UnknownConflict.js"), CorePluginSource() + "\n// changed");
        YanflyBundleAuditResult result = new YanflyBundleAuditor().Audit(temp, DateTimeOffset.UnixEpoch);
        True(!result.Success);
        True(result.Diagnostics.Any(diagnostic => diagnostic.Code == "YEP0006" && diagnostic.Severity == YanflyAuditSeverity.Error));
    }
    finally
    {
        Directory.Delete(temp, true);
    }
}

void GoldenVerifier()
{
    YanflyBundleInventory inventory = YanflyInventorySerializer.LoadEmbeddedPhase0Inventory();
    YanflyGoldenManifestVerifier verifier = new();
    True(verifier.Verify(inventory, inventory).Success);
    YanflyBundleInventory changed = inventory with
    {
        Summary = inventory.Summary with { UniquePlugins = inventory.Summary.UniquePlugins + 1 }
    };
    YanflyVerificationResult result = verifier.Verify(inventory, changed);
    True(!result.Success);
    True(result.Differences.Any(difference => difference.Code == "YEPV001"));
}

void ReportOutput()
{
    string temp = CreateTempDirectory();
    try
    {
        YanflyBundleInventory inventory = YanflyInventorySerializer.LoadEmbeddedPhase0Inventory();
        YanflyBundleAuditResult result = new(inventory, []);
        new YanflyBundleReportWriter().WriteAll(result, temp);
        string[] required =
        [
            "yanfly-phase0-inventory.json",
            "yanfly-phase0-diagnostics.json",
            "yanfly-phase0-constitution.json",
            "yanfly-phase0-coverage-contract.json",
            "yanfly-phase0-duplicate-resolutions.json",
            "yanfly-phase0-dependency-graph.json",
            "yanfly-phase0-coverage.csv",
            "YANFLY_PHASE0_BUNDLE_AUDIT.md"
        ];
        foreach (string file in required) True(File.Exists(Path.Combine(temp, file)), "Missing report output: " + file);
        using JsonDocument _ = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(temp, "yanfly-phase0-inventory.json")));
    }
    finally
    {
        Directory.Delete(temp, true);
    }
}

static string CorePluginSource() => """
//=============================================================================
// Yanfly Engine Plugins - Synthetic Core
// YEP_TestCore.js
//=============================================================================
var Imported = Imported || {};
Imported.YEP_TestCore = true;
var Yanfly = Yanfly || {};
Yanfly.Test = Yanfly.Test || {};
Yanfly.Test.version = 1.00;
/*:
 * @plugindesc v1.00 Synthetic test core.
 * @author Playroma Tests
 * @param Enabled
 * @type boolean
 * @default true
 * @help
 * ============================================================================
 * Introduction
 * ============================================================================
 * Synthetic test source.
 * ============================================================================
 * Actor Notetags
 * ============================================================================
 * <Test Tag>
 * ============================================================================
 * Plugin Commands
 * ============================================================================
 * TestCommand
 * ============================================================================
 * Lunatic Mode
 * ============================================================================
 * Custom code.
 */
""";

static string ExtensionPluginSource() => """
//=============================================================================
// Yanfly Engine Plugins - Synthetic Extension
// YEP_X_TestExtension.js
//=============================================================================
var Imported = Imported || {};
Imported.YEP_X_TestExtension = true;
/*:
 * @plugindesc v1.01 (Requires YEP_TestCore.js) Synthetic extension.
 * @author Playroma Tests
 * @help
 * ============================================================================
 * Introduction
 * ============================================================================
 * This plugin requires YEP_TestCore.js and must be located under it.
 * ============================================================================
 * Comment Tags
 * ============================================================================
 * <Test Comment>
 * ============================================================================
 * Action Sequences
 * ============================================================================
 * TEST ACTION: target
 * ============================================================================
 * Text Codes
 * ============================================================================
 * \\TEST
 * ============================================================================
 * Script Calls
 * ============================================================================
 * Test.call();
 */
""";

static void WriteEntry(ZipArchive archive, string path, string text)
{
    ZipArchiveEntry entry = archive.CreateEntry(path);
    using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
    writer.Write(text);
}

static string CreateTempDirectory()
{
    string path = Path.Combine(Path.GetTempPath(), "Playroma-Yanfly-Phase0-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void True(bool condition, string message = "Assertion was false.")
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
}
