using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

List<(string Name, Func<Task> Test)> tests =
[
    ("Settings version 9 round trip", SettingsRoundTripAsync),
    ("Legacy settings migration", LegacySettingsMigrationAsync),
    ("Modern settings win over legacy fields", ModernSettingsPrecedenceAsync),
    ("Future settings are not rewritten", FutureSettingsVersionAsync),
    ("Invalid settings recovery", InvalidSettingsRecoveryAsync),
    ("Recent project ordering", RecentProjectOrderingAsync),
    ("Recent project relocation", RecentProjectRelocationAsync),
    ("Shell command registry", ShellCommandRegistryAsync),
    ("Navigation history", NavigationHistoryAsync),
    ("Window key identity", WindowKeyIdentityAsync),
    ("Background task lifecycle", BackgroundTaskLifecycleAsync),
    ("Background task cancellation", BackgroundTaskCancellationAsync),
    ("Document lease protects cached document", DocumentLeaseAsync),
    ("Recovery snapshot restores modified bytes", RecoverySnapshotAsync),
    ("Legacy recovery manifest remains readable", LegacyRecoveryManifestReadAsync),
    ("Project file watcher reports relative paths", ProjectFileWatcherAsync)
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

Console.WriteLine($"{tests.Count - failures}/{tests.Count} multi-window Phase 12 shell tests passed.");
return failures == 0 ? 0 : 1;

static async Task SettingsRoundTripAsync()
{
    using TemporaryDirectory directory = new();
    string path = Path.Combine(directory.Path, "settings.json");
    JsonSettingsService writer = new(path);
    writer.Update(settings => settings with
    {
        Theme = EditorTheme.Dark,
        WindowPlacements =
        [
            new WindowPlacement("Database", new ShellBounds(50, 60, 1100, 760), true, @"\\.\DISPLAY2")
        ],
        ReopenLastProjectOnStartup = true,
        LastProjectRoot = @"C:\Games\SampleProject",
        MapEditorLayout = new MapEditorLayoutSettings(
            LeftPanelWidth: 336,
            PalettePanelHeight: 448,
            MinimapVisible: false,
            PaletteZoomPercent: 125,
            SelectedPaletteTab: "C",
            OverlayFlags: 49,
            ExpandedMapIds: [1, 4, 9],
            ActiveMapId: 9,
            SearchText: "castle",
            MapViews:
            [
                new MapEditorMapViewSettings(9, 1.5f, 24, 18, "Events", "Events", "Automatic", 2, 2048, "A2", 49, false)
            ])
    });
    await writer.SaveAsync().ConfigureAwait(false);

    JsonSettingsService reader = new(path);
    await reader.LoadAsync().ConfigureAwait(false);
    Equal(EditorTheme.Dark, reader.Current.Theme);
    Equal(1, reader.Current.EffectiveWindowPlacements.Count);
    Equal(1100, reader.Current.EffectiveWindowPlacements[0].NormalBounds.Width);
    True(reader.Current.EffectiveWindowPlacements[0].Maximized);
    True(reader.Current.ReopenLastProjectOnStartup);
    Equal(@"C:\Games\SampleProject", reader.Current.LastProjectRoot);
    Equal(336, reader.Current.EffectiveMapEditorLayout.LeftPanelWidth);
    Equal(9, reader.Current.EffectiveMapEditorLayout.ActiveMapId ?? -1);
    Equal("C", reader.Current.EffectiveMapEditorLayout.SelectedPaletteTab);
    Equal(1, reader.Current.EffectiveMapEditorLayout.EffectiveMapViews.Count);
    Equal(EditorSettingsMigrator.CurrentVersion, reader.Current.Version);
}

static async Task LegacySettingsMigrationAsync()
{
    using TemporaryDirectory directory = new();
    string path = Path.Combine(directory.Path, "settings.json");
    string legacy = """
    {
      "Theme": "Dark",
      "MainWindowBounds": { "X": 10, "Y": 20, "Width": 1280, "Height": 800 },
      "MainWindowMaximized": true,
      "Layout": { "LeftProportion": 0.25, "RightProportion": 0.22, "BottomProportion": 0.24 },
      "Preferences": { "UiScalePercent": 125, "UiDensity": "Comfortable", "Version": 2 },
      "Version": 8
    }
    """;
    await File.WriteAllTextAsync(path, legacy).ConfigureAwait(false);
    JsonSettingsService service = new(path);
    await service.LoadAsync().ConfigureAwait(false);
    Equal(EditorSettingsMigrator.CurrentVersion, service.Current.Version);
    WindowPlacement placement = service.Current.EffectiveWindowPlacements.Single(item => item.PlacementId == "MapEditor");
    Equal(1280, placement.NormalBounds.Width);
    True(placement.Maximized);
    Equal(320, service.Current.EffectiveMapEditorLayout.LeftPanelWidth);
    Equal(125, service.Current.EffectivePreferences.UiScalePercent);
    True(File.Exists(Path.Combine(directory.Path, EditorSettingsMigrator.LegacyBackupFileName)), "Legacy settings backup was not created.");
    string rewritten = await File.ReadAllTextAsync(path).ConfigureAwait(false);
    True(!rewritten.Contains("MainWindowBounds", StringComparison.Ordinal), "Legacy main-window state was written back to settings.");
    True(!rewritten.Contains("\"Layout\"", StringComparison.Ordinal), "Legacy dock layout was written back to settings.");
}


static async Task ModernSettingsPrecedenceAsync()
{
    using TemporaryDirectory directory = new();
    string path = Path.Combine(directory.Path, "settings.json");
    string source = """
    {
      "MainWindowBounds": { "X": 1, "Y": 2, "Width": 900, "Height": 600 },
      "MainWindowMaximized": true,
      "Layout": { "LeftProportion": 0.40 },
      "WindowPlacements": [
        { "PlacementId": "MapEditor", "NormalBounds": { "X": 40, "Y": 50, "Width": 1440, "Height": 900 }, "Maximized": false, "Version": 1 }
      ],
      "MapEditorLayout": { "LeftPanelWidth": 410, "PalettePanelHeight": 420, "Version": 2 },
      "Version": 8
    }
    """;
    await File.WriteAllTextAsync(path, source).ConfigureAwait(false);
    JsonSettingsService service = new(path);
    await service.LoadAsync().ConfigureAwait(false);
    WindowPlacement placement = service.Current.EffectiveWindowPlacements.Single(item => item.PlacementId == "MapEditor");
    Equal(1440, placement.NormalBounds.Width);
    True(!placement.Maximized, "Legacy maximized state replaced a modern placement.");
    Equal(410, service.Current.EffectiveMapEditorLayout.LeftPanelWidth);
}

static async Task FutureSettingsVersionAsync()
{
    using TemporaryDirectory directory = new();
    string path = Path.Combine(directory.Path, "settings.json");
    string source = """
    {
      "Theme": "Dark",
      "Preferences": { "UiScalePercent": 150, "UiDensity": "Comfortable", "Version": 2 },
      "Version": 99
    }
    """;
    await File.WriteAllTextAsync(path, source).ConfigureAwait(false);
    JsonSettingsService service = new(path);
    await service.LoadAsync().ConfigureAwait(false);
    Equal(99, service.Current.Version);
    Equal(150, service.Current.EffectivePreferences.UiScalePercent);
    Equal(source, await File.ReadAllTextAsync(path).ConfigureAwait(false));
    True(!File.Exists(Path.Combine(directory.Path, EditorSettingsMigrator.LegacyBackupFileName)), "Future settings unexpectedly created a migration backup.");
}

static async Task InvalidSettingsRecoveryAsync()
{
    using TemporaryDirectory directory = new();
    string path = Path.Combine(directory.Path, "settings.json");
    await File.WriteAllTextAsync(path, "{not-json").ConfigureAwait(false);
    JsonSettingsService settings = new(path);
    await settings.LoadAsync().ConfigureAwait(false);
    Equal(EditorTheme.System, settings.Current.Theme);
    True(!File.Exists(path), "Invalid settings file was not moved aside.");
    True(Directory.EnumerateFiles(directory.Path, "settings.json.invalid-*").Any(), "Invalid settings backup was not created.");
}

static async Task RecentProjectOrderingAsync()
{
    using TemporaryDirectory directory = new();
    string first = Path.Combine(directory.Path, "One");
    string second = Path.Combine(directory.Path, "Two");
    Directory.CreateDirectory(first);
    Directory.CreateDirectory(second);
    string path = Path.Combine(directory.Path, "recent.json");
    RecentProjectService service = new(path);
    await service.RecordAsync(new RecentProject(first, "One", DateTimeOffset.UtcNow.AddMinutes(-5), MvProjectLayoutKind.SourceRoot, true)).ConfigureAwait(false);
    await service.RecordAsync(new RecentProject(second, "Two", DateTimeOffset.UtcNow, MvProjectLayoutKind.SourceRoot, true)).ConfigureAwait(false);
    Equal(second, service.RecentProjects[0].ProjectRoot);

    RecentProjectService reloaded = new(path);
    await reloaded.LoadAsync().ConfigureAwait(false);
    Equal(2, reloaded.RecentProjects.Count);
    Equal(second, reloaded.RecentProjects[0].ProjectRoot);
}


static async Task RecentProjectRelocationAsync()
{
    using TemporaryDirectory directory = new();
    string missing = Path.Combine(directory.Path, "Missing");
    string relocated = Path.Combine(directory.Path, "Relocated");
    Directory.CreateDirectory(relocated);
    string path = Path.Combine(directory.Path, "recent.json");
    RecentProjectService service = new(path);
    int changes = 0;
    service.Changed += (_, _) => changes++;
    await service.RecordAsync(new RecentProject(
        missing,
        "Relocatable",
        DateTimeOffset.UtcNow,
        MvProjectLayoutKind.SourceRoot,
        false)).ConfigureAwait(false);
    await service.RelocateAsync(missing, relocated).ConfigureAwait(false);
    Equal(Path.GetFullPath(relocated), service.RecentProjects.Single().ProjectRoot);
    True(service.RecentProjects.Single().PathExists);
    True(changes >= 2, "Recent project changes were not published.");
}

static async Task ShellCommandRegistryAsync()
{
    await using ShellServiceContainer services = new();
    ShellCommandService commands = new(services);
    bool enabled = false;
    int executions = 0;
    commands.Register(new DelegateShellCommand(
        "Test.Command",
        "Test Command",
        (_, _) =>
        {
            executions++;
            return ValueTask.CompletedTask;
        },
        _ => enabled));

    True(!commands.CanExecute("Test.Command"));
    await commands.ExecuteAsync("Test.Command").ConfigureAwait(false);
    Equal(0, executions);
    enabled = true;
    commands.Invalidate();
    await commands.ExecuteAsync("Test.Command").ConfigureAwait(false);
    Equal(1, executions);
    bool duplicateRejected = false;
    try
    {
        commands.Register(new DelegateShellCommand("Test.Command", "Duplicate", (_, _) => ValueTask.CompletedTask));
    }
    catch (InvalidOperationException)
    {
        duplicateRejected = true;
    }
    True(duplicateRejected, "Duplicate command ID was accepted.");
}

static Task NavigationHistoryAsync()
{
    NavigationService navigation = new();
    NavigationLocation first = new(new DocumentRequest(EditorDocumentKind.ProjectSummary, "summary"));
    NavigationLocation second = new(new DocumentRequest(EditorDocumentKind.MapProperties, "map:1", MapId: 1));
    NavigationLocation third = new(new DocumentRequest(EditorDocumentKind.Database, "actors", "data/Actors.json", MvDocumentKind.Actors, 1));
    navigation.Navigate(first);
    navigation.Navigate(second);
    navigation.Navigate(third);
    True(navigation.CanGoBack);
    Equal(second, navigation.GoBack());
    Equal(first, navigation.GoBack());
    True(navigation.CanGoForward);
    Equal(second, navigation.GoForward());
    return Task.CompletedTask;
}


static Task WindowKeyIdentityAsync()
{
    WindowKey first = new(EditorWindowKind.EventEditor, @"Map:138\Event:71");
    WindowKey second = new(EditorWindowKind.EventEditor, "map:138/event:71");
    WindowKey different = new(EditorWindowKind.EventEditor, "map:138/event:72");
    True(first == second, "Window keys should be case-insensitive and slash-normalized.");
    True(first != different);

    Dictionary<WindowKey, string> registry = [];
    registry[first] = "open";
    Equal("open", registry[second]);

    WindowRequest request = new(
        EditorWindowKind.Database,
        "database",
        DisplayTitle: "Database",
        PlacementId: "Database.Main");
    Equal(new WindowKey(EditorWindowKind.Database, "DATABASE"), request.Key);
    Equal("Database.Main", request.EffectivePlacementId);
    return Task.CompletedTask;
}

static async Task BackgroundTaskLifecycleAsync()
{
    using BackgroundTaskService service = new();
    int result = await service.RunAsync(
        new BackgroundTaskDefinition("Count", "Tests"),
        (progress, token) =>
        {
            token.ThrowIfCancellationRequested();
            progress.Report(new TaskProgress(1, 2, "one", "Halfway"));
            progress.Report(new TaskProgress(2, 2, "two", "Done"));
            return ValueTask.FromResult(42);
        }).ConfigureAwait(false);
    Equal(42, result);
    BackgroundTaskInfo task = service.Tasks.Single();
    Equal(BackgroundTaskState.Completed, task.State);
    Equal(100.0, task.Progress?.Percentage ?? -1.0);
    service.ClearCompleted();
    Equal(0, service.Tasks.Count);
}

static async Task BackgroundTaskCancellationAsync()
{
    using BackgroundTaskService service = new();
    Task started = service.RunAsync(
        new BackgroundTaskDefinition("Wait", "Tests"),
        async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            return true;
        }).AsTask();

    BackgroundTaskInfo task = await WaitForAsync(
        () => service.Tasks.SingleOrDefault(item => item.State == BackgroundTaskState.Running),
        TimeSpan.FromSeconds(3)).ConfigureAwait(false);
    service.Cancel(task.Id);
    bool cancelled = false;
    try
    {
        await started.ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        cancelled = true;
    }
    True(cancelled, "Background operation was not cancelled.");
    Equal(BackgroundTaskState.Cancelled, service.Tasks.Single().State);
}

