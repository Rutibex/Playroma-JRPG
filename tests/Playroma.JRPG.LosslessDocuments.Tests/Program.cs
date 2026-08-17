using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.RpgMakerMV.Documents;

List<(string Name, Func<Task> Test)> tests =
[
    ("Untouched JSON is byte identical", UntouchedRoundTripAsync),
    ("Scalar edits preserve lexical neighbours", ScalarEditAsync),
    ("Unknown plugin data survives typed edit", TypedActorEditAsync),
    ("Property insertion and removal are valid", StructuralObjectEditAsync),
    ("Sequential structural edits remain addressable", SequentialStructuralEditsAsync),
    ("Sequential structural edits pass mutation verification", SequentialStructuralMutationVerifierAsync),
    ("Typed setters add missing MV properties", TypedMissingPropertyAsync),
    ("Array insertion and removal are valid", StructuralArrayEditAsync),
    ("Plugin manifest preserves wrapper", PluginManifestAsync),
    ("Plugin manifest saves through verified transaction", PluginManifestSaveAsync),
    ("Mutation verifier blocks unexplained changes", MutationVerifierAsync),
    ("Mutation verifier validates structural array edits", StructuralMutationVerifierAsync),
    ("External modifications block saving", ExternalModificationAsync),
    ("BOM documents pass verified saving", BomSaveAsync),
    ("Journaled save commits and verifies", SaveTransactionAsync),
    ("Interrupted save retains recoverable journal", FailedSaveRecoveryAsync),
    ("Project copy preserves untouched files and applies edits", ProjectCopyAsync),
    ("Map data buffer edits one tile", MapDataBufferAsync)
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
        Console.Error.WriteLine("FAIL " + name);
        Console.Error.WriteLine(exception);
    }
}

Console.WriteLine($"{tests.Count - failures}/{tests.Count} Phase 2 tests passed.");
return failures == 0 ? 0 : 1;

static Task UntouchedRoundTripAsync()
{
    byte[] source = Encoding.UTF8.GetBytes("\uFEFF{\r\n  \"a\": 1.0,\r\n  \"escaped\": \"\\u0041\",\r\n  \"duplicate\": 1,\r\n  \"duplicate\": 2\r\n}\r\n");
    LosslessJsonParseResult parsed = new LosslessJsonParser().Parse(source, "test.json");
    Assert(parsed.Document is not null, "Parser rejected valid JSON.");
    byte[] generated = parsed.Document!.GenerateBytes();
    Assert(source.AsSpan().SequenceEqual(generated), "Untouched bytes changed.");
    return Task.CompletedTask;
}

static Task ScalarEditAsync()
{
    byte[] source = Encoding.UTF8.GetBytes("{\"a\":1.0,\"name\":\"Harold\",\"escaped\":\"\\u0041\"}");
    LosslessJsonDocument document = new LosslessJsonParser().Parse(source).Document!;
    document.SetString("/name", "Theron");
    string generated = Encoding.UTF8.GetString(document.GenerateBytes());
    Assert(generated == "{\"a\":1.0,\"name\":\"Theron\",\"escaped\":\"\\u0041\"}", "Unrelated lexical content changed.");
    return Task.CompletedTask;
}

static async Task TypedActorEditAsync()
{
    byte[] source = Encoding.UTF8.GetBytes("[null,{\"id\":1,\"name\":\"Hero\",\"note\":\"<Tag>\",\"customPluginData\":{\"formula\":\"a.atk * 4\",\"nested\":[1,2,3]}}]");
    MutableJsonProjectDocument inner = MutableJsonProjectDocument.Create(
        "data/Actors.json",
        MvDocumentKind.Actors,
        source,
        Fingerprint(source));
    using MvActorsDocument actors = new(inner);
    MvActorView actor = actors.Root.GetById(1) ?? throw new InvalidOperationException("Actor not found.");
    actor.Name = "New Hero";
    byte[] generated = (await actors.GenerateBytesAsync(CancellationToken.None).ConfigureAwait(false)).ToArray();
    using JsonDocument json = JsonDocument.Parse(generated);
    JsonElement record = json.RootElement[1];
    Assert(record.GetProperty("name").GetString() == "New Hero", "Typed edit was not written.");
    Assert(record.GetProperty("customPluginData").GetProperty("formula").GetString() == "a.atk * 4", "Unknown plugin data changed.");
}

