using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;
using Playroma.JRPG.RpgMakerMV;

string root = RepositoryRootLocator.Find();
string fixtures = Path.Combine(root, "testdata", "fixtures");
List<(string Name, Func<Task> Test)> tests =
[
    ("Source project discovery", SourceProjectDiscovery),
    ("WWW project discovery", WwwProjectDiscovery),
    ("Multiple candidate discovery", MultipleCandidateDiscovery),
    ("Unsupported MZ recognition", UnsupportedMzRecognition),
    ("Standard database inventory", StandardDatabaseInventory),
    ("Complete map scan", CompleteMapScan),
    ("Plugin inventory", PluginInventory),
    ("Unsafe plugin path diagnostic", UnsafePluginPathDiagnostic),
    ("Encrypted asset classification", EncryptedAssetClassification),
    ("Malformed JSON isolation", MalformedJsonIsolation),
    ("Read-only guarantee", ReadOnlyGuarantee),
    ("Path traversal rejection", PathTraversalRejection),
    ("Cancellation", Cancellation),
    ("Private stress corpus", PrivateStressCorpus)
];

int failures = 0;
foreach ((string name, Func<Task> test) in tests)
{
    try
    {
        await test().ConfigureAwait(false);
        Console.WriteLine("PASS " + name);
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + exception);
    }
}

Console.WriteLine($"{tests.Count - failures}/{tests.Count} Phase 1 tests passed.");
return failures == 0 ? 0 : 1;

async Task SourceProjectDiscovery()
{
    string path = Fixture("phase1", "source-layout-project");
    IReadOnlyList<MvProjectCandidate> candidates = await new MvProjectLoader().ProbeAsync(path).ConfigureAwait(false);
    Equal(1, candidates.Count);
    Equal(MvProjectLayoutKind.SourceRoot, candidates[0].LayoutKind);
    Equal(ProjectEditability.FullyEditableSource, candidates[0].Editability);
    True(candidates[0].Evidence.Any(evidence => evidence.Kind == ProjectEvidenceKind.RuntimeCoreScript));
}

async Task WwwProjectDiscovery()
{
    string path = Fixture("phase1", "www-layout-project");
    IReadOnlyList<MvProjectCandidate> candidates = await new MvProjectLoader().ProbeAsync(path).ConfigureAwait(false);
    MvProjectCandidate candidate = candidates.Single(item => item.DataRoot.EndsWith(Path.Combine("www", "data"), StringComparison.OrdinalIgnoreCase));
    Equal(MvProjectLayoutKind.ReadableDeployment, candidate.LayoutKind);
    Equal(Path.Combine(path, "www"), candidate.RuntimeRoot!);
}

async Task MultipleCandidateDiscovery()
{
    string path = Fixture("phase1", "multiple-project-candidates");
    IReadOnlyList<MvProjectCandidate> candidates = await new MvProjectLoader().ProbeAsync(path).ConfigureAwait(false);
    Equal(2, candidates.Count(candidate => candidate.Editability != ProjectEditability.Invalid));
}

async Task UnsupportedMzRecognition()
{
    string path = Fixture("phase1", "unsupported-mz-project");
    IReadOnlyList<MvProjectCandidate> candidates = await new MvProjectLoader().ProbeAsync(path).ConfigureAwait(false);
    Equal(1, candidates.Count);
    Equal(MvProjectLayoutKind.UnsupportedMzProject, candidates[0].LayoutKind);
    Equal(ProjectEditability.Invalid, candidates[0].Editability);
}

async Task StandardDatabaseInventory()
{
    string path = Fixture("database-catalog-project");
    OperationResult<MvProjectSnapshot> result = await new MvProjectLoader().LoadAsync(
        path,
        new ProjectLoadOptions(ProjectLoadDepth.StandardData)).ConfigureAwait(false);
    NotNull(result.Value);
    True(!result.HasFatalErrors, JoinDiagnostics(result.Diagnostics));
    Equal("Playroma Synthetic Fixture", result.Value!.Identity.ProjectTitle!);
    Equal(1, result.Value.Database.Find(MvDocumentKind.Actors)!.PopulatedRecordCount);
    Equal(1, result.Value.Database.Find(MvDocumentKind.Classes)!.PopulatedRecordCount);
    Equal(1, result.Value.Maps.Count);
    True(result.Value.Database.Files.All(file => file.Kind == MvDocumentKind.System || file.ArrayLength >= 1));
}

