using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.Specifications;
using Playroma.JRPG.Testing;

string root = RepositoryRootLocator.Find();
List<(string Name, Action Test)> tests =
[
    ("Product identity", ProductIdentity),
    ("Compatibility charter", CompatibilityCharter),
    ("Event command catalogue", EventCommandCatalogue),
    ("Movement route catalogue", MovementRouteCatalogue),
    ("Specification JSON validity", SpecificationValidity),
    ("Embedded specification fallback", EmbeddedSpecificationFallback),
    ("Synthetic fixture coverage", FixtureCoverage),
    ("Map layer fixture shape", MapLayerFixture),
    ("Malformed fixture declarations", MalformedDeclarations),
    ("Deterministic fixture generation", DeterministicFixtures),
    ("Repository provenance audit", RepositoryAudit)
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

Console.WriteLine($"{tests.Count - failures}/{tests.Count} baseline tests passed.");
return failures == 0 ? 0 : 1;

void ProductIdentity()
{
    Equal("Playroma jRPG", PlayromaProduct.Name);
    Equal("MIT", PlayromaProduct.Licence);
    True(!string.IsNullOrWhiteSpace(PlayromaProduct.ReleaseLabel));
}

void CompatibilityCharter()
{
    True(PlayromaCompatibilityCharter.Promises.Count >= 9);
    True(PlayromaCompatibilityCharter.Promises.Any(p => p.Id == "unknown-data" && p.Guarantee == CompatibilityGuarantee.Required));
    True(PlayromaCompatibilityCharter.Promises.Any(p => p.Id == "deployment-recovery" && p.Guarantee == CompatibilityGuarantee.OutOfScope));
    Equal(PlayromaCompatibilityCharter.Promises.Count, PlayromaCompatibilityCharter.Promises.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count());
}

void EventCommandCatalogue()
{
    MvSpecificationCatalog catalog = MvSpecificationCatalog.Load(root);
    Equal(catalog.EventCommands.Count, catalog.EventCommands.Select(c => c.Code).Distinct().Count());
    int[] required = [0, 101, 401, 102, 402, 403, 404, 105, 405, 108, 408, 111, 411, 412, 205, 505, 301, 601, 602, 603, 604, 302, 605, 355, 655, 356];
    foreach (int code in required) True(catalog.EventCommands.Any(c => c.Code == code), "Missing event code " + code);
    True(catalog.EventCommands.Count >= 120, "Event catalogue is unexpectedly small.");
}

void MovementRouteCatalogue()
{
    MvSpecificationCatalog catalog = MvSpecificationCatalog.Load(root);
    Equal(46, catalog.MovementRouteCommands.Count);
    Equal(46, catalog.MovementRouteCommands.Select(c => c.Code).Distinct().Count());
    for (int code = 0; code <= 45; code++) True(catalog.MovementRouteCommands.Any(c => c.Code == code), "Missing route code " + code);
}

void SpecificationValidity()
{
    foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "specifications"), "*.json"))
    {
        using JsonDocument _ = JsonDocument.Parse(File.ReadAllBytes(file));
    }
}

void EmbeddedSpecificationFallback()
{
    string missingRoot = Path.Combine(Path.GetTempPath(), "PlayromaJRPG-Missing-Specification-Root");
    MvSpecificationCatalog embedded = MvSpecificationCatalog.Load(missingRoot);
    True(embedded.EventCommands.Count >= 120);
    Equal(46, embedded.MovementRouteCommands.Count);
    True(embedded.DatabaseFiles.Count >= 15);
}

void FixtureCoverage()
{
    string fixtureRoot = Path.Combine(root, "testdata", "fixtures");
    string[] required = ["empty-source-project", "database-catalog-project", "event-command-catalog-project", "map-layer-catalog-project", "movement-route-catalog-project", "plugin-parameter-catalog-project", "malformed", "phase1"];
    foreach (string directory in required) True(Directory.Exists(Path.Combine(fixtureRoot, directory)), "Missing fixture directory " + directory);

    using JsonDocument events = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, "event-command-catalog-project", "data", "CommonEvents.json")));
    HashSet<int> fixtureCodes = [];
    foreach (JsonElement commonEvent in events.RootElement.EnumerateArray().Skip(1))
    {
        foreach (JsonElement command in commonEvent.GetProperty("list").EnumerateArray()) fixtureCodes.Add(command.GetProperty("code").GetInt32());
    }
    foreach (int code in MvSpecificationCatalog.Load(root).EventCommands.Select(c => c.Code)) True(fixtureCodes.Contains(code), "Fixture missing event code " + code);
}

void MapLayerFixture()
{
    string path = Path.Combine(root, "testdata", "fixtures", "map-layer-catalog-project", "data", "Map001.json");
    using JsonDocument map = JsonDocument.Parse(File.ReadAllBytes(path));
    int width = map.RootElement.GetProperty("width").GetInt32();
    int height = map.RootElement.GetProperty("height").GetInt32();
    Equal(width * height * 6, map.RootElement.GetProperty("data").GetArrayLength());
    Equal(7, map.RootElement.GetProperty("events")[1].GetProperty("pages").GetArrayLength());
}

void MalformedDeclarations()
{
    string malformed = Path.Combine(root, "testdata", "fixtures", "malformed");
    string[] cases = ["invalid-json", "missing-index-zero", "map-data-length-mismatch", "unknown-event-command", "duplicate-plugin-entry", "missing-standard-file"];
    foreach (string name in cases)
    {
        string expected = Path.Combine(malformed, name, "expected-diagnostics.json");
        True(File.Exists(expected), "Missing expected diagnostics for " + name);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(expected));
        True(document.RootElement.GetArrayLength() > 0);
    }
}

void DeterministicFixtures()
{
    FixtureCheckResult result = new FixtureCorpusGenerator().Check(Path.Combine(root, "testdata", "fixtures"), root);
    True(result.Success, string.Join(Environment.NewLine, result.Differences));
}

void RepositoryAudit()
{
    RepositoryAuditResult result = new RepositoryAuditEngine().Audit(root);
    True(result.Success, string.Join(Environment.NewLine, result.Findings.Select(f => $"{f.Code} {f.Path}: {f.Message}")));
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