static Task StructuralObjectEditAsync()
{
    byte[] source = Encoding.UTF8.GetBytes("{\n  \"a\": 1\n}\n");
    LosslessJsonDocument document = new LosslessJsonParser().Parse(source).Document!;
    document.AddProperty(JsonPath.Root, "b", "2"u8);
    byte[] generated = document.GenerateBytes();
    using JsonDocument parsed = JsonDocument.Parse(generated);
    Assert(parsed.RootElement.GetProperty("a").GetInt32() == 1, "Existing property changed.");
    Assert(parsed.RootElement.GetProperty("b").GetInt32() == 2, "Inserted property missing.");

    LosslessJsonDocument second = new LosslessJsonParser().Parse(generated).Document!;
    second.RemoveProperty(JsonPath.Root, "a");
    using JsonDocument removed = JsonDocument.Parse(second.GenerateBytes());
    Assert(!removed.RootElement.TryGetProperty("a", out _), "Property removal failed.");
    Assert(removed.RootElement.GetProperty("b").GetInt32() == 2, "Neighbour property changed.");
    return Task.CompletedTask;
}

static Task SequentialStructuralEditsAsync()
{
    byte[] source = Encoding.UTF8.GetBytes("{\"base\":1,\"items\":[1,2]}");
    MutableJsonProjectDocument document = MutableJsonProjectDocument.Create(
        "data/Test.json",
        MvDocumentKind.Unknown,
        source,
        Fingerprint(source));
    document.AddProperty(JsonPath.Root, "added", "10"u8);
    document.SetInt32("/added", 11);
    document.InsertArrayElement("/items", 1, "9"u8);
    document.SetInt32("/items/2", 22);
    document.RemoveArrayElement("/items", 0);
    byte[] generated = document.GenerateBytesAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult().ToArray();
    using JsonDocument parsed = JsonDocument.Parse(generated);
    Assert(parsed.RootElement.GetProperty("added").GetInt32() == 11, "A newly added property could not be edited again.");
    JsonElement items = parsed.RootElement.GetProperty("items");
    Assert(items.GetArrayLength() == 2 && items[0].GetInt32() == 9 && items[1].GetInt32() == 22,
        "Sequential array edits did not preserve current addressing.");
    document.Dispose();
    return Task.CompletedTask;
}

static Task SequentialStructuralMutationVerifierAsync()
{
    byte[] source = Encoding.UTF8.GetBytes("{\"base\":1,\"items\":[1,2]}");
    MutableJsonProjectDocument document = MutableJsonProjectDocument.Create(
        "data/Test.json",
        MvDocumentKind.Unknown,
        source,
        Fingerprint(source));
    document.AddProperty(JsonPath.Root, "added", "10"u8);
    document.SetInt32("/added", 11);
    document.InsertArrayElement("/items", 1, "9"u8);
    document.SetInt32("/items/2", 22);
    document.RemoveArrayElement("/items", 0);
    byte[] generated = document.GenerateBytesAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult().ToArray();
    MutationVerificationResult verification = new DocumentMutationVerifier().Verify(
        source,
        generated,
        document.Changes,
        document.RelativePath);
    Assert(verification.IsValid,
        "Mutation verification rejected a valid sequence of property and array edits: "
        + string.Join("; ", verification.Diagnostics.Select(item => item.Message)));
    document.Dispose();
    return Task.CompletedTask;
}

static async Task TypedMissingPropertyAsync()
{
    byte[] source = Encoding.UTF8.GetBytes("[null,{\"id\":1,\"name\":\"Hero\",\"note\":\"\"}]");
    MutableJsonProjectDocument inner = MutableJsonProjectDocument.Create(
        "data/Actors.json",
        MvDocumentKind.Actors,
        source,
        Fingerprint(source));
    using MvActorsDocument actors = new(inner);
    MvActorView actor = actors.Root.GetById(1) ?? throw new InvalidOperationException("Actor not found.");
    actor.Nickname = "Newly Added";
    actor.Nickname = "Updated";
    byte[] generated = (await actors.GenerateBytesAsync(CancellationToken.None).ConfigureAwait(false)).ToArray();
    using JsonDocument parsed = JsonDocument.Parse(generated);
    Assert(parsed.RootElement[1].GetProperty("nickname").GetString() == "Updated",
        "A typed setter did not add and subsequently update a missing property.");
}