async Task CompleteMapScan()
{
    string path = Fixture("movement-route-catalog-project");
    OperationResult<MvProjectSnapshot> result = await new MvProjectLoader().LoadAsync(
        path,
        new ProjectLoadOptions(ProjectLoadDepth.Complete)).ConfigureAwait(false);
    NotNull(result.Value);
    MvMapDescriptor map = result.Value!.Maps.Single(map => map.Id == 1);
    Equal(4, map.Width!.Value);
    Equal(4, map.Height!.Value);
    Equal(1, map.EventCount!.Value);
    True((map.EventPageCount ?? 0) > 0);
    True(map.EventCommandCount > 0);
}

async Task PluginInventory()
{
    string path = Fixture("phase1", "plugin-heavy-project");
    OperationResult<MvProjectSnapshot> result = await new MvProjectLoader().LoadAsync(
        path,
        new ProjectLoadOptions(ProjectLoadDepth.Inventory)).ConfigureAwait(false);
    NotNull(result.Value);
    Equal(25, result.Value!.Plugins.Entries.Count);
    Equal(17, result.Value.Plugins.EnabledCount);
    True(result.Value.Plugins.Entries.All(plugin => plugin.ScriptExists));
    True(result.Value.Plugins.Entries.Single(plugin => plugin.Name == "SyntheticPlugin25").AppearsNodeDependent);
    True(result.Value.Runtime.AppearsNodeDependent);
}

async Task UnsafePluginPathDiagnostic()
{
    string path = Fixture("phase1", "unsafe-plugin-path");
    OperationResult<MvProjectSnapshot> result = await new MvProjectLoader().LoadAsync(
        path,
        new ProjectLoadOptions(ProjectLoadDepth.Inventory)).ConfigureAwait(false);
    NotNull(result.Value);
    True(result.Diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCodes.UnsafePluginPath));
    True(result.Value!.Plugins.Entries.Single().ScriptRelativePath is null);
}

async Task EncryptedAssetClassification()
{
    string path = Fixture("phase1", "encrypted-asset-layout");
    OperationResult<MvProjectSnapshot> result = await new MvProjectLoader().LoadAsync(
        path,
        new ProjectLoadOptions(ProjectLoadDepth.Inventory)).ConfigureAwait(false);
    NotNull(result.Value);
    Equal(1, result.Value!.Assets.EncryptedAssetCount);
    True(result.Diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCodes.EncryptedAssetsDetected));
}

async Task MalformedJsonIsolation()
{
    string temporary = CreateTemporaryCopy(Fixture("phase1", "source-layout-project"));
    try
    {
        await File.WriteAllTextAsync(Path.Combine(temporary, "data", "Actors.json"), "[null, { invalid ]", Encoding.UTF8).ConfigureAwait(false);
        OperationResult<MvProjectSnapshot> result = await new MvProjectLoader().LoadAsync(
            temporary,
            new ProjectLoadOptions(ProjectLoadDepth.StandardData, ContinueAfterErrors: true)).ConfigureAwait(false);
        NotNull(result.Value);
        True(result.Diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCodes.InvalidJson));
        True(result.Value!.Database.Find(MvDocumentKind.Classes)!.IsValidJson);
    }
    finally
    {
        Directory.Delete(temporary, recursive: true);
    }
}