static async Task DocumentLeaseAsync()
{
    string fixture = Path.GetFullPath(Path.Combine("testdata", "fixtures", "database-catalog-project"));
    var opened = await MvProjectWorkspace.OpenAsync(
        fixture,
        new DocumentCacheOptions(MaximumCleanDocumentBytes: 1, MaximumOpenMapDocuments: 0)).ConfigureAwait(false);
    await using MvProjectWorkspace workspace = opened.Value ?? throw new InvalidOperationException("Fixture workspace did not load.");
    await using IProjectDocumentLease lease = await workspace.AcquireDocumentAsync("data/Actors.json").ConfigureAwait(false);
    object leasedDocument = lease.Document;
    await workspace.OpenDocumentAsync("data/Classes.json").ConfigureAwait(false);
    object reopened = await workspace.OpenDocumentAsync("data/Actors.json").ConfigureAwait(false);
    True(ReferenceEquals(leasedDocument, reopened), "Leased document was evicted from the workspace cache.");
}

static async Task RecoverySnapshotAsync()
{
    using TemporaryDirectory directory = new();
    string project = Path.Combine(directory.Path, "project");
    CopyDirectory(Path.GetFullPath(Path.Combine("testdata", "fixtures", "database-catalog-project")), project);
    var opened = await MvProjectSession.OpenAsync(
        project,
        analysisLoadMode: ProjectAnalysisLoadMode.Deferred).ConfigureAwait(false);
    await using MvProjectSession session = opened.Value ?? throw new InvalidOperationException("Fixture session did not load.");
    await using (ProjectTransaction transaction = session.BeginTransaction("Rename actor for recovery"))
    {
        transaction.Add(new SetJsonStringCommand("data/Actors.json", "/1/name", "Recovered Hero"));
        await transaction.CommitAsync().ConfigureAwait(false);
    }

    RecoveryService recovery = new(Path.Combine(directory.Path, "recovery"));
    RecoveryCandidate candidate = await recovery.WriteAsync(session).ConfigureAwait(false)
        ?? throw new InvalidOperationException("Recovery data was not written.");

    session.Workspace.DiscardAllChanges();
    MutableJsonProjectDocument actors = (MutableJsonProjectDocument)await session.Workspace.OpenDocumentAsync("data/Actors.json").ConfigureAwait(false);
    True(!string.Equals(actors.GetString("/1/name"), "Recovered Hero", StringComparison.Ordinal));
    await recovery.RestoreAsync(session, candidate).ConfigureAwait(false);
    Equal("Recovered Hero", actors.GetString("/1/name"));
    Equal(0, candidate.Manifest.LegacyOpenDocuments.Count);
    True(candidate.Manifest.OpenDocuments is null, "New recovery manifests must not write the retired document list.");
    Equal(4, candidate.Manifest.Version);
}