static Task StructuralArrayEditAsync()
{
    byte[] source = Encoding.UTF8.GetBytes("[null,{\"id\":1}]");
    LosslessJsonDocument document = new LosslessJsonParser().Parse(source).Document!;
    document.InsertArrayElement(JsonPath.Root, 2, "{\"id\":2}"u8);
    byte[] generated = document.GenerateBytes();
    using JsonDocument parsed = JsonDocument.Parse(generated);
    Assert(parsed.RootElement.GetArrayLength() == 3, "Array insertion failed.");
    Assert(parsed.RootElement[2].GetProperty("id").GetInt32() == 2, "Inserted array object is wrong.");

    LosslessJsonDocument second = new LosslessJsonParser().Parse(generated).Document!;
    second.RemoveArrayElement(JsonPath.Root, 1);
    using JsonDocument removed = JsonDocument.Parse(second.GenerateBytes());
    Assert(removed.RootElement.GetArrayLength() == 2, "Array removal failed.");
    Assert(removed.RootElement[1].GetProperty("id").GetInt32() == 2, "Wrong array element remained.");
    return Task.CompletedTask;
}

static async Task PluginManifestAsync()
{
    byte[] source = Encoding.UTF8.GetBytes("// header\nvar $plugins =\n[\n  {\"name\":\"One\",\"status\":true,\"description\":\"\",\"parameters\":{\"Value\":\"12\"}},\n  {\"name\":\"One\",\"status\":false,\"description\":\"duplicate\",\"parameters\":{}}\n];\n// suffix\n");
    MvPluginManifestDocument manifest = MvPluginManifestDocument.Parse("js/plugins.js", source, Fingerprint(source));
    manifest.SetParameter(0, "Value", "99");
    manifest.SetParameter(1, "Added", "yes");
    manifest.AppendEntryRaw("{\"name\":\"Two\",\"status\":true,\"description\":\"added\",\"parameters\":{}}"u8);
    byte[] generated = (await manifest.GenerateBytesAsync(CancellationToken.None).ConfigureAwait(false)).ToArray();
    string text = Encoding.UTF8.GetString(generated);
    Assert(text.StartsWith("// header\nvar $plugins =\n", StringComparison.Ordinal), "Plugin prefix changed.");
    Assert(text.EndsWith("];\n// suffix\n", StringComparison.Ordinal), "Plugin suffix changed.");
    Assert(text.Contains("\"Value\":\"99\"", StringComparison.Ordinal), "Plugin parameter was not edited.");
    Assert(text.Contains("\"description\":\"duplicate\"", StringComparison.Ordinal), "Duplicate plugin entry was lost.");
    Assert(text.Contains("\"Added\":\"yes\"", StringComparison.Ordinal), "A missing plugin parameter was not added.");
    Assert(text.Contains("\"name\":\"Two\"", StringComparison.Ordinal), "A new plugin entry was not appended.");
    manifest.Dispose();
}

static async Task PluginManifestSaveAsync()
{
    using TemporaryDirectory temporary = new();
    string scriptDirectory = Path.Combine(temporary.Path, "js");
    Directory.CreateDirectory(scriptDirectory);
    string path = Path.Combine(scriptDirectory, "plugins.js");
    byte[] source = Encoding.UTF8.GetBytes(
        "// wrapper\nvar $plugins = [{\"name\":\"One\",\"status\":true,\"description\":\"\",\"parameters\":{\"Value\":\"12\"}}];\n");
    await File.WriteAllBytesAsync(path, source).ConfigureAwait(false);
    MvPluginManifestDocument manifest = MvPluginManifestDocument.Parse(
        "js/plugins.js",
        source,
        FileFingerprint(path, source));
    manifest.SetParameter(0, "Value", "77");
    ProjectSaveOptions options = new();
    ProjectSavePlan plan = await new SavePlanBuilder(temporary.Path)
        .BuildAsync([manifest], options)
        .ConfigureAwait(false);
    Assert(plan.CanSave, "Verified save rejected a valid plugins.js mutation.");
    ProjectSaveResult result = await new ProjectSaveService(temporary.Path)
        .SaveAsync(plan, options)
        .ConfigureAwait(false);
    Assert(result.Succeeded, "Plugin manifest transaction failed.");
    string saved = await File.ReadAllTextAsync(path).ConfigureAwait(false);
    Assert(saved.StartsWith("// wrapper\nvar $plugins = ", StringComparison.Ordinal),
        "Plugin wrapper changed during save.");
    Assert(saved.Contains("\"Value\":\"77\"", StringComparison.Ordinal),
        "Plugin parameter was not saved.");
    Assert(manifest.State == MutableDocumentState.Clean,
        "Plugin manifest did not establish a clean post-save baseline.");
    manifest.Dispose();
}

