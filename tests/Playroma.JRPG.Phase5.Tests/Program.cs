using System.Buffers.Binary;
using System.Text;
using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Documents.Mutable;

List<(string Name, Func<Task> Test)> tests =
[
    ("Logical asset variants", LogicalAssetVariantsAsync),
    ("Missing referenced asset", MissingReferencedAssetAsync),
    ("Structured asset queries", StructuredAssetQueriesAsync),
    ("MV sprite-sheet descriptors", SpriteSheetDescriptorsAsync),
    ("WAV metadata and streaming waveform", WavMetadataAndWaveformAsync),
    ("Exact duplicate detection", DuplicateDetectionAsync),
    ("Import undo redo and save", ImportUndoRedoSaveAsync),
    ("Atomic batch import undo redo and save", AtomicBatchImportUndoRedoSaveAsync),
    ("Rename updates exact references", RenameUpdatesReferencesAsync),
    ("Delete is staged and undoable", DeleteUndoAsync),
    ("Asset validation diagnostics", AssetValidationAsync)
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
Console.WriteLine($"{tests.Count - failures}/{tests.Count} Phase 5 tests passed.");
return failures == 0 ? 0 : 1;

static async Task LogicalAssetVariantsAsync()
{
    using TemporaryDirectory temp = new();
    WriteFile(temp.Path, "audio/bgm/Theme.ogg", [1, 2, 3]);
    WriteFile(temp.Path, "audio/bgm/Theme.m4a", [4, 5, 6, 7]);
    await using AssetCatalogueService catalogue = new(temp.Path, AssetInventory.Empty);
    await catalogue.BuildAsync().ConfigureAwait(false);
    AssetRecord record = catalogue.Find(new AssetKey(AssetCategory.BackgroundMusic, "Theme"))
        ?? throw new InvalidOperationException("Logical BGM was not created.");
    Equal(2, record.Variants.Count);
    Equal(1, catalogue.Statistics.LogicalAssetCount);
    Equal(2, catalogue.Statistics.PhysicalVariantCount);
}

static async Task MissingReferencedAssetAsync()
{
    using TemporaryDirectory temp = new();
    AssetReferenceHint hint = new(new AssetKey(AssetCategory.Characters, "MissingHero"), "data/Actors.json", "/1/characterName", true, "Actor 1");
    await using AssetCatalogueService catalogue = new(temp.Path, AssetInventory.Empty, references: [hint]);
    await catalogue.BuildAsync().ConfigureAwait(false);
    AssetRecord record = catalogue.Find(hint.Key) ?? throw new InvalidOperationException("Missing record was not created.");
    True(record.IsMissing);
    Equal(1, record.Usage.ExactReferenceCount);
    True(record.Diagnostics.Any(diagnostic => diagnostic.Code == Playroma.JRPG.Core.DiagnosticCodes.Phase5MissingAsset));
}

static Task StructuredAssetQueriesAsync()
{
    AssetQuery query = AssetQueryParser.Parse("category:characters used:true missing:false extension:png name:\"Main Hero\"");
    Equal(AssetCategory.Characters, query.Category);
    Equal(true, query.Used);
    Equal(false, query.Missing);
    Equal("png", query.Extension);
    Equal("Main Hero", query.Text);
    return Task.CompletedTask;
}

static Task SpriteSheetDescriptorsAsync()
{
    CharacterSheetDescriptor ordinary = MvSpriteSheetDescriptors.DescribeCharacter("People1", 576, 384);
    True(ordinary.IsValid);
    True(!ordinary.IsSingleCharacter);
    Equal(96, ordinary.Cells.Count);
    Equal(48, ordinary.CellWidth);
    Equal(48, ordinary.CellHeight);

    CharacterSheetDescriptor singleObject = MvSpriteSheetDescriptors.DescribeCharacter("!$Door", 144, 192);
    True(singleObject.IsValid);
    True(singleObject.IsSingleCharacter);
    True(singleObject.IsObjectCharacter);
    Equal(12, singleObject.Cells.Count);

    GridSheetDescriptor faces = MvSpriteSheetDescriptors.DescribeFaces(576, 288);
    True(faces.IsValid);
    Equal(8, faces.Cells.Count);

    GridSheetDescriptor sideView = MvSpriteSheetDescriptors.DescribeSideViewActor(576, 384);
    True(sideView.IsValid);
    Equal(54, sideView.Cells.Count);
    return Task.CompletedTask;
}

static async Task WavMetadataAndWaveformAsync()
{
    using TemporaryDirectory temp = new();
    string path = Path.Combine(temp.Path, "tone.wav");
    WritePcmWave(path, 8_000, 1, 1.0);
    AssetMetadataSummary metadata = await AssetMetadataReaders.ReadAsync(path, AssetMediaKind.Audio).ConfigureAwait(false);
    True(metadata.Audio?.IsValid == true);
    Equal(8_000, metadata.Audio?.SampleRate ?? 0);
    Equal(1, metadata.Audio?.Channels ?? 0);
    WaveformEnvelope waveform = await AssetMetadataReaders.AnalyseWaveformAsync(path, 256).ConfigureAwait(false);
    Equal(256, waveform.Buckets.Count);
    True(waveform.Buckets.Any(bucket => bucket.Maximum > 0.2f));
}

static async Task DuplicateDetectionAsync()
{
    using TemporaryDirectory temp = new();
    byte[] bytes = Enumerable.Range(0, 128).Select(index => (byte)index).ToArray();
    WriteFile(temp.Path, "img/pictures/One.png", bytes);
    WriteFile(temp.Path, "img/pictures/Two.png", bytes);
    await using AssetCatalogueService catalogue = new(temp.Path, AssetInventory.Empty);
    await catalogue.BuildAsync().ConfigureAwait(false);
    IReadOnlyList<IReadOnlyList<AssetVariant>> duplicates = await catalogue.FindExactDuplicatesAsync().ConfigureAwait(false);
    Equal(1, duplicates.Count);
    Equal(2, duplicates[0].Count);
}

static async Task ImportUndoRedoSaveAsync()
{
    using TemporaryDirectory temp = new();
    string project = CopyFixture(temp.Path, "import-project");
    string external = Path.Combine(temp.Path, "Imported.png");
    await File.WriteAllBytesAsync(external, TinyPng()).ConfigureAwait(false);
    var opened = await MvProjectSession.OpenAsync(project, analysisLoadMode: ProjectAnalysisLoadMode.Deferred).ConfigureAwait(false);
    await using MvProjectSession session = opened.Value ?? throw new InvalidOperationException("Fixture session did not open.");
    AssetKey key = new(AssetCategory.Pictures, "Imported");
    await session.AssetOperations.ImportAsync(external, key).ConfigureAwait(false);
    True(session.Editing.Files.HasPendingChanges);
    True(session.AssetContent.Exists("img/pictures/Imported.png"));
    True(session.Assets.Find(key)?.Status == AssetStatus.PendingImport);

    await session.Undo.UndoAsync().ConfigureAwait(false);
    True(!session.AssetContent.Exists("img/pictures/Imported.png"));
    await session.Undo.RedoAsync().ConfigureAwait(false);
    True(session.AssetContent.Exists("img/pictures/Imported.png"));

    var saved = await session.SaveAsync().ConfigureAwait(false);
    True(saved.Succeeded, "Save failed: " + string.Join("; ", saved.Diagnostics.Select(diagnostic => diagnostic.Message)));
    True(File.Exists(Path.Combine(project, "img", "pictures", "Imported.png")));
    True(!session.Editing.Files.HasPendingChanges);
}

static async Task AtomicBatchImportUndoRedoSaveAsync()
{
    using TemporaryDirectory temp = new();
    string project = CopyFixture(temp.Path, "batch-import-project");
    string sourceOne = Path.Combine(temp.Path, "BatchOne.png");
    string sourceTwo = Path.Combine(temp.Path, "BatchTwo.png");
    await File.WriteAllBytesAsync(sourceOne, TinyPng()).ConfigureAwait(false);
    await File.WriteAllBytesAsync(sourceTwo, TinyPng()).ConfigureAwait(false);

    var opened = await MvProjectSession.OpenAsync(project, analysisLoadMode: ProjectAnalysisLoadMode.Deferred).ConfigureAwait(false);
    await using MvProjectSession session = opened.Value ?? throw new InvalidOperationException("Fixture session did not open.");

    bool traversalBlocked = false;
    try
    {
        AssetImportPlan invalid = new([
            new AssetImportPlanItem(sourceOne, new AssetKey(AssetCategory.Pictures, "../escape"))
        ]);
        _ = await session.AssetOperations.PreviewBatchImportAsync(invalid).ConfigureAwait(false);
    }
    catch (ArgumentException)
    {
        traversalBlocked = true;
    }
    True(traversalBlocked, "Application-layer batch validation allowed a traversal logical name.");

    AssetImportPlan plan = new([
        new AssetImportPlanItem(sourceOne, new AssetKey(AssetCategory.Pictures, "BatchOne")),
        new AssetImportPlanItem(sourceTwo, new AssetKey(AssetCategory.Pictures, "BatchTwo"))
    ], "Import batch test assets");

    AssetManagementPreview preview = await session.AssetOperations.PreviewBatchImportAsync(plan).ConfigureAwait(false);
    Equal(2, preview.Transaction.ChangedFileCount);
    Equal(2, preview.FileChanges.Count);

    int before = session.Undo.UndoCount;
    await session.AssetOperations.ImportBatchAsync(plan).ConfigureAwait(false);
    Equal(before + 1, session.Undo.UndoCount);
    Equal("Import batch test assets", session.Undo.UndoDescription);
    True(session.AssetContent.Exists("img/pictures/BatchOne.png"));
    True(session.AssetContent.Exists("img/pictures/BatchTwo.png"));

    await session.Undo.UndoAsync().ConfigureAwait(false);
    True(!session.AssetContent.Exists("img/pictures/BatchOne.png"));
    True(!session.AssetContent.Exists("img/pictures/BatchTwo.png"));
    await session.Undo.RedoAsync().ConfigureAwait(false);
    True(session.AssetContent.Exists("img/pictures/BatchOne.png"));
    True(session.AssetContent.Exists("img/pictures/BatchTwo.png"));

    var saved = await session.SaveAsync().ConfigureAwait(false);
    True(saved.Succeeded, "Save failed: " + string.Join("; ", saved.Diagnostics.Select(diagnostic => diagnostic.Message)));
    True(File.Exists(Path.Combine(project, "img", "pictures", "BatchOne.png")));
    True(File.Exists(Path.Combine(project, "img", "pictures", "BatchTwo.png")));
}

static async Task RenameUpdatesReferencesAsync()
{
    using TemporaryDirectory temp = new();
    string project = CopyFixture(temp.Path, "rename-project");
    string actorsPath = Path.Combine(project, "data", "Actors.json");
    string actors = await File.ReadAllTextAsync(actorsPath).ConfigureAwait(false);
    actors = actors.Replace("\"characterName\": \"\"", "\"characterName\": \"Hero\"", StringComparison.Ordinal);
    await File.WriteAllTextAsync(actorsPath, actors, new UTF8Encoding(false)).ConfigureAwait(false);
    WriteFile(project, "img/characters/Hero.png", TinyPng());

    var opened = await MvProjectSession.OpenAsync(project).ConfigureAwait(false);
    await using MvProjectSession session = opened.Value ?? throw new InvalidOperationException("Fixture session did not open.");
    AssetKey source = new(AssetCategory.Characters, "Hero");
    AssetRecord record = session.Assets.Find(source) ?? throw new InvalidOperationException("Hero asset was not indexed.");
    True(record.Usage.ExactReferenceCount > 0, "The actor character reference was not indexed.");

    AssetManagementPreview preview = await session.AssetOperations.PreviewRenameAsync(source, "MainHero").ConfigureAwait(false);
    True(preview.ExactReferences.Count > 0);
    await session.AssetOperations.RenameAsync(source, "MainHero").ConfigureAwait(false);
    MutableJsonProjectDocument document = (MutableJsonProjectDocument)await session.Workspace.OpenDocumentAsync("data/Actors.json").ConfigureAwait(false);
    Equal("MainHero", document.GetString("/1/characterName"));
    True(session.AssetContent.Exists("img/characters/MainHero.png"));
    True(!session.AssetContent.Exists("img/characters/Hero.png"));

    await session.Undo.UndoAsync().ConfigureAwait(false);
    Equal("Hero", document.GetString("/1/characterName"));
    True(session.AssetContent.Exists("img/characters/Hero.png"));
    await session.Undo.RedoAsync().ConfigureAwait(false);
    Equal("MainHero", document.GetString("/1/characterName"));
    var saved = await session.SaveAsync().ConfigureAwait(false);
    True(saved.Succeeded);
    True(File.Exists(Path.Combine(project, "img", "characters", "MainHero.png")));
    True(!File.Exists(Path.Combine(project, "img", "characters", "Hero.png")));
}

static async Task DeleteUndoAsync()
{
    using TemporaryDirectory temp = new();
    string project = CopyFixture(temp.Path, "delete-project");
    WriteFile(project, "img/pictures/Unused.png", TinyPng());
    var opened = await MvProjectSession.OpenAsync(project, analysisLoadMode: ProjectAnalysisLoadMode.Deferred).ConfigureAwait(false);
    await using MvProjectSession session = opened.Value ?? throw new InvalidOperationException("Fixture session did not open.");
    AssetKey key = new(AssetCategory.Pictures, "Unused");
    await session.AssetOperations.DeleteAsync(key).ConfigureAwait(false);
    True(!session.AssetContent.Exists("img/pictures/Unused.png"));
    True(session.Assets.Find(key)?.Status == AssetStatus.PendingDeletion);
    await session.Undo.UndoAsync().ConfigureAwait(false);
    True(session.AssetContent.Exists("img/pictures/Unused.png"));
}

static async Task AssetValidationAsync()
{
    using TemporaryDirectory temp = new();
    WriteFile(temp.Path, "img/pictures/Empty.png", []);
    await using AssetCatalogueService catalogue = new(temp.Path, AssetInventory.Empty);
    await catalogue.BuildAsync().ConfigureAwait(false);
    AssetAuditReport audit = await new AssetCatalogueAuditor().AuditAsync(catalogue, includeDuplicateHashing: false).ConfigureAwait(false);
    True(audit.Diagnostics.Any(diagnostic => diagnostic.Code == Playroma.JRPG.Core.DiagnosticCodes.Phase5ZeroByteAsset));
}

static string CopyFixture(string root, string name)
{
    string source = Path.GetFullPath(Path.Combine("testdata", "fixtures", "database-catalog-project"));
    string destination = Path.Combine(root, name);
    Directory.CreateDirectory(destination);
    foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: true);
    }
    return destination;
}

