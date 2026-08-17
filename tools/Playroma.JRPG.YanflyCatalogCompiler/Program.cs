using Playroma.JRPG.PluginTags.Yanfly;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return args.Length == 0 ? 2 : 0;
}

string command = args[0].ToLowerInvariant();
Dictionary<string, string?> options = ParseOptions(args.Skip(1).ToArray());
try
{
    return command switch
    {
        "audit" => RunAudit(options),
        "verify" => RunInventoryVerify(options),
        "constitution" => PrintConstitution(),
        "compile" => RunCompile(options),
        "verify-catalog" => RunCatalogVerify(options),
        "inspect-project" => await RunProjectInspectionAsync(options),
        _ => Unknown(command)
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}


static async Task<int> RunProjectInspectionAsync(IReadOnlyDictionary<string, string?> options)
{
    string project = Require(options, "project");
    string output = Require(options, "output");
    YanflyProjectPluginScanResult result = await new YanflyProjectPluginIntelligenceScanner().ScanAsync(project);
    new YanflyPluginIntelligenceReportWriter().Write(result.Analysis, output);
    Console.WriteLine($"Plugins: {result.Analysis.Entries.Count}");
    Console.WriteLine($"Enabled: {result.Analysis.Entries.Count(entry => entry.Installed.IsEnabled)}");
    Console.WriteLine($"Errors: {result.Analysis.Issues.Count(issue => issue.Severity == Playroma.JRPG.PluginTags.InstalledPluginIssueSeverity.Error)}");
    Console.WriteLine($"Warnings: {result.Analysis.Issues.Count(issue => issue.Severity == Playroma.JRPG.PluginTags.InstalledPluginIssueSeverity.Warning)}");
    Console.WriteLine("Reports written to: " + Path.GetFullPath(output));
    bool strict = options.ContainsKey("strict");
    return !result.Analysis.HasErrors && (!strict || !result.Analysis.HasWarnings) ? 0 : 1;
}

static int RunCompile(IReadOnlyDictionary<string, string?> options)
{
    string input = Require(options, "input");
    string output = Require(options, "output");
    YanflyBundleInventory inventory = string.IsNullOrWhiteSpace(Get(options, "inventory"))
        ? YanflyInventorySerializer.LoadEmbeddedPhase0Inventory()
        : YanflyInventorySerializer.Load(Get(options, "inventory")!);
    YanflySemanticOverlayDocument overlay = string.IsNullOrWhiteSpace(Get(options, "overlay"))
        ? YanflySemanticOverlaySerializer.LoadEmbedded()
        : new YanflySemanticOverlaySerializer().Load(Get(options, "overlay")!);
    YanflyCatalogCompilation compilation = new YanflyCatalogCompiler().Compile(input, inventory, overlay);
    new YanflyCatalogReportWriter().WriteAll(compilation, output);
    PrintCatalogSummary(compilation);
    Console.WriteLine("Reports written to: " + Path.GetFullPath(output));
    bool strict = options.ContainsKey("strict");
    bool warnings = compilation.Diagnostics.Any(d => d.Severity == YanflyCatalogDiagnosticSeverity.Warning);
    return compilation.Success && (!strict || !warnings) ? 0 : 1;
}

static int RunCatalogVerify(IReadOnlyDictionary<string, string?> options)
{
    string input = Require(options, "input");
    string expectedPath = Require(options, "expected");
    YanflyCatalogCompilation expected = new YanflyCatalogCompilationSerializer().Load(expectedPath);
    YanflyBundleInventory inventory = string.IsNullOrWhiteSpace(Get(options, "inventory"))
        ? YanflyInventorySerializer.LoadEmbeddedPhase0Inventory()
        : YanflyInventorySerializer.Load(Get(options, "inventory")!);
    YanflySemanticOverlayDocument overlay = string.IsNullOrWhiteSpace(Get(options, "overlay"))
        ? YanflySemanticOverlaySerializer.LoadEmbedded()
        : new YanflySemanticOverlaySerializer().Load(Get(options, "overlay")!);
    YanflyCatalogCompilation actual = new YanflyCatalogCompiler().Compile(input, inventory, overlay, expected.GeneratedAtUtc);
    YanflyCatalogVerificationResult verification = new YanflyCatalogGoldenVerifier().Verify(expected, actual);
    foreach (YanflyCatalogVerificationDifference difference in verification.Differences)
        Console.Error.WriteLine($"{difference.Code} {difference.PluginId ?? "bundle"}: {difference.Message}");
    if (verification.Success) Console.WriteLine("Golden YEP Phase 2 catalog verification passed.");
    return verification.Success && actual.Success ? 0 : 1;
}

static int RunAudit(IReadOnlyDictionary<string, string?> options)
{
    string input = Require(options, "input");
    string output = Require(options, "output");
    bool strict = options.ContainsKey("strict");
    YanflyBundleAuditResult result = new YanflyBundleAuditor().Audit(input);
    new YanflyBundleReportWriter().WriteAll(result, output);
    PrintInventorySummary(result);
    Console.WriteLine("Reports written to: " + Path.GetFullPath(output));
    bool warnings = result.Diagnostics.Any(d => d.Severity == YanflyAuditSeverity.Warning);
    return result.Success && (!strict || !warnings) ? 0 : 1;
}

static int RunInventoryVerify(IReadOnlyDictionary<string, string?> options)
{
    string input = Require(options, "input");
    string? manifest = Get(options, "manifest");
    YanflyBundleInventory expected = string.IsNullOrWhiteSpace(manifest)
        ? YanflyInventorySerializer.LoadEmbeddedPhase0Inventory()
        : YanflyInventorySerializer.Load(manifest);
    YanflyBundleAuditResult audit = new YanflyBundleAuditor().Audit(input, expected.GeneratedAtUtc);
    YanflyVerificationResult verification = new YanflyGoldenManifestVerifier().Verify(expected, audit.Inventory);
    PrintInventorySummary(audit);
    foreach (YanflyVerificationDifference difference in verification.Differences)
        Console.Error.WriteLine($"{difference.Code} {difference.PluginId ?? "bundle"}: {difference.Message}");
    if (verification.Success) Console.WriteLine("Golden YEP inventory verification passed.");
    return verification.Success && audit.Success ? 0 : 1;
}

static void PrintCatalogSummary(YanflyCatalogCompilation c)
{
    Console.WriteLine($"Plugins: {c.Coverage.PluginCount}");
    Console.WriteLine($"Help sections: {c.Coverage.HelpSectionCount}");
    Console.WriteLine($"Plugin parameters: {c.Coverage.PluginParameterCount}");
    Console.WriteLine($"Discoveries: {c.Coverage.SourceDiscoveryCount}");
    Console.WriteLine($"Generated features: {c.Coverage.GeneratedFeatureCount}");
    Console.WriteLine($"Requires review: {c.Coverage.RequiresReviewCount}");
    Console.WriteLine($"Diagnostics: {c.Diagnostics.Count}");
}

static void PrintInventorySummary(YanflyBundleAuditResult result)
{
    YanflyBundleSummary s = result.Inventory.Summary;
    Console.WriteLine($"Unique plugins: {s.UniquePlugins}");
    Console.WriteLine($"Plugin occurrences: {s.PluginOccurrences}");
    Console.WriteLine($"Duplicate filename groups: {s.DuplicateFileNameGroups}");
    Console.WriteLine($"Conflicting duplicate groups: {s.ConflictingDuplicateGroups}");
    Console.WriteLine($"Diagnostics: {result.Diagnostics.Count}");
}

static int PrintConstitution()
{
    Console.WriteLine("Playroma Plugin Tags — Yanfly Phase 0 Constitution");
    foreach (YanflyConstitutionPromise promise in YanflyPhase0Constitution.Promises)
        Console.WriteLine($"- {promise.Id}: {promise.Guarantee}");
    return 0;
}

static int Unknown(string command) { Console.Error.WriteLine("Unknown command: " + command); PrintUsage(); return 2; }
static Dictionary<string, string?> ParseOptions(string[] arguments) { Dictionary<string, string?> options = new(StringComparer.OrdinalIgnoreCase); for (int i = 0; i < arguments.Length; i++) { string token = arguments[i]; if (!token.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Unexpected argument: " + token); string name = token[2..]; options[name] = i + 1 < arguments.Length && !arguments[i + 1].StartsWith("--", StringComparison.Ordinal) ? arguments[++i] : null; } return options; }
static string Require(IReadOnlyDictionary<string, string?> options, string name) => string.IsNullOrWhiteSpace(Get(options, name)) ? throw new ArgumentException("Missing required option --" + name + ".") : Get(options, name)!;
static string? Get(IReadOnlyDictionary<string, string?> options, string name) => options.TryGetValue(name, out string? value) ? value : null;
static void PrintUsage() { Console.WriteLine("Playroma.JRPG.YanflyCatalogCompiler"); Console.WriteLine("  inspect-project --project <mv-project-root> --output <directory> [--strict]"); Console.WriteLine("  compile --input <directory-or-zip> --output <directory> [--inventory <json>] [--overlay <json>] [--strict]"); Console.WriteLine("  verify-catalog --input <directory-or-zip> --expected <compilation.json> [--inventory <json>] [--overlay <json>]"); Console.WriteLine("  audit --input <directory-or-zip> --output <directory> [--strict]"); Console.WriteLine("  verify --input <directory-or-zip> [--manifest <inventory.json>]"); Console.WriteLine("  constitution"); }