static Task MutationVerifierAsync()
{
    byte[] original = Encoding.UTF8.GetBytes("{\"name\":\"A\",\"unknown\":7}");
    byte[] generated = Encoding.UTF8.GetBytes("{\"name\":\"B\",\"unknown\":8}");
    DocumentChange authorized = new(Guid.NewGuid(), "/name", DocumentChangeKind.SetValue, null, null, "name");
    MutationVerificationResult result = new DocumentMutationVerifier().Verify(original, generated, [authorized], "test.json");
    Assert(!result.IsValid, "Verifier allowed an unexplained difference.");
    Assert(result.UnauthorizedDifferences.Any(item => item.Path.ToString() == "/unknown"), "Verifier did not identify the unknown property change.");
    return Task.CompletedTask;
}

static Task StructuralMutationVerifierAsync()
{
    byte[] original = Encoding.UTF8.GetBytes("[1,2,3]");
    DocumentChange insertion = new(
        Guid.NewGuid(),
        "/1",
        DocumentChangeKind.InsertArrayElement,
        null,
        new JsonValueSnapshot("9"),
        "insert");
    byte[] expected = Encoding.UTF8.GetBytes("[1,9,2,3]");
    MutationVerificationResult valid = new DocumentMutationVerifier().Verify(original, expected, [insertion], "array.json");
    Assert(valid.IsValid, "Verifier rejected the exact authorized array insertion.");

    byte[] corrupted = Encoding.UTF8.GetBytes("[7,9,2,3]");
    MutationVerificationResult invalid = new DocumentMutationVerifier().Verify(original, corrupted, [insertion], "array.json");
    Assert(!invalid.IsValid, "Verifier allowed an unrelated array value to change during insertion.");
    return Task.CompletedTask;
}

static async Task ExternalModificationAsync()
{
    using TemporaryDirectory temporary = new();
    string data = Path.Combine(temporary.Path, "data");
    Directory.CreateDirectory(data);
    string file = Path.Combine(data, "Actors.json");
    await File.WriteAllTextAsync(file, "[null,{\"id\":1,\"name\":\"A\"}]").ConfigureAwait(false);
    byte[] openedBytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
    MutableJsonProjectDocument document = MutableJsonProjectDocument.Create("data/Actors.json", MvDocumentKind.Actors, openedBytes, FileFingerprint(file, openedBytes));
    document.SetString("/1/name", "B");
    await File.WriteAllTextAsync(file, "[null,{\"id\":1,\"name\":\"External\"}]").ConfigureAwait(false);
    ProjectSavePlan plan = await new SavePlanBuilder(temporary.Path).BuildAsync([document], new ProjectSaveOptions()).ConfigureAwait(false);
    Assert(plan.Documents.Single().Disposition == SaveDisposition.Conflict, "External change was not blocked.");
    document.Dispose();
}

static async Task BomSaveAsync()
{
    using TemporaryDirectory temporary = new();
    string data = Path.Combine(temporary.Path, "data");
    Directory.CreateDirectory(data);
    string file = Path.Combine(data, "System.json");
    byte[] body = Encoding.UTF8.GetBytes("{\r\n  \"gameTitle\": \"Before\"\r\n}\r\n");
    byte[] bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble();
    byte[] source = bom.Concat(body).ToArray();
    await File.WriteAllBytesAsync(file, source).ConfigureAwait(false);
    MutableJsonProjectDocument document = MutableJsonProjectDocument.Create(
        "data/System.json",
        MvDocumentKind.System,
        source,
        FileFingerprint(file, source));
    document.SetString("/gameTitle", "After");
    ProjectSaveOptions options = new();
    ProjectSavePlan plan = await new SavePlanBuilder(temporary.Path).BuildAsync([document], options).ConfigureAwait(false);
    Assert(plan.CanSave, "Verified save rejected a valid UTF-8 BOM document.");
    ProjectSaveResult result = await new ProjectSaveService(temporary.Path).SaveAsync(plan, options).ConfigureAwait(false);
    Assert(result.Succeeded, "BOM document save failed.");
    byte[] saved = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
    Assert(saved.AsSpan(0, 3).SequenceEqual(bom), "UTF-8 BOM was not preserved.");
    Assert(Encoding.UTF8.GetString(saved).Contains("After", StringComparison.Ordinal), "BOM document edit was not saved.");
    document.Dispose();
}

