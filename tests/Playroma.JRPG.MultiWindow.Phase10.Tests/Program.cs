using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.MultiWindow.Phase10.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "Playroma-MultiWindow-Phase10-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await WorkspaceStateRoundTripAsync(root);
            await WorkspaceStateBackupFallbackAsync(root);
            await RecoveryGenerationFallbackAsync(root);
            await BackgroundTaskDrainAsync();
            Console.WriteLine("PASS Multi-Window Phase 10 shell tests");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Multi-Window Phase 10 shell tests: " + exception);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static async Task WorkspaceStateRoundTripAsync(string root)
    {
        string projectRoot = Path.Combine(root, "project");
        Directory.CreateDirectory(projectRoot);
        JsonProjectWorkspaceStateStore store = new(Path.Combine(root, "workspace-state"));
        ProjectWorkspaceSnapshot expected = CreateSnapshot(projectRoot, "Actors", 1);
        await store.WriteAsync(expected);
        ProjectWorkspaceSnapshot actual = await store.ReadAsync(projectRoot)
            ?? throw new InvalidOperationException("Workspace snapshot was not read back.");

        Require(actual.Version == 1, "Workspace version changed.");
        Require(actual.ProjectRoot == projectRoot, "Project root changed.");
        Require(actual.EffectiveWindows.Count == 1, "Window state count changed.");
        Require(actual.EffectiveWindows[0].Request.Kind == EditorWindowKind.Database, "Database window kind was not preserved.");
        Require(actual.EffectivePrivateSessions.Count == 1, "Private edit session was not preserved.");
        Require(actual.ActiveWindow?.Kind == EditorWindowKind.Database, "Active window was not preserved.");
    }

    private static async Task WorkspaceStateBackupFallbackAsync(string root)
    {
        string projectRoot = Path.Combine(root, "backup-project");
        Directory.CreateDirectory(projectRoot);
        string stateRoot = Path.Combine(root, "backup-workspace-state");
        JsonProjectWorkspaceStateStore store = new(stateRoot);
        ProjectWorkspaceSnapshot first = CreateSnapshot(projectRoot, "Actors", 1);
        ProjectWorkspaceSnapshot second = CreateSnapshot(projectRoot, "Items", 2);
        await store.WriteAsync(first);
        await store.WriteAsync(second);

        string current = Directory.EnumerateFiles(stateRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Single(path => !path.EndsWith(".previous", StringComparison.OrdinalIgnoreCase));
        await File.WriteAllTextAsync(current, "{corrupt");
        ProjectWorkspaceSnapshot recovered = await store.ReadAsync(projectRoot)
            ?? throw new InvalidOperationException("The previous valid workspace snapshot was not used.");
        Require(recovered.EffectiveWindows[0].CustomStateJson?.Contains("Actors", StringComparison.Ordinal) == true,
            "Backup fallback did not return the previous complete snapshot.");
    }

    private static async Task RecoveryGenerationFallbackAsync(string root)
    {
        string recoveryRoot = Path.Combine(root, "recovery-generations");
        string projectDirectory = Path.Combine(recoveryRoot, "project-key");
        string olderDirectory = Path.Combine(projectDirectory, "snapshot-20260101000000000-older");
        string newerDirectory = Path.Combine(projectDirectory, "snapshot-20260102000000000-newer");
        Directory.CreateDirectory(olderDirectory);
        Directory.CreateDirectory(newerDirectory);

        RecoveryManifest valid = new(
            Path.Combine(root, "generation-project"),
            "Generation Project",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            [],
            []);
        await File.WriteAllTextAsync(
            Path.Combine(olderDirectory, "manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(valid));
        await File.WriteAllTextAsync(Path.Combine(newerDirectory, "manifest.json"), "{corrupt");
        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "current.json"),
            System.Text.Json.JsonSerializer.Serialize(Path.GetFileName(newerDirectory)));

        using RecoveryService recovery = new(recoveryRoot);
        IReadOnlyList<RecoveryCandidate> candidates = await recovery.FindAsync();
        Require(candidates.Count == 1, "Recovery generation fallback did not find the older valid generation.");
        Require(
            Path.GetFullPath(candidates[0].DirectoryPath) == Path.GetFullPath(olderDirectory),
            "Recovery generation fallback returned the corrupt current generation.");
    }

    private static async Task BackgroundTaskDrainAsync()
    {
        await using BackgroundTaskService tasks = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task cancelTask = tasks.RunAsync(
            new BackgroundTaskDefinition("Cancelable", "Test", ShutdownBehavior: TaskShutdownBehavior.CancelAndAwait),
            async (_, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            }).AsTask();
        await started.Task;

        tasks.StopAcceptingNewTasks();
        bool rejected = false;
        try
        {
            await tasks.RunAsync(
                new BackgroundTaskDefinition("Rejected", "Test"),
                (_, _) => ValueTask.FromResult(true));
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Require(rejected, "The task service accepted work after shutdown preparation began.");

        tasks.CancelAll(task => task.Definition.ShutdownBehavior != TaskShutdownBehavior.FinishBeforeShutdown);
        Require(await tasks.AwaitIdleAsync(TimeSpan.FromSeconds(2)), "Cancelable task did not drain.");
        try { await cancelTask; }
        catch (OperationCanceledException) { }

        Task<bool> critical = tasks.RunAsync(
            new BackgroundTaskDefinition(
                "Critical save",
                "Test",
                CanCancel: false,
                ShutdownBehavior: TaskShutdownBehavior.FinishBeforeShutdown),
            async (_, cancellationToken) =>
            {
                await Task.Delay(30, cancellationToken);
                return true;
            }).AsTask();
        tasks.CancelAll(task => task.Definition.ShutdownBehavior != TaskShutdownBehavior.FinishBeforeShutdown);
        Require(await tasks.AwaitIdleAsync(TimeSpan.FromSeconds(2)), "Finish-before-shutdown task did not drain.");
        Require(await critical, "Finish-before-shutdown task did not complete successfully.");
    }

    private static ProjectWorkspaceSnapshot CreateSnapshot(string projectRoot, string category, int revision)
    {
        WindowRequest request = new(
            EditorWindowKind.Database,
            "database",
            "Database",
            Scope: WindowScope.Project,
            PlacementId: "Database");
        PrivateSessionRecoveryState privateSession = new(
            "event-edit-session",
            new WindowRequest(
                EditorWindowKind.EventEditor,
                "map:1/event:2",
                "Event 2",
                Presentation: WindowPresentation.Modal,
                Scope: WindowScope.Project),
            "Event 2",
            $"{{\"revision\":{revision}}}",
            "baseline",
            DateTimeOffset.UtcNow);
        return new ProjectWorkspaceSnapshot(
            projectRoot,
            DateTimeOffset.UtcNow,
            $"{{\"mapId\":{revision}}}",
            [new WindowWorkspaceState(
                request,
                null,
                new ShellBounds(10, 20, 900, 700),
                Maximized: false,
                MonitorDeviceName: "DISPLAY1",
                WasVisible: true,
                ActivationOrder: 0,
                CustomStateJson: $"{{\"category\":\"{category}\"}}")],
            new WindowIdentityState(EditorWindowKind.Database, "database"),
            [privateSession]);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