static Task LegacyRecoveryManifestReadAsync()
{
    string json = """
    {
      "ProjectRoot": "C:\\Games\\LegacyProject",
      "ProjectTitle": "Legacy Project",
      "CreatedUtc": "2026-07-01T12:00:00+00:00",
      "Documents": [],
      "OpenDocuments": [
        { "Kind": "Database", "Identity": "database", "RelativePath": "data/Actors.json", "DisplayTitle": "Database" },
        { "Kind": "MapView", "Identity": "map:3", "RelativePath": "data/Map003.json", "MapId": 3, "DisplayTitle": "Map 003" }
      ],
      "FileMutations": [],
      "Version": 3
    }
    """;
    RecoveryManifest manifest = JsonSerializer.Deserialize<RecoveryManifest>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    }) ?? throw new InvalidOperationException("Legacy recovery manifest did not deserialize.");
    Equal(3, manifest.Version);
    Equal(2, manifest.LegacyOpenDocuments.Count);
    Equal(EditorDocumentKind.MapView, manifest.LegacyOpenDocuments[1].Kind);
    return Task.CompletedTask;
}

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
    }
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: true);
    }
}

static async Task ProjectFileWatcherAsync()
{
    using TemporaryDirectory directory = new();
    using ProjectFileWatchService watcher = new();
    TaskCompletionSource<ProjectFileChangedEventArgs> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    watcher.FileChanged += (_, args) =>
    {
        if (string.Equals(args.RelativePath, "data/Actors.json", StringComparison.OrdinalIgnoreCase)) completion.TrySetResult(args);
    };
    watcher.Start(directory.Path);
    Directory.CreateDirectory(Path.Combine(directory.Path, "data"));
    await File.WriteAllTextAsync(Path.Combine(directory.Path, "data", "Actors.json"), "[]").ConfigureAwait(false);
    ProjectFileChangedEventArgs changed = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
    Equal("data/Actors.json", changed.RelativePath);
}

static async Task<T> WaitForAsync<T>(Func<T?> valueFactory, TimeSpan timeout) where T : class
{
    DateTimeOffset end = DateTimeOffset.UtcNow.Add(timeout);
    while (DateTimeOffset.UtcNow < end)
    {
        T? value = valueFactory();
        if (value is not null) return value;
        await Task.Delay(20).ConfigureAwait(false);
    }
    throw new TimeoutException("Timed out waiting for the expected value.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void True(bool condition, string? message = null)
{
    if (!condition) throw new InvalidOperationException(message ?? "Expected condition to be true.");
}

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PlayromaJRPG-Phase4-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A delayed FileSystemWatcher notification may retain a handle very briefly.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup failure does not invalidate the behavior being tested.
        }
    }
}