static async Task SaveTransactionAsync()
{
    using TemporaryDirectory temporary = new();
    string data = Path.Combine(temporary.Path, "data");
    Directory.CreateDirectory(data);
    string file = Path.Combine(data, "Actors.json");
    await File.WriteAllTextAsync(file, "[null,{\"id\":1,\"name\":\"A\",\"custom\":42}]").ConfigureAwait(false);
    byte[] openedBytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
    MutableJsonProjectDocument document = MutableJsonProjectDocument.Create("data/Actors.json", MvDocumentKind.Actors, openedBytes, FileFingerprint(file, openedBytes));
    document.SetString("/1/name", "B");
    ProjectSaveOptions options = new();
    ProjectSavePlan plan = await new SavePlanBuilder(temporary.Path).BuildAsync([document], options).ConfigureAwait(false);
    Assert(plan.CanSave, "Save plan was unexpectedly invalid.");
    ProjectSaveResult result = await new ProjectSaveService(temporary.Path).SaveAsync(plan, options).ConfigureAwait(false);
    Assert(result.Succeeded, "Save transaction failed.");
    using JsonDocument saved = JsonDocument.Parse(await File.ReadAllBytesAsync(file).ConfigureAwait(false));
    Assert(saved.RootElement[1].GetProperty("name").GetString() == "B", "Saved value is wrong.");
    Assert(saved.RootElement[1].GetProperty("custom").GetInt32() == 42, "Unknown property was lost.");
    Assert(Directory.Exists(Path.Combine(temporary.Path, ".playroma", "transactions")), "Save journal was not created.");
    Assert(document.State == MutableDocumentState.Clean && document.Changes.Count == 0, "Successful save did not establish a new clean baseline.");

    document.SetString("/1/name", "C");
    ProjectSavePlan secondPlan = await new SavePlanBuilder(temporary.Path).BuildAsync([document], options).ConfigureAwait(false);
    Assert(secondPlan.CanSave && secondPlan.Documents.Single().Disposition == SaveDisposition.WriteModified, "Second save was falsely treated as an external conflict.");
    ProjectSaveResult secondResult = await new ProjectSaveService(temporary.Path).SaveAsync(secondPlan, options).ConfigureAwait(false);
    Assert(secondResult.Succeeded, "Second save transaction failed.");
    using JsonDocument secondSaved = JsonDocument.Parse(await File.ReadAllBytesAsync(file).ConfigureAwait(false));
    Assert(secondSaved.RootElement[1].GetProperty("name").GetString() == "C", "Second saved value is wrong.");
    Assert(secondSaved.RootElement[1].GetProperty("custom").GetInt32() == 42, "Unknown property was lost on second save.");
    document.Dispose();
}

static async Task FailedSaveRecoveryAsync()
{
    using TemporaryDirectory temporary = new();
    string data = Path.Combine(temporary.Path, "data");
    Directory.CreateDirectory(data);
    string file = Path.Combine(data, "Actors.json");
    await File.WriteAllTextAsync(file, "[null,{\"id\":1,\"name\":\"A\"}]").ConfigureAwait(false);
    byte[] openedBytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
    MutableJsonProjectDocument document = MutableJsonProjectDocument.Create(
        "data/Actors.json",
        MvDocumentKind.Actors,
        openedBytes,
        FileFingerprint(file, openedBytes));
    document.SetString("/1/name", "B");

    ProjectSaveOptions options = new();
    ProjectSavePlan plan = await new SavePlanBuilder(temporary.Path).BuildAsync([document], options).ConfigureAwait(false);
    ProjectSaveResult result = await new ProjectSaveService(temporary.Path, new FailingReplacer())
        .SaveAsync(plan, options)
        .ConfigureAwait(false);
    Assert(!result.Succeeded, "Injected replacement failure unexpectedly succeeded.");

    string current = await File.ReadAllTextAsync(file).ConfigureAwait(false);
    Assert(current.Contains("\"name\":\"A\"", StringComparison.Ordinal), "Original file changed before failed replacement recovery.");
    IReadOnlyList<SaveRecoveryCandidate> candidates = await new SaveRecoveryService(temporary.Path).InspectAsync().ConfigureAwait(false);
    SaveRecoveryCandidate candidate = candidates.Single(item => item.Journal.TransactionId == plan.TransactionId);
    Assert(candidate.Journal.State == SaveTransactionState.RecoveryRequired, "Failed save did not retain a recovery-required journal.");
    Assert(candidate.Recommendation == RecoveryRecommendation.RollBack, "Recovery recommendation should be rollback before any replacement.");
    Assert(candidate.Journal.Documents.All(item => File.Exists(item.BackupPath)), "Mandatory transaction backup was not retained.");

    await new SaveRecoveryService(temporary.Path).RollBackAsync(candidate).ConfigureAwait(false);
    string restored = await File.ReadAllTextAsync(file).ConfigureAwait(false);
    Assert(restored.Contains("\"name\":\"A\"", StringComparison.Ordinal), "Rollback did not restore the original file.");
    document.Dispose();
}