static void WriteFile(string root, string relative, byte[] bytes)
{
    string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, bytes);
}

static byte[] TinyPng() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+X9iVAAAAAElFTkSuQmCC");

static void WritePcmWave(string path, int sampleRate, int channels, double seconds)
{
    int frames = checked((int)Math.Round(sampleRate * seconds));
    int bytesPerSample = 2;
    int dataLength = frames * channels * bytesPerSample;
    byte[] bytes = new byte[44 + dataLength];
    Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), 36 + dataLength);
    Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(bytes, 8);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), 16);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20, 2), 1);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22, 2), checked((short)channels));
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24, 4), sampleRate);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28, 4), sampleRate * channels * bytesPerSample);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32, 2), checked((short)(channels * bytesPerSample)));
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34, 2), 16);
    Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40, 4), dataLength);
    for (int frame = 0; frame < frames; frame++)
    {
        short sample = checked((short)Math.Round(Math.Sin(frame * 2 * Math.PI * 440 / sampleRate) * short.MaxValue * 0.5));
        for (int channel = 0; channel < channels; channel++)
        {
            int offset = 44 + (frame * channels + channel) * 2;
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset, 2), sample);
        }
    }
    File.WriteAllBytes(path, bytes);
}

static void True(bool condition, string message = "Assertion failed.")
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PlayromaJRPG-Phase5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