async Task ReadOnlyGuarantee()
{
    string path = Fixture("phase1", "source-layout-project");
    Dictionary<string, string> before = HashTree(path);
    OperationResult<MvProjectSnapshot> result = await new MvProjectLoader().LoadAsync(
        path,
        new ProjectLoadOptions(ProjectLoadDepth.Complete, CalculateStrongAssetHashes: true)).ConfigureAwait(false);
    NotNull(result.Value);
    Dictionary<string, string> after = HashTree(path);
    Equal(before.Count, after.Count);
    foreach ((string file, string hash) in before)
    {
        True(after.TryGetValue(file, out string? afterHash), "File disappeared: " + file);
        Equal(hash, afterHash!);
    }

    string marker = Path.Combine(path, "Game.rpgproject");
    using FileStream stream = new(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    True(stream.CanWrite, "A project file remained locked after loading.");
}

Task PathTraversalRejection()
{
    ProjectPathPolicy policy = new(Fixture("phase1", "source-layout-project"));
    bool threw = false;
    try
    {
        policy.ResolveRelative(Path.Combine("..", "outside.json"));
    }
    catch (ProjectPathException exception)
    {
        threw = exception.DiagnosticCode == DiagnosticCodes.PathOutsideProjectRoot;
    }
    True(threw);
    return Task.CompletedTask;
}

async Task Cancellation()
{
    using CancellationTokenSource source = new();
    source.Cancel();
    bool cancelled = false;
    try
    {
        await new MvProjectLoader().LoadAsync(
            Fixture("phase1", "source-layout-project"),
            new ProjectLoadOptions(ProjectLoadDepth.Complete),
            cancellationToken: source.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        cancelled = true;
    }
    True(cancelled);
}


async Task PrivateStressCorpus()
{
    string? path = Environment.GetEnvironmentVariable("PLAYROMA_PRIVATE_MV_PROJECT");
    if (string.IsNullOrWhiteSpace(path))
    {
        Console.WriteLine("SKIP Private stress corpus: PLAYROMA_PRIVATE_MV_PROJECT is not set.");
        return;
    }

    OperationResult<MvProjectSnapshot> result = await new MvProjectLoader().LoadAsync(
        path,
        new ProjectLoadOptions(ProjectLoadDepth.Complete, MaximumParallelReads: 4)).ConfigureAwait(false);
    NotNull(result.Value);
    MvProjectSnapshot snapshot = result.Value!;
    Equal(138, snapshot.Maps.Count);
    Equal(3296, snapshot.Maps.Sum(map => map.EventCount ?? 0));
    Equal(152687, snapshot.Maps.Sum(map => map.EventCommandCount ?? 0));
    Equal(70, snapshot.Plugins.EnabledCount);
    Equal(56, snapshot.Database.Find(MvDocumentKind.Actors)!.PopulatedRecordCount);
    Equal(101, snapshot.Database.Find(MvDocumentKind.Classes)!.PopulatedRecordCount);
    Equal(1250, snapshot.Database.Find(MvDocumentKind.Skills)!.PopulatedRecordCount);
    Equal(500, snapshot.Database.Find(MvDocumentKind.Items)!.PopulatedRecordCount);
    Equal(1250, snapshot.Database.Find(MvDocumentKind.Weapons)!.PopulatedRecordCount);
    Equal(400, snapshot.Database.Find(MvDocumentKind.Armors)!.PopulatedRecordCount);
    Equal(370, snapshot.Database.Find(MvDocumentKind.Enemies)!.PopulatedRecordCount);
    Equal(175, snapshot.Database.Find(MvDocumentKind.Troops)!.PopulatedRecordCount);
    Equal(400, snapshot.Database.Find(MvDocumentKind.States)!.PopulatedRecordCount);
}

string Fixture(params string[] parts) => parts.Aggregate(fixtures, Path.Combine);

static string CreateTemporaryCopy(string source)
{
    string destination = Path.Combine(Path.GetTempPath(), "PlayromaJRPG-Phase1-Test-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
    Directory.CreateDirectory(destination);
    foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
    }
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target);
    }
    return destination;
}

static Dictionary<string, string> HashTree(string rootPath) =>
    Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToDictionary(
            path => Path.GetRelativePath(rootPath, path).Replace('\\', '/'),
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
            StringComparer.Ordinal);

static string JoinDiagnostics(IEnumerable<ProjectDiagnostic> diagnostics) =>
    string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

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

static void NotNull<T>(T? value) where T : class
{
    if (value is null) throw new InvalidOperationException("Expected a non-null value.");
}