static async Task ProjectCopyAsync()
{
    using TemporaryDirectory source = new();
    using TemporaryDirectory destinationContainer = new();
    string destination = Path.Combine(destinationContainer.Path, "copy");
    string data = Path.Combine(source.Path, "data");
    string audio = Path.Combine(source.Path, "audio", "se");
    Directory.CreateDirectory(data);
    Directory.CreateDirectory(audio);
    Directory.CreateDirectory(Path.Combine(source.Path, ".playroma", "cache"));
    string actorsPath = Path.Combine(data, "Actors.json");
    await File.WriteAllTextAsync(actorsPath, "[null,{\"id\":1,\"name\":\"A\",\"unknown\":5}]").ConfigureAwait(false);
    byte[] binary = [0, 1, 2, 3, 255];
    await File.WriteAllBytesAsync(Path.Combine(audio, "tone.ogg"), binary).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(source.Path, ".playroma", "cache", "ignored.txt"), "cache").ConfigureAwait(false);

    byte[] opened = await File.ReadAllBytesAsync(actorsPath).ConfigureAwait(false);
    MutableJsonProjectDocument document = MutableJsonProjectDocument.Create(
        "data/Actors.json",
        MvDocumentKind.Actors,
        opened,
        FileFingerprint(actorsPath, opened));
    document.SetString("/1/name", "Copied");
    await new ProjectCopyService().CopyAsync(
        source.Path,
        destination,
        [document],
        new ProjectCopyOptions(),
        CancellationToken.None).ConfigureAwait(false);

    using JsonDocument copied = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(destination, "data", "Actors.json")).ConfigureAwait(false));
    Assert(copied.RootElement[1].GetProperty("name").GetString() == "Copied", "Modified document was not applied to the project copy.");
    Assert(copied.RootElement[1].GetProperty("unknown").GetInt32() == 5, "Unknown data was lost in the project copy.");
    byte[] copiedBinary = await File.ReadAllBytesAsync(Path.Combine(destination, "audio", "se", "tone.ogg")).ConfigureAwait(false);
    Assert(binary.AsSpan().SequenceEqual(copiedBinary), "Untouched binary file changed in the project copy.");
    Assert(!Directory.Exists(Path.Combine(destination, ".playroma")), "Playroma metadata was copied despite the default exclusion.");
    document.Dispose();
}

static async Task MapDataBufferAsync()
{
    byte[] source = Encoding.UTF8.GetBytes("{\"displayName\":\"Map\",\"width\":2,\"height\":1,\"tilesetId\":1,\"note\":\"\",\"data\":[1,2,3,4,5,6,7,8,9,10,11,12],\"events\":[null]}");
    MutableJsonProjectDocument document = MutableJsonProjectDocument.Create("data/Map001.json", MvDocumentKind.Map, source, Fingerprint(source));
    MvMapView map = new(document);
    Assert(map.Data.LayerCount == 6, "Map layer count was not detected.");
    map.Data.SetTile(1, 0, 0, 99);
    map.Data.Commit();
    byte[] generated = (await document.GenerateBytesAsync(CancellationToken.None).ConfigureAwait(false)).ToArray();
    using JsonDocument json = JsonDocument.Parse(generated);
    Assert(json.RootElement.GetProperty("data")[1].GetInt32() == 99, "Map tile change was not committed.");
    Assert(json.RootElement.GetProperty("events").GetArrayLength() == 1, "Events changed during map data edit.");
    document.Dispose();
}

static ContentFingerprint Fingerprint(byte[] bytes) => new(
    bytes.Length,
    DateTimeOffset.UnixEpoch,
    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

static ContentFingerprint FileFingerprint(string path, byte[] bytes) => new(
    bytes.Length,
    File.GetLastWriteTimeUtc(path),
    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

file sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PlayromaJRPG-Phase2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

file sealed class FailingReplacer : IAtomicFileReplacer
{
    public ValueTask ReplaceAsync(string temporaryPath, string destinationPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new IOException("Injected replacement failure.");
    }
}
