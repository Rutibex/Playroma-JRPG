using Playroma.Sprite.Formats;
using Playroma.Sprite.Resilience;

string root = Path.Combine(Path.GetTempPath(), "PlayromaSpritePhase10-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int assertions = 0;
try
{
    TestPreferences();
    TestWorkspaces();
    await TestRecoveryAsync();
    await TestBackgroundJobsAsync();
    TestPerformanceAndAccessibility();
    TestHistoryBudget();
    Console.WriteLine($"PASS Playroma Sprite Phase 10 resilience contracts ({assertions} assertions)");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("FAIL " + exception);
    return 1;
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

void Assert(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}

void TestPreferences()
{
    string path = Path.Combine(root, "preferences.json");
    SpritePreferencesStore store = new(path);
    SpriteEditorPreferences preferences = SpriteEditorPreferences.Default();
    preferences.AutosaveSeconds = 1;
    preferences.UiScale = 99;
    store.Save(preferences);
    SpriteEditorPreferences loaded = store.LoadOrDefault();
    Assert(loaded.AutosaveSeconds == 15, "Autosave minimum was not repaired.");
    Assert(loaded.UiScale == 3d, "UI scale maximum was not repaired.");
    Assert(File.Exists(path), "Preferences were not stored atomically.");
    List<SpriteShortcutBinding> conflicts = [new() { Command = "one", Gesture = "Ctrl+K" }, new() { Command = "two", Gesture = "control+k" }];
    Assert(SpriteShortcutRegistry.Validate(conflicts).Count == 1, "Shortcut conflict was not detected.");
    Assert(SpriteShortcutRegistry.Normalize("shift+control+oemcomma") == "Ctrl+Shift+Comma", "Shortcut normalization failed.");
}

void TestWorkspaces()
{
    SpriteWorkspaceProfileStore store = new(Path.Combine(root, "workspaces"));
    SpriteWorkspaceProfile profile = SpriteWorkspaceProfileStore.Default();
    profile.Id = "My Painting Layout"; profile.Name = "My Painting Layout"; profile.MirrorInspector = true; profile.CanvasZoom = 12;
    store.Save(profile);
    SpriteWorkspaceProfile loaded = store.Load("my-painting-layout");
    Assert(loaded.MirrorInspector, "Mirrored workspace was not preserved.");
    Assert(loaded.CanvasZoom == 12, "Canvas zoom was not preserved.");
    Assert(store.LoadAll().Any(item => item.Id == "my-painting-layout"), "Saved workspace was not discoverable.");
}

async Task TestRecoveryAsync()
{
    SpritePackageData package = SpritePackageData.CreateEmpty("Recovery Test", 8, 8);
    Guid contentId = package.Document.Layers[0].Cels[0].PixelContentId;
    package.GetRequired(contentId).SetPixel(2, 3, new Playroma.Sprite.Pixel.Rgba32(10, 20, 30, 255));
    SpriteRecoveryService recovery = new(Path.Combine(root, "recovery"));
    SpriteRecoverySnapshot first = await recovery.CaptureAsync(package, "C:/project/test.playromasprite", "C:/project/img/characters/test.png", "manual", 3);
    SpritePackageData loaded = await recovery.LoadAsync(first);
    Assert(loaded.GetRequired(contentId).GetPixel(2, 3).R == 10, "Recovery package lost pixel data.");
    Assert(first.Sha256.Length == 64, "Recovery SHA-256 was not recorded.");
    for (int index = 0; index < 5; index++) await recovery.CaptureAsync(package, null, null, "autosave", 3);
    Assert(recovery.ListSnapshots(package.Document.Id).Count == 3, "Recovery retention did not prune old snapshots.");
    recovery.DeleteAll(package.Document.Id);
    Assert(recovery.ListSnapshots(package.Document.Id).Count == 0, "Recovery deletion failed.");
}

async Task TestBackgroundJobsAsync()
{
    await using ResilientBackgroundJobQueue queue = new(2);
    TaskCompletionSource<bool> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    queue.Changed += (_, _) => { if (queue.Jobs.Any(item => item.Status == SpriteBackgroundJobStatus.Completed)) completed.TrySetResult(true); };
    queue.Enqueue("Test export", async (progress, cancellationToken) =>
    {
        progress.Report(new SpriteBackgroundProgress(50, "Half"));
        await Task.Delay(20, cancellationToken);
        progress.Report(new SpriteBackgroundProgress(100, "Done"));
    });
    await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    SpriteBackgroundJobInfo job = queue.Jobs.Single();
    Assert(job.Status == SpriteBackgroundJobStatus.Completed, "Background job did not complete.");
    Assert(job.ProgressPercent == 100, "Background progress did not reach 100%.");

    TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    queue.Enqueue("Cancelable export", async (_, cancellationToken) =>
    {
        started.TrySetResult(true);
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
    }, retryCount: 0);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(queue.CancelAll() >= 1, "CancelAll did not cancel an active job.");
    for (int attempt = 0; attempt < 100 && queue.Jobs.All(item => item.Name != "Cancelable export" || item.Status != SpriteBackgroundJobStatus.Cancelled); attempt++)
        await Task.Delay(10);
    Assert(queue.Jobs.Any(item => item.Name == "Cancelable export" && item.Status == SpriteBackgroundJobStatus.Cancelled), "Cancelled job did not reach the cancelled state.");
}

void TestPerformanceAndAccessibility()
{
    SpritePerformanceMonitor monitor = new();
    using (monitor.Measure("paint")) Thread.SpinWait(1000);
    Assert(monitor.Summaries.Single().Samples == 1, "Performance sample was not recorded.");
    double contrast = SpriteAccessibilityAuditor.ContrastRatio((0, 0, 0), (255, 255, 255));
    Assert(contrast > 20d, "Contrast ratio calculation is incorrect.");
    SpritePackageData package = SpritePackageData.CreateEmpty("Large", 4096, 4096);
    Assert(SpriteLargeDocumentPolicy.Assess(package, 1_000_000).IsLarge, "Large-document policy did not activate.");
}

void TestHistoryBudget()
{
    SpritePackageHistory history = new() { MemoryLimitBytes = 16L * 1024L * 1024L };
    SpritePackageData package = SpritePackageData.CreateEmpty("History", 1024, 1024);
    for (int index = 0; index < 5; index++)
    {
        Guid contentId = package.Document.Layers[0].Cels[0].PixelContentId;
        Playroma.Sprite.Pixel.PixelBuffer before = package.GetRequired(contentId).Clone();
        package.GetRequired(contentId).SetPixel(index, index, new Playroma.Sprite.Pixel.Rgba32((byte)index, 2, 3, 255));
        history.CommitPixel("paint " + index, package, contentId, before);
    }
    Assert(history.MemoryBytes <= history.MemoryLimitBytes || history.UndoCount == 1, "Undo history did not trim to its memory budget.");
}
